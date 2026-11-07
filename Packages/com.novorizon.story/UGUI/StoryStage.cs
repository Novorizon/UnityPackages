using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Localization;
using Game.Framework.Localization.UGUI;
using Game.Framework.Story.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.Framework.Story.UGUI
{
    [Serializable]
    public sealed class StoryPortraitSlot
    {
        public string id;
        public Image image;
    }

    /// <summary>A presentation component; the core StoryPlayer owns flow and terminal results.</summary>
    public sealed class StoryStage : MonoBehaviour, IStoryPresenter
    {
        [SerializeField] private TMP_Text speaker;
        [SerializeField] private TMP_Text body;
        [SerializeField] private GameObject dialogueRoot;
        [SerializeField] private Image background;
        [SerializeField] private StoryPortraitSlot[] portraits = Array.Empty<StoryPortraitSlot>();
        [SerializeField] private Transform choiceRoot;
        [SerializeField] private Button choicePrefab;
        [SerializeField] private Button advanceButton;
        [SerializeField] private AudioSource voiceSource;
        [SerializeField] private MonoBehaviour videoProvider;
        [SerializeField, Min(0)] private float charactersPerSecond = 40;
        [SerializeField, Min(.1f)] private float videoPrepareTimeoutSeconds = 15;

        private sealed class ChoiceButton
        {
            internal Button button;
            internal UnityAction action;
        }

        private readonly List<ChoiceButton> buttons = new List<ChoiceButton>();
        private readonly Dictionary<string, StoryAssetLease<Sprite>> portraitLeases = new Dictionary<string, StoryAssetLease<Sprite>>(StringComparer.Ordinal);
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private CancellationTokenSource stepCancellation;
        private TaskCompletionSource<StoryStepOutcome> waiter;
        private CancellationTokenRegistration waitingCancellation;
        private StoryAssetLease<Sprite> backgroundLease;
        private StoryAssetLease<AudioClip> voiceLease;
        private IStoryAssets assets;
        private IStoryVideo video;
        private LocalizationManager localization;
        private StoryStepContext context;
        private StoryNode currentNode;
        private Guid sessionId;
        private bool open, typing, ownsVoiceSource, destroyed;
        private int totalCharacters;
        private float visibleCharacters;
        private double automaticRemaining;
        private UnityAction advanceAction;
        private LocalizedText speakerBinding, bodyBinding;
        private int ownerThreadId;
        private Exception cancellationFailure;

        public CancellationToken LifetimeToken => lifetime.Token;
        public bool IsOpen => open;
        public string CurrentNodeId => context?.NodeId;
        public int OwnedAssetCount => portraitLeases.Count + (backgroundLease == null ? 0 : 1) + (voiceLease == null ? 0 : 1);
        public bool IsRevealing => typing;

        public void Configure(IStoryAssets provider, LocalizationManager translations = null, IStoryVideo media = null)
        {
            MainThread();
            if (open)
            {
                throw new InvalidOperationException("Cannot reconfigure an open story stage.");
            }
            assets = provider ?? throw new ArgumentNullException(nameof(provider));
            localization = translations;
            video = media ?? videoProvider as IStoryVideo;
        }

        public void ConfigureUi(
            TMP_Text speakerLabel,
            TMP_Text bodyLabel,
            Image backgroundImage,
            StoryPortraitSlot[] portraitSlots,
            Transform choices,
            Button optionPrefab,
            Button advance,
            GameObject dialogue = null)
        {
            if (open)
            {
                throw new InvalidOperationException("Cannot change UI while a story is open.");
            }
            speaker = speakerLabel;
            body = bodyLabel;
            background = backgroundImage;
            portraits = portraitSlots ?? Array.Empty<StoryPortraitSlot>();
            choiceRoot = choices;
            choicePrefab = optionPrefab;
            advanceButton = advance;
            dialogueRoot = dialogue;
        }

        public void ConfigureTiming(float typingCharactersPerSecond, float prepareTimeoutSeconds = 15)
        {
            if (float.IsNaN(typingCharactersPerSecond) || float.IsInfinity(typingCharactersPerSecond) || typingCharactersPerSecond < 0 ||
                float.IsNaN(prepareTimeoutSeconds) || float.IsInfinity(prepareTimeoutSeconds) || prepareTimeoutSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(typingCharactersPerSecond));
            }
            charactersPerSecond = typingCharactersPerSecond;
            videoPrepareTimeoutSeconds = prepareTimeoutSeconds;
        }

        public Task OpenAsync(StorySequence sequence, Guid id, CancellationToken cancellationToken)
        {
            MainThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (destroyed || !isActiveAndEnabled)
            {
                throw new InvalidOperationException("The story stage must be enabled before playback.");
            }
            if (open)
            {
                throw new InvalidOperationException("The stage already owns a story session.");
            }
            if (dialogueRoot != null && (dialogueRoot == gameObject || transform.IsChildOf(dialogueRoot.transform)))
            {
                throw new InvalidOperationException("Dialogue content must not contain or be the StoryStage owner.");
            }
            if (assets == null || body == null)
            {
                throw new InvalidOperationException("Configure the stage asset provider and TMP body before playback.");
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in portraits)
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.id) || slot.image == null || !seen.Add(slot.id))
                {
                    throw new InvalidOperationException("Portrait slots need unique IDs and Image references.");
                }
            }
            sessionId = id;
            cancellationFailure = null;
            open = true;
            if (localization == null || !localization.IsInitialized)
            {
                LocalizationManager.TryGetInstance(out localization);
            }
            if (localization != null && localization.IsInitialized)
            {
                localization.LanguageChanged += OnLanguageChanged;
            }
            SetDialogueVisible(false);
            ClearText();
            return Task.CompletedTask;
        }

        public async Task<StoryStepOutcome> ExecuteAsync(StoryNode node, StoryStepContext step, CancellationToken cancellationToken)
        {
            MainThread();
            if (!open || step.SessionId != sessionId || stepCancellation != null)
            {
                throw new InvalidOperationException("The stage does not own this step or is still presenting another step.");
            }
            context = step;
            currentNode = node;
            stepCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
            CancellationToken token = stepCancellation.Token;
            Exception executionError = null;
            try
            {
                token.ThrowIfCancellationRequested();
                switch (node.Kind)
                {
                    case StoryNodeKind.SetPortrait:
                        await SetPortraitAsync(node, step, token);
                        return StoryStepOutcome.Continue;
                    case StoryNodeKind.SetBackground:
                        await SetBackgroundAsync(node, step, token);
                        return StoryStepOutcome.Continue;
                    case StoryNodeKind.Say:
                        return await SayAsync(node, step, token);
                    case StoryNodeKind.Choice:
                        return await ChooseAsync(node, step, token);
                    case StoryNodeKind.PlayVideo:
                        if (video == null)
                        {
                            throw new InvalidOperationException("Configure an IStoryVideo adapter before video steps.");
                        }
                        SetDialogueVisible(false);
                        ClearText();
                        await video.PlayAsync(node.ResourceId, videoPrepareTimeoutSeconds, node.CanSkip, token);
                        EnsureCurrent(step, token);
                        return StoryStepOutcome.Continue;
                    default:
                        throw new InvalidOperationException("The core handles this node kind: " + node.Kind);
                }
            }
            catch (Exception error)
            {
                executionError = error;
                throw;
            }
            finally
            {
                MainThread();
                var failures = new List<Exception>();
                if (cancellationFailure != null)
                {
                    failures.Add(cancellationFailure);
                }
                var retired = stepCancellation;
                stepCancellation = null;
                context = null;
                currentNode = null;
                waiter = null;
                typing = false;
                Release(waitingCancellation.Dispose, failures);
                Release(RemoveAdvance, failures);
                Release(ClearChoices, failures);
                Release(StopVoice, failures);
                Release(retired.Dispose, failures);
                if (failures.Count != 0)
                {
                    if (executionError != null)
                    {
                        failures.Insert(0, executionError);
                    }
                    throw new AggregateException("Story step cleanup failed.", failures);
                }
            }
        }

        public void Advance(StoryStepContext step)
        {
            MainThread();
            if (!Matches(step) || currentNode.Kind != StoryNodeKind.Say || waiter == null)
            {
                return;
            }
            if (typing)
            {
                RevealAll();
            }
            else
            {
                waiter.TrySetResult(StoryStepOutcome.Continue);
            }
        }

        public bool Choose(StoryStepContext step, string choiceId)
        {
            MainThread();
            if (!Matches(step) || currentNode.Kind != StoryNodeKind.Choice || waiter == null)
            {
                return false;
            }
            foreach (var choice in currentNode.Choices)
            {
                if (string.Equals(choice.Id, choiceId, StringComparison.Ordinal))
                {
                    return waiter.TrySetResult(StoryStepOutcome.Choose(choiceId));
                }
            }
            return false;
        }

        public bool SkipVideo()
        {
            MainThread();
            return open && currentNode?.Kind == StoryNodeKind.PlayVideo && video != null && video.SkipCurrent();
        }

        public Task CloseAsync(Guid id)
        {
            MainThread();
            if (!open || id != sessionId)
            {
                return Task.CompletedTask;
            }
            open = false;
            context = null;
            sessionId = Guid.Empty;
            var failures = new List<Exception>();
            if (cancellationFailure != null)
            {
                failures.Add(cancellationFailure);
            }
            if (localization != null)
            {
                localization.LanguageChanged -= OnLanguageChanged;
            }
            Release(RemoveAdvance, failures);
            Release(ClearChoices, failures);
            Release(() => video?.Stop(), failures);
            Release(StopVoice, failures);
            ClearText();
            SetDialogueVisible(false);
            if (background != null)
            {
                background.sprite = null;
                background.enabled = false;
            }
            Release(() => backgroundLease?.Dispose(), failures);
            backgroundLease = null;
            foreach (var slot in portraits)
            {
                if (slot?.image != null)
                {
                    slot.image.sprite = null;
                    slot.image.enabled = false;
                }
            }
            foreach (var lease in portraitLeases.Values)
            {
                Release(lease.Dispose, failures);
            }
            portraitLeases.Clear();
            return failures.Count == 0 ? Task.CompletedTask : Task.FromException(new AggregateException("Story stage cleanup failed.", failures));
        }

        private async Task SetPortraitAsync(StoryNode node, StoryStepContext step, CancellationToken token)
        {
            StoryPortraitSlot selected = null;
            foreach (var slot in portraits)
            {
                if (string.Equals(slot.id, node.SlotId, StringComparison.Ordinal))
                {
                    selected = slot;
                    break;
                }
            }
            if (selected == null)
            {
                throw new InvalidOperationException("Unknown portrait slot: " + node.SlotId);
            }
            StoryAssetLease<Sprite> next = null;
            try
            {
                if (!string.IsNullOrEmpty(node.ResourceId))
                {
                    next = await assets.LoadAsync<Sprite>(node.ResourceId, token);
                    if (next == null)
                    {
                        throw new InvalidOperationException("The story asset provider returned a null lease.");
                    }
                }
                EnsureCurrent(step, token);
                selected.image.sprite = next?.Asset;
                selected.image.enabled = next != null;
                portraitLeases.TryGetValue(node.SlotId, out var previous);
                portraitLeases.Remove(node.SlotId);
                if (next != null)
                {
                    portraitLeases.Add(node.SlotId, next);
                    next = null;
                }
                previous?.Dispose();
            }
            finally
            {
                next?.Dispose();
            }
        }

        private async Task SetBackgroundAsync(StoryNode node, StoryStepContext step, CancellationToken token)
        {
            if (background == null)
            {
                throw new InvalidOperationException("Configure a background Image before background steps.");
            }
            StoryAssetLease<Sprite> next = null;
            try
            {
                if (!string.IsNullOrEmpty(node.ResourceId))
                {
                    next = await assets.LoadAsync<Sprite>(node.ResourceId, token);
                    if (next == null)
                    {
                        throw new InvalidOperationException("The story asset provider returned a null lease.");
                    }
                }
                EnsureCurrent(step, token);
                background.sprite = next?.Asset;
                background.enabled = next != null;
                var previous = backgroundLease;
                backgroundLease = next;
                next = null;
                previous?.Dispose();
            }
            finally
            {
                next?.Dispose();
            }
        }

        private async Task<StoryStepOutcome> SayAsync(StoryNode node, StoryStepContext step, CancellationToken token)
        {
            if (!string.IsNullOrEmpty(node.ResourceId))
            {
                voiceLease = await assets.LoadAsync<AudioClip>(node.ResourceId, token);
                if (voiceLease == null)
                {
                    throw new InvalidOperationException("The story asset provider returned a null voice lease.");
                }
                EnsureCurrent(step, token);
                if (voiceSource == null)
                {
                    voiceSource = new GameObject("Story Voice", typeof(AudioSource)).GetComponent<AudioSource>();
                    voiceSource.transform.SetParent(transform, false);
                    ownsVoiceSource = true;
                }
                voiceSource.clip = voiceLease.Asset;
                voiceSource.loop = false;
                voiceSource.Play();
            }
            EnsureCurrent(step, token);
            SetDialogueVisible(true);
            BindText(body, ref bodyBinding, node.TextKey);
            BindText(speaker, ref speakerBinding, node.SpeakerKey);
            body.maxVisibleCharacters = int.MaxValue;
            body.ForceMeshUpdate();
            totalCharacters = body.textInfo.characterCount;
            visibleCharacters = 0;
            typing = charactersPerSecond > 0 && totalCharacters > 0;
            body.maxVisibleCharacters = typing ? 0 : int.MaxValue;
            automaticRemaining = node.DurationSeconds;
            waiter = NewWaiter(token);
            if (advanceButton != null)
            {
                advanceAction = () => Advance(step);
                advanceButton.onClick.AddListener(advanceAction);
            }
            return await waiter.Task;
        }

        private async Task<StoryStepOutcome> ChooseAsync(StoryNode node, StoryStepContext step, CancellationToken token)
        {
            if (choiceRoot == null || choicePrefab == null)
            {
                throw new InvalidOperationException("Configure a choice container and Button prefab before Choice steps.");
            }
            SetDialogueVisible(true);
            ClearText();
            waiter = NewWaiter(token);
            foreach (var choice in node.Choices)
            {
                EnsureCurrent(step, token);
                Button button = Instantiate(choicePrefab, choiceRoot, false);
                button.gameObject.SetActive(false);
                TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
                if (label == null)
                {
                    Destroy(button.gameObject);
                    throw new InvalidOperationException("Choice prefab requires a TMP label.");
                }
                LocalizedText binding = null;
                BindText(label, ref binding, choice.TextKey);
                string id = choice.Id;
                UnityAction action = () => Choose(step, id);
                button.onClick.AddListener(action);
                buttons.Add(new ChoiceButton { button = button, action = action });
                button.gameObject.SetActive(true);
            }
            return await waiter.Task;
        }

        private TaskCompletionSource<StoryStepOutcome> NewWaiter(CancellationToken token)
        {
            var completion = new TaskCompletionSource<StoryStepOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Cancellation may originate off-thread. Unity changes happen only after the captured-context await.
            waitingCancellation = token.Register(() => completion.TrySetCanceled(token));
            return completion;
        }

        private void Update()
        {
            if (!open || currentNode?.Kind != StoryNodeKind.Say || waiter == null || stepCancellation.IsCancellationRequested)
            {
                return;
            }
            if (typing)
            {
                visibleCharacters += charactersPerSecond * Time.unscaledDeltaTime;
                body.maxVisibleCharacters = Math.Min(totalCharacters, (int)visibleCharacters);
                if (visibleCharacters >= totalCharacters)
                {
                    RevealAll();
                }
            }
            else if (currentNode.DurationSeconds > 0)
            {
                automaticRemaining -= Time.unscaledDeltaTime;
                if (automaticRemaining <= 0)
                {
                    waiter.TrySetResult(StoryStepOutcome.Continue);
                }
            }
        }

        private void RevealAll()
        {
            typing = false;
            visibleCharacters = totalCharacters;
            body.maxVisibleCharacters = int.MaxValue;
        }

        private void BindText(TMP_Text label, ref LocalizedText binding, string key)
        {
            if (label == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(key))
            {
                if (binding != null)
                {
                    binding.enabled = false;
                }
                label.text = string.Empty;
                return;
            }
            if (localization != null && localization.IsInitialized)
            {
                binding = label.GetComponent<LocalizedText>();
                if (binding == null)
                {
                    binding = label.gameObject.AddComponent<LocalizedText>();
                }
                binding.Bind(localization, key);
                binding.enabled = true;
            }
            else
            {
                if (binding != null)
                {
                    binding.enabled = false;
                }
                label.text = key;
            }
        }

        private void OnLanguageChanged(string language)
        {
            if (!open || currentNode?.Kind != StoryNodeKind.Say || body == null)
            {
                return;
            }
            float fraction = totalCharacters == 0 ? 1 : Math.Min(1, visibleCharacters / totalCharacters);
            bodyBinding?.Refresh();
            speakerBinding?.Refresh();
            body.ForceMeshUpdate();
            totalCharacters = body.textInfo.characterCount;
            visibleCharacters = fraction * totalCharacters;
            body.maxVisibleCharacters = typing ? (int)visibleCharacters : int.MaxValue;
        }

        private void ClearText()
        {
            if (bodyBinding != null)
            {
                bodyBinding.enabled = false;
            }
            if (speakerBinding != null)
            {
                speakerBinding.enabled = false;
            }
            if (body != null)
            {
                body.text = string.Empty;
                body.maxVisibleCharacters = int.MaxValue;
            }
            if (speaker != null)
            {
                speaker.text = string.Empty;
            }
        }

        private void SetDialogueVisible(bool visible)
        {
            if (dialogueRoot != null)
            {
                dialogueRoot.SetActive(visible);
            }
        }

        private void StopVoice()
        {
            if (voiceSource != null)
            {
                voiceSource.Stop();
                voiceSource.clip = null;
            }
            var previous = voiceLease;
            voiceLease = null;
            previous?.Dispose();
        }

        private void ClearChoices()
        {
            foreach (var choice in buttons)
            {
                if (choice.button != null)
                {
                    choice.button.onClick.RemoveListener(choice.action);
                    choice.button.gameObject.SetActive(false);
                    Destroy(choice.button.gameObject);
                }
            }
            buttons.Clear();
        }

        private void RemoveAdvance()
        {
            if (advanceButton != null && advanceAction != null)
            {
                advanceButton.onClick.RemoveListener(advanceAction);
            }
            advanceAction = null;
        }

        private bool Matches(StoryStepContext step)
        {
            return open && context != null && step != null && sessionId == step.SessionId &&
                context.StepVersion == step.StepVersion && stepCancellation != null && !stepCancellation.IsCancellationRequested;
        }

        private void EnsureCurrent(StoryStepContext step, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (this == null || !Matches(step))
            {
                throw new OperationCanceledException("The story stage no longer owns this step.", token);
            }
        }

        private void Awake()
        {
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        private void OnEnable()
        {
            if (lifetime.IsCancellationRequested)
            {
                lifetime.Dispose();
                lifetime = new CancellationTokenSource();
            }
        }

        private void OnDisable()
        {
            CancelLifetime();
        }

        private void OnDestroy()
        {
            destroyed = true;
            CancelLifetime();
            try
            {
                CloseAsync(sessionId).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            lifetime.Dispose();
            if (ownsVoiceSource && voiceSource != null)
            {
                Destroy(voiceSource.gameObject);
            }
        }

        private static void Release(Action action, List<Exception> failures)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }

        private void CancelLifetime()
        {
            try
            {
                lifetime.Cancel();
            }
            catch (Exception error)
            {
                cancellationFailure = cancellationFailure == null ? error : new AggregateException(cancellationFailure, error);
            }
        }

        private void MainThread()
        {
            if (ownerThreadId == 0 || Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException("Use the story stage on its Unity main thread.");
            }
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Localization;
using Game.Framework.Story.Unity;
using UnityEngine;
using UnityEngine.Events;

namespace Game.Framework.Story.UGUI
{
    /// <summary>Inspector-first playback. Project code can inject command/assets adapters once.</summary>
    public sealed class StoryLauncher : MonoBehaviour
    {
        [SerializeField] private StorySettings settings;
        [SerializeField] private StoryStage stage;
        [SerializeField] private bool playOnStart;
        [SerializeField] private UnityEvent completed = new UnityEvent();
        private StoryPlayer player;
        private StorySession session;
        private IStoryAssets assets;
        private IStoryCommands commands;
        private LocalizationManager localization;
        private IStoryVideo video;
        private CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool destroyed;

        public bool IsPlaying => player != null && player.IsPlaying;
        public StorySession CurrentSession => session;
        public StoryResult LastResult { get; private set; }
        public event Action<StoryResult> Finished;

        public void Configure(
            StorySettings value,
            StoryStage presentation,
            IStoryAssets assetProvider = null,
            LocalizationManager translations = null,
            IStoryCommands businessCommands = null,
            IStoryVideo media = null)
        {
            if (IsPlaying)
            {
                throw new InvalidOperationException("Stop playback before reconfiguring its launcher.");
            }
            settings = value ?? throw new ArgumentNullException(nameof(value));
            stage = presentation ?? throw new ArgumentNullException(nameof(presentation));
            assets = assetProvider;
            localization = translations;
            commands = businessCommands;
            video = media;
            player?.Dispose();
            player = null;
        }

        public async Task<StoryResult> PlayAsync(CancellationToken cancellationToken = default, StoryCheckpoint checkpoint = null)
        {
            if (destroyed || !isActiveAndEnabled || settings == null || settings.sequence == null || stage == null)
            {
                throw new InvalidOperationException("Enable a launcher with StorySettings, sequence and stage before playback.");
            }
            if (IsPlaying)
            {
                throw new StoryBusyException();
            }
            if (player == null)
            {
                var provider = assets ?? settings.assets;
                if (provider == null)
                {
                    throw new InvalidOperationException("Assign a StoryAssetLibrary or inject IStoryAssets.");
                }
                stage.Configure(provider, localization, video);
                stage.ConfigureTiming(settings.charactersPerSecond, settings.videoPrepareTimeoutSeconds);
                player = new StoryPlayer(stage, commands);
            }
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token, stage.LifetimeToken))
            {
                session = player.Play(settings.sequence.Build(), linked.Token, checkpoint);
                StoryResult result = await session.Completion;
                LastResult = result;
                if (!destroyed)
                {
                    if (result.Status == StoryStatus.Completed)
                    {
                        completed.Invoke();
                    }
                    Finished?.Invoke(result);
                }
                return result;
            }
        }

        /// <summary>UnityEvent/button boundary. Errors are observed; business flows should await PlayAsync.</summary>
        public async void Play()
        {
            try
            {
                await PlayAsync();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        public bool Advance() => session != null && session.Advance();
        public bool Skip() => session != null && session.Skip();
        public bool Cancel() => session != null && session.Cancel();
        public Task StopAsync() => player == null ? Task.CompletedTask : player.StopAsync();

        private void Start()
        {
            if (playOnStart)
            {
                Play();
            }
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
            player?.Dispose();
            lifetime.Dispose();
        }

        private void CancelLifetime()
        {
            try
            {
                lifetime.Cancel();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
    }
}

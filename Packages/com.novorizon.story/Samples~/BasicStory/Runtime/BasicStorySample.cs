using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Localization;
using Game.Framework.Localization.UGUI;
using Game.Framework.Story.UGUI;
using Game.Framework.Story.Unity;
using Game.Framework.Story.Video;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace Game.Framework.Story.Samples
{
    /// <summary>A dedicated, self-contained scene sample. All generated objects and catalogs belong to this instance.</summary>
    [DisallowMultipleComponent]
    public sealed class BasicStorySample : MonoBehaviour
    {
        [SerializeField] private bool autoPlay = true;
        [SerializeField] private TMP_FontAsset font;
        [SerializeField] private AudioClip optionalVoice;
        [SerializeField] private VideoClip optionalVideo;
        private readonly List<Object> ownedAssets = new List<Object>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private GameObject uiRoot;
        private GameObject inputRoot;
        private GameObject audioRoot;
        private RawImage videoImage;
        private TMP_Text status;
        private Button replay;
        private Button skipVideo;
        private Task shutdown;
        private bool built;
        private bool stopping;

        public bool AutoPlay { get => autoPlay; set => autoPlay = value; }
        public bool IsBuilt => built && !stopping;
        public StoryStage Stage { get; private set; }
        public StoryLauncher Launcher { get; private set; }
        public StoryVideoPlayer Video { get; private set; }
        public StorySettings Settings { get; private set; }
        public LocalizationManager Localization { get; private set; }
        public TMP_Text BodyText { get; private set; }
        public TMP_Text SpeakerText { get; private set; }
        public Transform ChoiceRoot { get; private set; }
        public Button AdvanceButton { get; private set; }
        public string MediaStatus => optionalVideo == null ? "media.absent" : "media.ready";

        public void ConfigureMedia(AudioClip voice, VideoClip video)
        {
            if (built || stopping)
            {
                throw new InvalidOperationException("Assign sample media before building the sample.");
            }
            optionalVoice = voice;
            optionalVideo = video;
        }

        /// <summary>Build without starting playback, useful for embedding the sample in native acceptance scenes.</summary>
        public Task BuildAsync()
        {
            if (stopping || !isActiveAndEnabled)
            {
                throw new InvalidOperationException("Enable a fresh sample component before building it.");
            }
            if (built)
            {
                return Task.CompletedTask;
            }
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Run the sample on Unity's initialized main thread.");
            }
            font = font != null ? font : TMP_Settings.defaultFontAsset;
            if (font == null)
            {
                throw new InvalidOperationException("Import TMP Essential Resources and assign its default font in TMP Settings, or assign a font on BasicStorySample.");
            }

            try
            {
                Localization = new LocalizationManager();
                Localization.Initialize(CreateCatalog(), "en");
                BuildAssets();
                BuildUi();
                Video.Configure(Settings.assets);
                Launcher.Configure(Settings, Stage, Settings.assets, Localization, media: Video);
                Launcher.Finished += OnFinished;
                built = true;
                RefreshStatus();
                return Task.CompletedTask;
            }
            catch
            {
                // Build is synchronous. No story/native player can own these assets yet.
                DestroyOwnedObjects();
                stopping = true;
                throw;
            }
        }

        public async Task<StoryResult> PlayAsync(CancellationToken cancellationToken = default)
        {
            await BuildAsync();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token))
            {
                RefreshStatus();
                return await Launcher.PlayAsync(linked.Token);
            }
        }

        public void SetLanguage(string language)
        {
            if (!IsBuilt)
            {
                throw new InvalidOperationException("Build the sample before changing language.");
            }
            Localization.SetLanguage(language);
            RefreshStatus();
        }

        public Task ShutdownAsync()
        {
            return shutdown ?? (shutdown = ShutdownCoreAsync());
        }

        private async Task ShutdownCoreAsync()
        {
            stopping = true;
            lifetime.Cancel();
            try
            {
                if (Launcher != null)
                {
                    Launcher.Finished -= OnFinished;
                    await Launcher.StopAsync();
                }
            }
            finally
            {
                DestroyOwnedObjects();
                lifetime.Dispose();
            }
        }

        private void BuildAssets()
        {
            var library = Own(ScriptableObject.CreateInstance<StoryAssetLibrary>());
            var entries = new List<StoryAssetLibrary.Entry>
            {
                new StoryAssetLibrary.Entry { id = "sample/background", asset = CreateBackground() },
                new StoryAssetLibrary.Entry { id = "sample/guide", asset = CreatePortrait() }
            };
            if (optionalVoice != null)
            {
                entries.Add(new StoryAssetLibrary.Entry { id = "sample/voice", asset = optionalVoice });
            }
            if (optionalVideo != null)
            {
                entries.Add(new StoryAssetLibrary.Entry { id = "sample/video", asset = optionalVideo });
            }
            library.entries = entries.ToArray();
            var sequence = Own(ScriptableObject.CreateInstance<StorySequenceAsset>());
            sequence.sequenceId = "basic-story";
            sequence.entryNodeId = "background";
            var nodes = new List<StoryNodeData>
            {
                new StoryNodeData { id = "background", kind = StoryNodeKind.SetBackground, resourceId = "sample/background" },
                new StoryNodeData { id = "portrait", kind = StoryNodeKind.SetPortrait, slotId = "guide", resourceId = "sample/guide" },
                new StoryNodeData
                {
                    id = "greeting",
                    kind = StoryNodeKind.Say,
                    speakerKey = "guide",
                    textKey = "greeting",
                    resourceId = optionalVoice == null ? null : "sample/voice"
                },
                new StoryNodeData
                {
                    id = "route",
                    kind = StoryNodeKind.Choice,
                    choices = new[]
                    {
                        new StoryChoiceData { id = "watch", textKey = "choice.watch", targetNodeId = "media" },
                        new StoryChoiceData { id = "finish", textKey = "choice.finish", targetNodeId = "closing" }
                    }
                },
                new StoryNodeData
                {
                    id = "media",
                    kind = StoryNodeKind.Say,
                    speakerKey = "guide",
                    textKey = optionalVideo == null ? "media.absent" : "media.intro",
                    nextId = optionalVideo == null ? "closing" : "video"
                }
            };
            if (optionalVideo != null)
            {
                nodes.Add(new StoryNodeData { id = "video", kind = StoryNodeKind.PlayVideo, resourceId = "sample/video", nextId = "closing" });
            }
            nodes.Add(new StoryNodeData { id = "closing", kind = StoryNodeKind.Say, speakerKey = "guide", textKey = "closing" });
            nodes.Add(new StoryNodeData { id = "end", kind = StoryNodeKind.End });
            sequence.nodes = nodes.ToArray();
            // Validate the exact authored graph before creating any presentation objects.
            sequence.Build();
            Settings = Own(ScriptableObject.CreateInstance<StorySettings>());
            Settings.sequence = sequence;
            Settings.assets = library;
            Settings.charactersPerSecond = 36;
        }

        private void BuildUi()
        {
            uiRoot = new GameObject("Basic Story Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            uiRoot.transform.SetParent(transform, false);
            var canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = .5f;
            Image backdrop = Image("Backdrop", uiRoot.transform, new Color(.08f, .12f, .2f));
            Stretch(backdrop.rectTransform);
            backdrop.preserveAspect = false;
            videoImage = Rect("Video", uiRoot.transform).gameObject.AddComponent<RawImage>();
            Stretch(videoImage.rectTransform);
            videoImage.raycastTarget = false;
            videoImage.enabled = false;
            var aspect = videoImage.gameObject.AddComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            RectTransform safe = Rect("Safe Content", uiRoot.transform);
            Stretch(safe);
            safe.gameObject.AddComponent<UISafeArea>();
            Image portrait = Image("Guide Portrait", safe, Color.white);
            Place(portrait.rectTransform, .07f, .26f, .44f, .89f);
            portrait.preserveAspect = true;

            RectTransform toolbar = Rect("Toolbar", safe);
            Place(toolbar, .03f, .89f, .97f, .98f);
            status = Label("Status", toolbar, 19);
            Place(status.rectTransform, 0, 0, .55f, 1);
            replay = Button("Replay", toolbar, "replay", () => RunPlayback());
            Place((RectTransform)replay.transform, .56f, .04f, .69f, .96f);
            Button english = Button("English", toolbar, "language.en", () => SetLanguage("en"));
            Place((RectTransform)english.transform, .70f, .04f, .84f, .96f);
            Button french = Button("French", toolbar, "language.fr", () => SetLanguage("fr"));
            Place((RectTransform)french.transform, .85f, .04f, 1, .96f);

            Image panel = Image("Dialogue", safe, new Color(.035f, .055f, .09f, .96f));
            Place(panel.rectTransform, .03f, .035f, .97f, .40f);
            SpeakerText = Label("Speaker", panel.transform, 27);
            SpeakerText.fontStyle = FontStyles.Bold;
            Place(SpeakerText.rectTransform, .04f, .75f, .96f, .95f);
            BodyText = Label("Body", panel.transform, 24);
            Place(BodyText.rectTransform, .04f, .23f, .96f, .73f);
            AdvanceButton = Button("Advance", panel.transform, "advance", null);
            Place((RectTransform)AdvanceButton.transform, .75f, .035f, .96f, .22f);
            var choices = Rect("Choices", panel.transform);
            Place(choices, .04f, .08f, .72f, .90f);
            var layout = choices.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            ChoiceRoot = choices;
            var templates = Rect("Inactive Templates", uiRoot.transform);
            templates.gameObject.SetActive(false);
            Button choiceTemplate = Button("Choice Template", templates, null, null);
            var element = choiceTemplate.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 62;
            element.minHeight = 48;
            choiceTemplate.gameObject.SetActive(false);

            var controls = Rect("Media Controls", safe);
            Place(controls, .57f, .42f, .97f, .50f);
            skipVideo = Button("Skip Video", controls, "skip.video", () => Video.SkipCurrent());
            Place((RectTransform)skipVideo.transform, 0, 0, 1, 1);

            Stage = uiRoot.AddComponent<StoryStage>();
            Stage.ConfigureUi(
                SpeakerText,
                BodyText,
                backdrop,
                new[] { new StoryPortraitSlot { id = "guide", image = portrait } },
                choices,
                choiceTemplate,
                AdvanceButton,
                panel.gameObject);
            Video = uiRoot.AddComponent<StoryVideoPlayer>();
            Video.ConfigureImage(videoImage, aspect);
            Launcher = uiRoot.AddComponent<StoryLauncher>();
            if (optionalVoice != null)
            {
                bool listenerPresent = false;
                foreach (AudioListener listener in Object.FindObjectsOfType<AudioListener>())
                {
                    listenerPresent |= listener.enabled;
                }
                if (!listenerPresent)
                {
                    audioRoot = new GameObject("Basic Story Audio Listener", typeof(AudioListener));
                    audioRoot.transform.SetParent(transform, false);
                }
            }
            // Reuse project input without modifying it; otherwise create only this sample's input.
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                inputRoot = new GameObject("Basic Story Input", typeof(EventSystem));
                inputRoot.transform.SetParent(transform, false);
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                Type module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module == null)
                {
                    throw new InvalidOperationException("Install the Input System UI module or enable legacy input to use sample buttons.");
                }
                inputRoot.AddComponent(module);
                module.GetMethod("AssignDefaultActions", Type.EmptyTypes)?.Invoke(inputRoot.GetComponent(module), null);
#else
                inputRoot.AddComponent<StandaloneInputModule>();
#endif
            }
        }

        private Button Button(string name, Transform parent, string key, Action action)
        {
            Image image = Image(name, parent, new Color(.15f, .28f, .42f));
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (action != null)
            {
                button.onClick.AddListener(() => action());
            }
            TMP_Text label = Label("Label", image.transform, 21);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            label.margin = new Vector4(10, 2, 10, 2);
            if (!string.IsNullOrEmpty(key))
            {
                label.gameObject.AddComponent<LocalizedText>().Bind(Localization, key);
            }
            return button;
        }

        private TMP_Text Label(string name, Transform parent, float size)
        {
            var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = size;
            label.color = Color.white;
            label.enableWordWrapping = true;
            label.raycastTarget = false;
            return label;
        }

        private static Image Image(string name, Transform parent, Color color)
        {
            var image = Rect(name, parent).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect) => Place(rect, 0, 0, 1, 1);

        private static void Place(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private Sprite CreateBackground()
        {
            return CreateSprite("Sample Background", 256, 128, (x, y) =>
            {
                float glow = Mathf.Clamp01(1 - Vector2.Distance(new Vector2(x / 256f, y / 128f), new Vector2(.76f, .72f)));
                return Color.Lerp(new Color(.025f, .055f, .12f), new Color(.16f, .35f, .42f), glow);
            });
        }

        private Sprite CreatePortrait()
        {
            return CreateSprite("Sample Guide", 128, 192, (x, y) =>
            {
                float dx = (x - 64) / 39f;
                float dy = (y - 128) / 48f;
                if (y < 94 && Mathf.Abs(x - 64) < (95 - y) * .28f + 27)
                {
                    return new Color(.2f, .57f, .64f);
                }
                if (dx * dx + dy * dy < 1)
                {
                    if (y > 151 || (y > 125 && x < 38))
                    {
                        return new Color(.13f, .13f, .22f);
                    }
                    if ((Mathf.Abs(x - 51) < 3 || Mathf.Abs(x - 77) < 3) && Mathf.Abs(y - 130) < 4)
                    {
                        return new Color(.08f, .13f, .18f);
                    }
                    if (Mathf.Abs(y - 111) < 2 && Mathf.Abs(x - 64) < 10)
                    {
                        return new Color(.6f, .25f, .28f);
                    }
                    return new Color(.93f, .73f, .57f);
                }
                return Color.clear;
            });
        }

        private Sprite CreateSprite(string name, int width, int height, Func<int, int, Color> pixel)
        {
            var texture = Own(new Texture2D(width, height, TextureFormat.RGBA32, false));
            texture.name = name + " Texture";
            var colors = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    colors[y * width + x] = pixel(x, y);
                }
            }
            texture.SetPixels(colors);
            texture.Apply(false, true);
            Sprite sprite = Own(Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f)));
            sprite.name = name;
            return sprite;
        }

        private T Own<T>(T asset) where T : Object
        {
            asset.hideFlags = HideFlags.DontSave;
            ownedAssets.Add(asset);
            return asset;
        }

        private void OnFinished(StoryResult result)
        {
            RefreshStatus();
            if (status != null)
            {
                status.text = Localization.Get("result") + ": " + result.Status + "\n" + Localization.Get(MediaStatus);
            }
            if (result.Error != null)
            {
                Debug.LogException(result.Error);
            }
        }

        private void RefreshStatus()
        {
            if (status != null && Localization != null && Localization.IsInitialized)
            {
                status.text = Localization.Get("instructions") + "\n" + Localization.Get(MediaStatus);
            }
        }

        private void Update()
        {
            if (!IsBuilt)
            {
                return;
            }
            bool showingVideo = Video.OutputTexture != null && Video.DecodedFrameCount > 0;
            videoImage.enabled = showingVideo;
            skipVideo.gameObject.SetActive(Video.IsBusy);
            replay.interactable = !Launcher.IsPlaying;
        }

        private async void RunPlayback()
        {
            try
            {
                await PlayAsync();
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested || stopping)
            {
            }
            catch (Exception error)
            {
                if (status != null)
                {
                    status.text = error.Message;
                }
                Debug.LogException(error);
            }
        }

        private void Start()
        {
            if (autoPlay && !stopping && !built)
            {
                RunPlayback();
            }
        }

        private void DestroyOwnedObjects()
        {
            if (uiRoot != null)
            {
                Object.Destroy(uiRoot);
            }
            if (inputRoot != null)
            {
                Object.Destroy(inputRoot);
            }
            if (audioRoot != null)
            {
                Object.Destroy(audioRoot);
            }
            // Shared inspector clips/fonts are deliberately absent from this list.
            for (int i = ownedAssets.Count - 1; i >= 0; i--)
            {
                if (ownedAssets[i] != null)
                {
                    Object.Destroy(ownedAssets[i]);
                }
            }
            ownedAssets.Clear();
            Localization?.Dispose();
            Localization = null;
            built = false;
        }

        private async void OnDestroy()
        {
            try
            {
                await ShutdownAsync();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        private sealed class Translation
        {
            internal string key;
            internal string english;
            internal string french;
        }

        private static LocalizationCatalog CreateCatalog()
        {
            return LocalizationCatalog.FromRows(
                new[]
                {
                    Row("guide", "Mira", "Mira"),
                    Row("greeting", "Welcome! Click Next once to reveal the line, then again to advance. Switch language at any time.", "Bienvenue ! Cliquez une fois sur Suite pour afficher la phrase, puis encore pour avancer. Changez de langue quand vous voulez."),
                    Row("choice.watch", "Show the media branch", "Voir la branche vidéo"),
                    Row("choice.finish", "Continue to the ending", "Aller à la fin"),
                    Row("media.intro", "Your assigned clip will play next. Skip video ends only that clip.", "Votre vidéo va commencer. Passer termine seulement cette vidéo."),
                    Row("media.absent", "No video assigned. Assign a VideoClip on BasicStorySample to enable real playback.", "Aucune vidéo choisie. Ajoutez un VideoClip sur BasicStorySample pour la lire."),
                    Row("media.ready", "A project VideoClip is assigned; the media branch will decode it.", "Un VideoClip du projet est choisi ; la branche vidéo le lira."),
                    Row("closing", "That is the whole story: portraits, dialogue, a choice and optional media. Your game owns the story content.", "Voilà toute l'histoire : portrait, dialogue, choix et média facultatif. Votre jeu définit le contenu."),
                    Row("advance", "Next / reveal", "Suite / afficher"),
                    Row("replay", "Replay", "Rejouer"),
                    Row("language.en", "English", "English"),
                    Row("language.fr", "Français", "Français"),
                    Row("skip.video", "Skip video", "Passer la vidéo"),
                    Row("instructions", "Next reveals, then advances. Choices are buttons.", "Suite affiche puis avance. Choisissez avec les boutons."),
                    Row("result", "Story result", "Résultat")
                },
                row => row.key,
                new TranslationColumn<Translation>("en", row => row.english),
                new TranslationColumn<Translation>("fr", row => row.french));
        }

        private static Translation Row(string key, string english, string french)
        {
            return new Translation { key = key, english = english, french = french };
        }
    }
}

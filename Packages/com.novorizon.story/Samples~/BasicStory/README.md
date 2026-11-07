# Basic Story

This sample creates a Canvas, TMP text, portrait/background sprites, a local English/French catalog, a `StorySequenceAsset`, `StorySettings` and `StoryAssetLibrary` at runtime. It needs no DataManager tables or resource SDK. Every generated object and the sample's own `LocalizationManager` belong to this sample instance; project fonts, clips, services and input objects remain project-owned.

1. Install the story UI dependencies (`com.novorizon.framework.ui`, `com.novorizon.localization`, TMP and uGUI) and `com.unity.modules.video`, then import **Basic Story** in Package Manager.
2. Import **Window > TextMeshPro > Import TMP Essential Resources**, and set the TMP default font if needed. You can also assign a font on `BasicStorySample`.
3. Choose **Tools > Novorizon > Demos > Create Basic Story Scene**. The menu saves a new dedicated scene under `Assets/NovorizonDemos`.
4. Enter Play mode. **Next / reveal** first finishes the visible line, then advances. Choose either branch and switch English/French while a line or choices are visible. **Replay** starts a fresh session after completion.

The generated graph sets a background and portrait, presents dialogue, offers two choices, presents optional media, then ends after another line. Missing video is visible in the toolbar and the media branch; it never reports a video as played. To enable actual playback, assign a project `VideoClip` to **Optional Video** before entering Play mode. Assign **Optional Voice** to play an actual `AudioClip` during the opening line. Clip decoding depends on Unity's platform codecs. **Skip video** ends only the active clip; playback errors remain failed story results.

Full-screen backgrounds and video sit outside `UISafeArea`. Dialogue, choices and controls sit inside it so screen cutouts and the iPhone home indicator inset the interactive content. The sample reuses an existing scene EventSystem; in an empty scene it creates and later removes its own. A project using only the new Input System needs its UI input module installed.

The sample assembly is conditional on its optional UI, TMP, localization and video packages. Missing dependencies hide the sample menu until they are installed; the story core remains usable independently.

For native tests or embedding, add `Game.Framework.Story.Samples.BasicStorySample`, set `AutoPlay = false`, call `ConfigureMedia(voice, video)` before `BuildAsync()`, and await `PlayAsync(token)`. Public `Stage`, `Launcher`, `Settings`, `Localization`, `Video`, `BodyText`, `SpeakerText`, `ChoiceRoot` and `AdvanceButton` expose the generated objects. `ShutdownAsync()` cancels playback, awaits story cleanup and releases only the sample's owned resources. Recreate the component after shutdown.

`Runtime/BasicStorySample.cs` shows the minimal local authoring/catalog wiring. Production games can replace its generated UI with inspector-authored prefabs and persist `StorySequenceAsset`/`StorySettings`/`StoryAssetLibrary` assets without depending on this sample.

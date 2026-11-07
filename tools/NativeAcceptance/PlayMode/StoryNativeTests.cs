using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Localization;
using Game.Framework.Story;
using Game.Framework.Story.UGUI;
using Game.Framework.Story.Unity;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class StoryNativeTests
{
    private sealed class Assets : IStoryAssets
    {
        internal Sprite Sprite;
        internal AudioClip Voice;
        internal int Loads, Releases;

        public Task<StoryAssetLease<T>> LoadAsync<T>(string id, CancellationToken token) where T : Object
        {
            token.ThrowIfCancellationRequested();
            T asset = (id == "voice" ? (Object)Voice : Sprite) as T;
            Assert.That(asset, Is.Not.Null, "The native test requires a real asset of the requested type.");
            Loads++;
            return Task.FromResult(new StoryAssetLease<T>(asset, () => Releases++));
        }
    }

    private sealed class Host
    {
        internal GameObject Root;
        internal StoryStage Stage;
        internal TMP_Text Body;
        internal Transform Choices;
        internal Image Portrait;
    }

    [UnityTest]
    public IEnumerator TmpStoryRevealsRefreshesChoosesAndReleasesOnlyItsOwnAssets()
    {
        Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null);
        AudioClip voice = Resources.Load<AudioClip>("NativeStory/voice");
        Assert.That(voice, Is.Not.Null, "Copy the owned PCM voice fixture before acceptance.");
        bool ownThreadHost = !UnityThread.IsInitialized;
        if (ownThreadHost)
        {
            UnityThread.Initialize();
        }
        var translations = new LocalizationManager();
        translations.Initialize(
            LocalizationCatalog.FromRows(
                new[]
                {
                    new[] { "hello", "A first line with a portrait.", "Une premiere ligne avec un portrait." },
                    new[] { "speaker", "Guide", "Guide" },
                    new[] { "choose", "Continue", "Continuer" },
                    new[] { "finish", "The branch is complete.", "La branche est terminee." }
                },
                row => row[0],
                new TranslationColumn<string[]>("en", row => row[1]),
                new TranslationColumn<string[]>("fr", row => row[2])),
            "en");
        var texture = new Texture2D(4, 4);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * .5f);
        var assets = new Assets { Sprite = sprite, Voice = voice };
        var host = CreateHost();
        var player = new StoryPlayer(host.Stage);
        try
        {
            host.Stage.Configure(assets, translations);
            host.Stage.ConfigureTiming(1);
            StorySession session = player.Play(Sequence());
            yield return UntilNode(host.Stage, "hello");
            Assert.That(host.Portrait.sprite, Is.SameAs(sprite));
            Assert.That(host.Stage.IsRevealing, Is.True);
            Assert.That(session.Advance(), Is.True);
            Assert.That(host.Stage.IsRevealing, Is.False);
            Assert.That(host.Stage.CurrentNodeId, Is.EqualTo("hello"), "The first click reveals without advancing.");
            translations.SetLanguage("fr");
            Assert.That(host.Body.text, Is.EqualTo("Une premiere ligne avec un portrait."));
            session.Advance();
            yield return UntilNode(host.Stage, "choice");
            Assert.That(assets.Releases, Is.EqualTo(1), "The voice lease ends with its dialogue step.");
            var options = host.Choices.GetComponentsInChildren<Button>();
            Assert.That(options.Length, Is.EqualTo(1));
            Button retired = options[0];
            Button.ButtonClickedEvent retiredClick = retired.onClick;
            Assert.That(retired.GetComponentInChildren<TMP_Text>().text, Is.EqualTo("Continuer"));
            retiredClick.Invoke();
            yield return UntilNode(host.Stage, "finish");
            retiredClick.Invoke();
            Assert.That(host.Stage.CurrentNodeId, Is.EqualTo("finish"), "A retired choice cannot advance another step.");
            session.Advance();
            session.Advance();
            yield return UntilCompleted(session.Completion);
            StoryResult result = session.Completion.GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(StoryStatus.Completed), result.Error?.ToString());
            Assert.That(result.Selections.Count, Is.EqualTo(1));
            Assert.That(result.Selections[0].NodeId, Is.EqualTo("choice"));
            Assert.That(result.Selections[0].ChoiceId, Is.EqualTo("continue"));
            Assert.That(host.Stage.IsOpen, Is.False);
            Assert.That(host.Stage.OwnedAssetCount, Is.Zero);
            Assert.That(assets.Releases, Is.EqualTo(assets.Loads));
            Assert.That(host.Portrait.sprite, Is.Null);
            Assert.That(sprite != null && voice != null, Is.True, "The shared project assets are not destroyed by releasing a lease.");
            translations.SetLanguage("en");
            Assert.That(host.Body.text, Is.Empty, "Closed dialogue must not be repopulated by language changes.");
        }
        finally
        {
            player.Dispose();
            host.Root.SetActive(false);
            Object.Destroy(host.Root);
            translations.Dispose();
            Object.Destroy(sprite);
            Object.Destroy(texture);
            if (ownThreadHost)
            {
                UnityThread.Reset();
            }
        }
    }

    [UnityTest]
    public IEnumerator DestroyingStageCancelsItsSessionAndReleasesPortraitAndVoice()
    {
        bool ownThreadHost = !UnityThread.IsInitialized;
        if (ownThreadHost)
        {
            UnityThread.Initialize();
        }
        var texture = new Texture2D(4, 4);
        var sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.one * .5f);
        var assets = new Assets { Sprite = sprite, Voice = Resources.Load<AudioClip>("NativeStory/voice") };
        var host = CreateHost();
        var player = new StoryPlayer(host.Stage);
        try
        {
            host.Stage.Configure(assets);
            StorySession session = player.Play(Sequence(), host.Stage.LifetimeToken);
            yield return UntilNode(host.Stage, "hello");
            Assert.That(assets.Loads, Is.EqualTo(2));
            Object.Destroy(host.Root);
            yield return UntilCompleted(session.Completion);
            StoryResult result = session.Completion.GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(StoryStatus.Cancelled), result.Error?.ToString());
            Assert.That(assets.Releases, Is.EqualTo(2));
            Assert.That(player.IsPlaying, Is.False);
            Assert.That(assets.Voice != null && sprite != null, Is.True);
        }
        finally
        {
            player.Dispose();
            if (host.Root != null)
            {
                Object.Destroy(host.Root);
            }
            Object.Destroy(sprite);
            Object.Destroy(texture);
            if (ownThreadHost)
            {
                UnityThread.Reset();
            }
        }
    }

    private static StorySequence Sequence()
    {
        return new StorySequence(
            "native-story",
            "1",
            new[]
            {
                new StoryNode("portrait", StoryNodeKind.SetPortrait, resourceId: "portrait", slotId: "left"),
                new StoryNode("hello", StoryNodeKind.Say, textKey: "hello", speakerKey: "speaker", resourceId: "voice"),
                new StoryNode("choice", StoryNodeKind.Choice, choices: new[] { new StoryChoice("continue", "choose", "finish") }),
                new StoryNode("finish", StoryNodeKind.Say, textKey: "finish"),
                new StoryNode("end", StoryNodeKind.End)
            });
    }

    private static Host CreateHost()
    {
        var host = new Host { Root = new GameObject("Story native stage", typeof(RectTransform), typeof(Canvas)) };
        host.Root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        host.Stage = host.Root.AddComponent<StoryStage>();
        var dialogue = new GameObject("Dialogue", typeof(RectTransform));
        dialogue.transform.SetParent(host.Root.transform, false);
        host.Body = Label("Body", dialogue.transform);
        TMP_Text speaker = Label("Speaker", dialogue.transform);
        host.Portrait = new GameObject("Portrait", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        host.Portrait.transform.SetParent(host.Root.transform, false);
        host.Choices = new GameObject("Choices", typeof(RectTransform)).transform;
        host.Choices.SetParent(dialogue.transform, false);
        var template = new GameObject("Option template", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
        template.transform.SetParent(dialogue.transform, false);
        Label("Label", template.transform);
        template.gameObject.SetActive(false);
        host.Stage.ConfigureUi(
            speaker,
            host.Body,
            null,
            new[] { new StoryPortraitSlot { id = "left", image = host.Portrait } },
            host.Choices,
            template,
            null,
            dialogue);
        return host;
    }

    private static TMP_Text Label(string name, Transform parent)
    {
        var text = new GameObject(name, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.transform.SetParent(parent, false);
        text.font = TMP_Settings.defaultFontAsset;
        return text;
    }

    private static IEnumerator UntilNode(StoryStage stage, string nodeId)
    {
        float deadline = Time.realtimeSinceStartup + 10;
        while (stage.CurrentNodeId != nodeId)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Story did not reach " + nodeId);
            yield return null;
        }
    }

    private static IEnumerator UntilCompleted(Task task)
    {
        float deadline = Time.realtimeSinceStartup + 10;
        while (!task.IsCompleted)
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Story cleanup did not settle.");
            yield return null;
        }
    }
}

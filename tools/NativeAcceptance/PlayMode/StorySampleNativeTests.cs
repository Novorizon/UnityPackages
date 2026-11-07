using System.Collections;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Story;
using Game.Framework.Story.Samples;
using NUnit.Framework;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

public sealed class StorySampleNativeTests
{
    [UnityTest]
    public IEnumerator BasicSampleRunsTheLocalizedPortraitChoiceVoiceAndRealVideoBranch()
    {
        Assert.That(TMP_Settings.defaultFontAsset, Is.Not.Null);
        var voice = Resources.Load<AudioClip>("NativeStory/voice");
        var clip = Resources.Load<VideoClip>("NativeStory/acceptance");
        Assert.That(voice, Is.Not.Null);
        Assert.That(clip, Is.Not.Null);
        bool ownThreadHost = !UnityThread.IsInitialized;
        if (ownThreadHost)
        {
            UnityThread.Initialize();
        }
        var root = new GameObject("Basic Story end-to-end");
        var sample = root.AddComponent<BasicStorySample>();
        sample.AutoPlay = false;
        try
        {
            sample.ConfigureMedia(voice, clip);
            sample.BuildAsync().GetAwaiter().GetResult();
            Assert.That(sample.Stage, Is.Not.Null);
            Assert.That(sample.Launcher, Is.Not.Null);
            Assert.That(sample.BodyText.font, Is.SameAs(TMP_Settings.defaultFontAsset));
            Assert.That(root.GetComponentInChildren<UISafeArea>(true), Is.Not.Null);
            sample.SetLanguage("fr");
            Assert.That(sample.Localization.Language, Is.EqualTo("fr"));
            sample.Settings.charactersPerSecond = 0;
            Task<StoryResult> playback = sample.PlayAsync();
            float deadline = Time.realtimeSinceStartup + 30;
            bool choseMedia = false;
            bool decoded = false;
            bool sawPortrait = false;
            while (!playback.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "The generated story sample must finish its media branch.");
                string node = sample.Stage.CurrentNodeId;
                if (node == "greeting")
                {
                    sawPortrait = sample.Stage.OwnedAssetCount >= 2;
                    Assert.That(sample.BodyText.text, Does.StartWith("Bienvenue"));
                    sample.Launcher.Advance();
                }
                else if (node == "route" && !choseMedia)
                {
                    var options = sample.ChoiceRoot.GetComponentsInChildren<Button>();
                    Assert.That(options.Length, Is.EqualTo(2));
                    Assert.That(options[0].GetComponentInChildren<TMP_Text>().text, Does.Contain("vidéo"));
                    choseMedia = true;
                    options[0].onClick.Invoke();
                }
                else if (node == "media" || node == "closing")
                {
                    sample.Launcher.Advance();
                }
                if (sample.Video.DecodedFrameCount > 0 && sample.Video.OutputTexture != null)
                {
                    decoded = true;
                }
                yield return null;
            }
            StoryResult result = playback.GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(StoryStatus.Completed), result.Error?.ToString());
            Assert.That(sawPortrait, Is.True);
            Assert.That(choseMedia, Is.True);
            Assert.That(decoded, Is.True, "The sample media branch must publish an actual decoded video frame.");
            Assert.That(result.Selections.Count, Is.EqualTo(1));
            Assert.That(result.Selections[0].ChoiceId, Is.EqualTo("watch"));
            Assert.That(sample.Stage.OwnedAssetCount, Is.Zero);
            Assert.That(sample.Video.IsBusy, Is.False);
            Assert.That(voice != null && clip != null, Is.True);
            Task shutdown = sample.ShutdownAsync();
            while (!shutdown.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            shutdown.GetAwaiter().GetResult();
        }
        finally
        {
            Object.Destroy(root);
            if (ownThreadHost)
            {
                UnityThread.Reset();
            }
        }
    }
}

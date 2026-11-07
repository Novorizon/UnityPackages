using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Story.Unity;
using Game.Framework.Story.Video;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

public sealed class StoryVideoNativeTests
{
    private sealed class Assets : IStoryAssets
    {
        internal readonly VideoClip Clip;
        internal readonly TaskCompletionSource<StoryAssetLease<VideoClip>> Delayed =
            new TaskCompletionSource<StoryAssetLease<VideoClip>>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Loads, Releases;
        internal bool DelayFirst;

        internal Assets(VideoClip clip)
        {
            Clip = clip;
        }

        public Task<StoryAssetLease<T>> LoadAsync<T>(string id, CancellationToken token) where T : Object
        {
            Assert.That(typeof(T), Is.EqualTo(typeof(VideoClip)));
            Loads++;
            if (DelayFirst && Loads == 1)
            {
                return (Task<StoryAssetLease<T>>)(object)Delayed.Task;
            }
            return Task.FromResult(new StoryAssetLease<T>((T)(Object)Clip, () => Releases++));
        }

        internal void CompleteDelayed()
        {
            Delayed.SetResult(new StoryAssetLease<VideoClip>(Clip, () => Releases++));
        }
    }

    [UnityTest]
    public IEnumerator LocalVideoDecodesAPixelFrameAndReleasesItsOwnLease()
    {
        VideoClip clip = Resources.Load<VideoClip>("NativeStory/acceptance");
        Assert.That(clip, Is.Not.Null, "Generate the owned VP8 webm fixture before native acceptance.");
        bool ownHost = !UnityThread.IsInitialized;
        if (ownHost)
        {
            UnityThread.Initialize();
        }
        var root = new GameObject("Story native video");
        var image = new GameObject("Video surface", typeof(RectTransform)).AddComponent<RawImage>();
        image.transform.SetParent(root.transform, false);
        var player = root.AddComponent<StoryVideoPlayer>();
        var assets = new Assets(clip);
        var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            player.Configure(assets);
            player.ConfigureImage(image);
            Task playback = player.PlayAsync("NativeStory/acceptance", 15, true, CancellationToken.None);
            float deadline = Time.realtimeSinceStartup + 20;
            bool decoded = false;
            while (!playback.IsCompleted)
            {
                if (player.DecodedFrameCount > 0 && player.OutputTexture != null && image.texture == player.OutputTexture)
                {
                    Assert.That(player.OutputTexture.IsCreated(), Is.True);
                    RenderTexture.active = player.OutputTexture;
                    readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    readback.Apply();
                    // The owned fixture is a nonblack blue frame (0x23557b), so a clear
                    // or newly allocated target cannot pass as decoded sample content.
                    Color pixel = readback.GetPixel(0, 0);
                    decoded |= pixel.a > .5f && pixel.b > .1f && pixel.b > pixel.r + .05f;
                    RenderTexture.active = previous;
                }
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            playback.GetAwaiter().GetResult();
            Assert.That(decoded, Is.True, "A real decoded frame and published texture must be observed.");
            Assert.That(assets.Releases, Is.EqualTo(1));
            Assert.That(player.IsBusy, Is.False);
            Assert.That(player.OutputTexture, Is.Null);
            Assert.That(image.texture, Is.Null);
        }
        finally
        {
            RenderTexture.active = previous;
            player.Stop();
            Object.Destroy(root);
            Object.Destroy(readback);
            if (ownHost)
            {
                UnityThread.Reset();
            }
        }
    }

    [UnityTest]
    public IEnumerator DestroyAfterAcquisitionCompletionReleasesTheIncomingLease()
    {
        VideoClip clip = Resources.Load<VideoClip>("NativeStory/acceptance");
        Assert.That(clip, Is.Not.Null);
        bool ownHost = !UnityThread.IsInitialized;
        if (ownHost)
        {
            UnityThread.Initialize();
        }
        var root = new GameObject("Story acquire handoff destruction");
        var player = root.AddComponent<StoryVideoPlayer>();
        var assets = new Assets(clip) { DelayFirst = true };
        try
        {
            player.Configure(assets);
            Task playback = player.PlayAsync("queued", 15, true, CancellationToken.None);
            // RunContinuationsAsynchronously queues the successful acquisition continuation.
            // Destroy now, before Unity's next update can attach that lease to the retired operation.
            assets.CompleteDelayed();
            Object.DestroyImmediate(root);
            float deadline = Time.realtimeSinceStartup + 10;
            while (!playback.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            Assert.Catch<OperationCanceledException>(() => playback.GetAwaiter().GetResult());
            yield return null;
            Assert.That(assets.Releases, Is.EqualTo(1), "A completed lease must not be attached after OnDestroy has already cleaned the operation.");
            Assert.That(clip != null, Is.True);
        }
        finally
        {
            if (root != null)
            {
                Object.Destroy(root);
            }
            if (ownHost)
            {
                UnityThread.Reset();
            }
        }
    }

    [UnityTest]
    public IEnumerator PreparationTimeoutReleasesNativeOperationAndLease()
    {
        VideoClip clip = Resources.Load<VideoClip>("NativeStory/acceptance");
        Assert.That(clip, Is.Not.Null);
        bool ownHost = !UnityThread.IsInitialized;
        if (ownHost)
        {
            UnityThread.Initialize();
        }
        var root = new GameObject("Story video timeout");
        var player = root.AddComponent<StoryVideoPlayer>();
        var assets = new Assets(clip);
        try
        {
            player.Configure(assets);
            Task playback = player.PlayAsync("clip", .000001f, false, CancellationToken.None);
            Assert.That(player.SkipCurrent(), Is.False, "A forbidden skip must not cancel the operation.");
            float deadline = Time.realtimeSinceStartup + 10;
            while (!playback.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                yield return null;
            }
            Assert.Throws<TimeoutException>(() => playback.GetAwaiter().GetResult());
            Assert.That(assets.Releases, Is.EqualTo(1));
            Assert.That(player.IsBusy, Is.False);
            Assert.That(player.OutputTexture, Is.Null);
            yield return null;
            Assert.That(root.GetComponentsInChildren<VideoPlayer>().Length, Is.Zero);
        }
        finally
        {
            player.Stop();
            Object.Destroy(root);
            if (ownHost)
            {
                UnityThread.Reset();
            }
        }
    }

    [UnityTest]
    public IEnumerator CancelledAcquisitionCannotPublishIntoTheNextVideoAndSkipCleansIt()
    {
        VideoClip clip = Resources.Load<VideoClip>("NativeStory/acceptance");
        Assert.That(clip, Is.Not.Null);
        bool ownHost = !UnityThread.IsInitialized;
        if (ownHost)
        {
            UnityThread.Initialize();
        }
        var root = new GameObject("Story video cancellation");
        var image = new GameObject("Video surface", typeof(RectTransform)).AddComponent<RawImage>();
        image.transform.SetParent(root.transform, false);
        var player = root.AddComponent<StoryVideoPlayer>();
        var assets = new Assets(clip) { DelayFirst = true };
        using (var cancel = new CancellationTokenSource())
        {
            try
            {
                player.Configure(assets);
                player.ConfigureImage(image);
                Task first = player.PlayAsync("delayed", 15, true, cancel.Token);
                cancel.Cancel();
                float deadline = Time.realtimeSinceStartup + 20;
                while (!first.IsCompleted)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.Catch<OperationCanceledException>(() => first.GetAwaiter().GetResult());
                Task second = player.PlayAsync("ready", 15, true, CancellationToken.None);
                assets.CompleteDelayed();
                while (!second.IsCompleted && player.DecodedFrameCount == 0)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                Assert.That(second.IsCompleted, Is.False, "The fixture must be long enough to exercise skipping.");
                Assert.That(player.SkipCurrent(), Is.True);
                while (!second.IsCompleted)
                {
                    Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
                    yield return null;
                }
                second.GetAwaiter().GetResult();
                yield return null;
                Assert.That(assets.Releases, Is.EqualTo(2));
                Assert.That(player.IsBusy, Is.False);
                Assert.That(player.OutputTexture, Is.Null);
                Assert.That(image.texture, Is.Null);
            }
            finally
            {
                player.Stop();
                Object.Destroy(root);
                if (ownHost)
                {
                    UnityThread.Reset();
                }
            }
        }
    }
}

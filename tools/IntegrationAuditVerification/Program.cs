using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Resource;
using UnityEngine;
using UnityEngine.U2D;
using YooAsset;
using Object = UnityEngine.Object;

internal static class Program
{
    private static Pump context;
    private static int passed;
    private static int failed;

    private static void Main()
    {
        Run("static status and shutdown do not initialize YooAsset", () =>
        {
            Assert(!YooAssets.Initialized);
            Equal(0, YooAssets.Package.InitializeCalls);
            Assert(!ResourceManager.Initialized && !ResourceManager.PackageInitialized && !ResourceManager.IsReady);
            Assert(ResourceManager.PackageVersion == null && ResourceManager.DownloadProgress == null);
            Done(ResourceManager.ShutdownAsync());
            ResourceManager.Dispose();
            Assert(!YooAssets.Initialized);
            Equal(0, YooAssets.Package.InitializeCalls);
        });
        Run("concurrent exact versions get a busy result before the first version is ready", () =>
        {
            var package = YooAssets.Package;
            var gate = Gate();
            package.InitializeGate = gate.Task;
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Online)));
            var first = ResourceManager.DownloadAsync(ForVersion("release-a"));
            var second = Done(ResourceManager.DownloadAsync(ForVersion("release-b")));
            gate.SetResult(true);
            Assert(Done(first).IsReady);
            Equal(ResourceDownloadStatus.Busy, second.Status);
            Equal("release-a", ResourceManager.PackageVersion);
            Equal(1, package.InitializeCalls);
            Equal(1, package.ManifestRequests);
        });
        Run("download snapshots version and download settings before the first await", () =>
        {
            var package = YooAssets.Package;
            var gate = Gate();
            package.InitializeGate = gate.Task;
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Online)));
            var setting = ForVersion("pinned");
            setting.ConcurrentDownloads = 6;
            setting.FailedTryAgain = 1;
            var loading = ResourceManager.DownloadAsync(setting);
            setting.PackageVersion = "mutated";
            setting.ConcurrentDownloads = 1;
            setting.FailedTryAgain = 9;
            gate.SetResult(true);
            Assert(Done(loading).IsReady);
            Equal("pinned", ResourceManager.PackageVersion);
            Equal(6, package.DownloadConcurrency);
            Equal(1, package.DownloadRetries);
            Equal(1, package.Downloader.Begun);
        });
        Run("cancelled native download remains owned until shutdown observes its completion", () =>
        {
            var package = YooAssets.Package;
            var gate = Gate();
            package.Downloader.Task = gate.Task;
            using var stop = new CancellationTokenSource();
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Online)));
            var loading = ResourceManager.DownloadAsync(ForVersion("cancelled"), stop.Token);
            stop.Cancel();
            var cancelled = Done(loading);
            Task shutdown = ResourceManager.ShutdownAsync();
            bool finishedBeforeNative = shutdown.IsCompleted;
            gate.SetResult(true);
            Done(shutdown);
            Equal(ResourceDownloadStatus.Cancelled, cancelled.Status);
            Assert(!finishedBeforeNative, "Shutdown completed before the cancelled native download.");
            Assert(!ResourceManager.IsReady);
            Equal(1, package.Downloader.Cancelled);
        });
        Run("a retry cannot replace the manifest while a cancelled download is still native-active", () =>
        {
            var package = YooAssets.Package;
            var gate = Gate();
            package.Downloader.Task = gate.Task;
            using var stop = new CancellationTokenSource();
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Online)));
            var loading = ResourceManager.DownloadAsync(ForVersion("old"), stop.Token);
            stop.Cancel();
            var cancelled = Done(loading);
            var retry = Done(ResourceManager.DownloadAsync(ForVersion("new")));
            int manifestRequests = package.ManifestRequests;
            gate.SetResult(true);
            Equal(ResourceDownloadStatus.Cancelled, cancelled.Status);
            Equal(ResourceDownloadStatus.Busy, retry.Status);
            Equal(1, manifestRequests);
        });
        Run("concurrent offline callers share the same native initialization", () =>
        {
            var gate = Gate();
            YooAssets.Package.InitializeGate = gate.Task;
            var first = ResourceManager.InitializeAsync(ResourceMode.Offline);
            var second = ResourceManager.InitializeAsync(ResourceMode.Offline);
            gate.SetResult(true);
            Assert(Done(first) && Done(second));
            Equal(1, YooAssets.Package.InitializeCalls);
            Equal(1, YooAssets.Package.ManifestRequests);
        });
        Run("an offline package never satisfies a request for a remote download", () =>
        {
            var gate = Gate();
            YooAssets.Package.InitializeGate = gate.Task;
            var offline = ResourceManager.InitializeAsync(ResourceMode.Offline);
            var duringInitialization = Done(ResourceManager.DownloadAsync(ForVersion("1")));
            gate.SetResult(true);
            Assert(Done(offline));
            Equal(ResourceDownloadStatus.Failed, duringInitialization.Status);
            Equal(ResourceDownloadStatus.Failed, Done(ResourceManager.DownloadAsync(ForVersion("1"))).Status);
            Equal(0, YooAssets.Package.Downloader.Begun);
        });
        Run("a previous Play session waiting on shutdown cannot initialize the replacement session", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var old = YooAssets.Package;
            var destroy = Gate();
            old.DestroyGate = destroy.Task;
            Task shutdown = ResourceManager.ShutdownAsync();
            Assert(!shutdown.IsCompleted);
            var oldOffline = ResourceManager.InitializeAsync(ResourceMode.Offline);
            typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            destroy.SetResult(true);
            Done(shutdown);
            Assert(!Done(oldOffline) && !ResourceManager.IsReady, "An old initialization waiter resurrected content after Play reset.");
        });
        Run("worker cancellation dispatches downloader cancellation onto the initialized main thread", () =>
        {
            var package = YooAssets.Package;
            var gate = Gate();
            package.Downloader.Task = gate.Task;
            using var stop = new CancellationTokenSource();
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Online)));
            var loading = ResourceManager.DownloadAsync(ForVersion("worker-cancel"), stop.Token);
            Task.Run(() => stop.Cancel()).GetAwaiter().GetResult();
            bool ranBeforeMainThreadPump = package.Downloader.Cancelled != 0;
            context.Until(() => package.Downloader.Cancelled == 1);
            gate.SetResult(true);
            Equal(ResourceDownloadStatus.Cancelled, Done(loading).Status);
            Assert(!ranBeforeMainThreadPump, "Cancellation touched the downloader from the worker thread.");
        });
        Run("two sequential loads of one asset retain two handles and release one at a time", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var first = Done(ResourceManager.LoadAssetAsync<TextAsset>("shared"));
            var second = Done(ResourceManager.LoadAssetAsync<TextAsset>("shared"));
            Assert(first != null && ReferenceEquals(first, second));
            Equal(2, package.Assets.Count);
            Assert(!ReferenceEquals(package.Assets[0], package.Assets[1]));
            Assert(ResourceManager.Release(first));
            Equal(1, package.Assets.Count(handle => handle.IsValid));
            Assert(second != null);
            Assert(ResourceManager.Release(second));
            Equal(0, package.Assets.Count(handle => handle.IsValid));
            Assert(package.Assets.All(handle => handle.ReleaseCount == 1));
            Assert(!ResourceManager.Release(first) && !ResourceManager.Release(null) && !ResourceManager.Release(new TextAsset()));
        });
        Run("concurrent loads of one asset keep independent handles before and after completion", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var gate = Gate();
            package.AssetGate = gate.Task;
            var first = ResourceManager.LoadAssetAsync<TextAsset>("shared");
            var second = ResourceManager.LoadAssetAsync<TextAsset>("shared");
            int handlesBeforeNative = package.Assets.Count;
            gate.SetResult(true);
            var asset = Done(first);
            Assert(asset != null && ReferenceEquals(asset, Done(second)));
            Equal(2, handlesBeforeNative);
            Assert(ResourceManager.Release(asset));
            Equal(1, package.Assets.Count(handle => handle.IsValid));
            Assert(ResourceManager.Release(asset));
            Equal(0, package.Assets.Count(handle => handle.IsValid));
        });
        Run("cancelling one waiter releases only its handle while another caller still loads", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var gate = Gate();
            package.AssetGate = gate.Task;
            using var stop = new CancellationTokenSource();
            var cancelled = ResourceManager.LoadAssetAsync<TextAsset>("shared", stop.Token);
            var waiting = ResourceManager.LoadAssetAsync<TextAsset>("shared");
            Task.Run(() => stop.Cancel()).GetAwaiter().GetResult();
            var stopped = Done(cancelled);
            bool remainingIsValid = package.Assets[1].IsValid && !waiting.IsCompleted;
            gate.SetResult(true);
            Assert(stopped == null && remainingIsValid);
            Equal(1, package.Assets[0].ReleaseCount);
            var asset = Done(waiting);
            Assert(asset != null && ResourceManager.Release(asset));
            Equal(1, package.Assets[1].ReleaseCount);
        });
        Run("synchronous and callback loads acquire references and unused unload does not release them", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var first = ResourceManager.LoadAsset<TextAsset>("shared");
            TextAsset second = null;
            Done(ResourceManager.LoadAssetAsync<TextAsset>("shared", result => second = result));
            ResourceManager.LoadAsset<TextAsset>("unused", callback: null);
            Done(ResourceManager.LoadAssetAsync<TextAsset>("unused", callback: null));
            Assert(first != null && ReferenceEquals(first, second));
            Equal(2, package.Assets.Count);
            ResourceManager.TryUnloadAsset("shared");
            Equal(2, package.Assets.Count(handle => handle.IsValid));
            Assert(ResourceManager.Release(first));
            Assert(ResourceManager.Release(second));
        });
        Run("failed loads and type mismatches return null and immediately release their own handles", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            package.FailNextAsset = true;
            TextAsset result = new TextAsset();
            Done(ResourceManager.LoadAssetAsync<TextAsset>("failed", asset => result = asset));
            Assert(result == null);
            Equal(1, package.Assets[0].ReleaseCount);

            var first = ResourceManager.LoadAsset<TextAsset>("wrong-type");
            Assert(first != null && Done(ResourceManager.LoadAssetAsync<GameObject>("wrong-type")) == null);
            Equal(1, package.Assets[2].ReleaseCount);
            Assert(package.Assets[1].IsValid && ResourceManager.Release(first));
        });
        Run("sprites from one atlas each own their atlas handle and a missing sprite retires its handle", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var first = Done(ResourceManager.LoadSpriteAsync("atlas", "one"));
            var second = ResourceManager.LoadSprite("atlas", "one");
            Sprite third = null;
            Done(ResourceManager.LoadSpriteAsync("atlas", "two", sprite => third = sprite));
            var missing = Done(ResourceManager.LoadSpriteAsync("atlas", "missing"));
            Assert(first != null && second != null && third != null && !ReferenceEquals(first, second));
            Equal(4, package.Assets.Count);
            Assert(package.Assets.All(handle => ReferenceEquals(handle.AssetObject, package.Assets[0].AssetObject)));
            Assert(missing == null && package.Assets[3].ReleaseCount == 1);
            Assert(ResourceManager.Release(second));
            Assert(package.Assets[0].IsValid && !package.Assets[1].IsValid && package.Assets[2].IsValid);
            Assert(first != null && third != null && second == null);
            Assert(!ResourceManager.Release(second));
            Assert(ResourceManager.Release(first) && ResourceManager.Release(third));
            Equal(0, package.Assets.Count(handle => handle.IsValid));
        });
        Run("automatic atlas bindings keep their reference until package shutdown", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            SpriteAtlas bound = null;
            SpriteAtlasManager.RequestForTest("auto", atlas => bound = atlas);
            context.Until(() => bound != null);
            var explicitAtlas = Done(ResourceManager.LoadSpriteAtlasAsync("Assets/GameData/SpriteAtlas/auto.spriteatlas"));
            Assert(ReferenceEquals(bound, explicitAtlas));
            Assert(ResourceManager.Release(explicitAtlas) && !ResourceManager.Release(bound));
            Assert(package.Assets[0].IsValid && !package.Assets[1].IsValid);
            Done(ResourceManager.ShutdownAsync());
            Assert(package.Assets.All(handle => handle.ReleaseCount == 1));
        });
        Run("a destroyed prefab instance can still return its acquisition", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var instance = Done(ResourceManager.LoadInstantiateAsync("prefab"));
            Assert(instance != null && package.Assets[0].IsValid);
            Object.Destroy(instance);
            Assert(instance == null && ResourceManager.Release(instance));
            Equal(1, package.Assets[0].ReleaseCount);
        });
        Run("reset retires outstanding and pending handles without publishing a late object", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var asset = Done(ResourceManager.LoadAssetAsync<TextAsset>("remaining"));
            var sprite = Done(ResourceManager.LoadSpriteAsync("atlas", "remaining"));
            var gate = Gate();
            package.AssetGate = gate.Task;
            var pending = ResourceManager.LoadAssetAsync<TextAsset>("pending");
            bool reset = Done(ResourceManager.ResetAsync());
            var result = Done(pending);
            gate.SetResult(true);
            Assert(reset && result == null);
            Assert(!ResourceManager.Release(asset) && !ResourceManager.Release(sprite));
            Assert(package.Assets.All(handle => handle.ReleaseCount == 1));
            Equal(1, package.DestroyCalls);
        });
        Run("play reset drops old references and late tasks without touching the next package", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var old = YooAssets.Package;
            var gate = Gate();
            old.AssetGate = gate.Task;
            var pending = ResourceManager.LoadAssetAsync<TextAsset>("shared");
            typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            YooAssets.Package = new ResourcePackage();
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var next = Done(ResourceManager.LoadAssetAsync<TextAsset>("shared"));
            gate.SetResult(true);
            Assert(Done(pending) == null);
            Assert(YooAssets.Package.Assets[0].IsValid && ResourceManager.Release(next));
            Equal(1, old.Assets[0].ReleaseCount);
        });
        Run("resource calls before initialization return safe results", () =>
        {
            Assert(ResourceManager.LoadAsset<TextAsset>("text") == null);
            Assert(Done(ResourceManager.LoadAssetAsync<TextAsset>("text")) == null);
            Assert(ResourceManager.CreateSceneLoader() == null);
            Assert(!Done(ResourceManager.LoadSceneAsync("scene")).IsValid());
            Assert(ResourceManager.LoadSceneHandleAsync("scene") == null);
            Done(ResourceManager.UnloadSceneAsync((string)null));
            Assert(!ResourceManager.Release(new TextAsset()));
            Done(ResourceManager.ShutdownAsync());
            Done(ResourceManager.ShutdownAsync());
            Equal(0, YooAssets.Package.Assets.Count);
        });
        Run("worker calls fail without invoking YooAsset or mutating ownership", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var held = Done(ResourceManager.LoadAssetAsync<TextAsset>("held"));
            Task.Run(async () =>
            {
                Assert(!await ResourceManager.InitializeAsync(ResourceMode.Offline));
                Equal(ResourceDownloadStatus.Failed, (await ResourceManager.DownloadAsync(ForVersion("worker"))).Status);
                Assert(!await ResourceManager.ResetAsync());
                await ResourceManager.ShutdownAsync();
                Assert(await ResourceManager.LoadAssetAsync<TextAsset>("worker") == null);
                bool callbackInvoked = false;
                await ResourceManager.LoadAssetAsync<TextAsset>("worker", _ => callbackInvoked = true);
                Assert(!callbackInvoked);
                Assert(!ResourceManager.Release(held));
                Assert(ResourceManager.CreateSceneLoader() == null);
                Assert(!(await ResourceManager.LoadSceneAsync("worker")).IsValid());
                ResourceManager.ReleaseAllAssets();
                ResourceManager.Dispose();
            }).GetAwaiter().GetResult();
            Equal(1, package.Assets.Count);
            Equal(0, package.Scenes.Count);
            Assert(ResourceManager.IsReady && ResourceManager.Release(held));
        });
        Run("scene failures return default and an unsuccessful unload can be retried", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            package.FailNextScene = true;
            Assert(!Done(ResourceManager.LoadSceneAsync("scene", UnityEngine.SceneManagement.LoadSceneMode.Additive)).IsValid());
            Equal(1, package.Scenes[0].ReleaseCount);

            using var loader = (YooAssetSceneLoader)ResourceManager.CreateSceneLoader();
            var mode = UnityEngine.SceneManagement.LoadSceneMode.Additive;
            var scene = Done(loader.LoadAsync("retry", mode, null));
            var handle = package.Scenes[1];
            handle.FailUnload = true;
            Done(loader.UnloadAsync(scene));
            Equal(0, handle.ReleaseCount);
            handle.FailUnload = false;
            Done(loader.UnloadAsync(scene));
            Equal(1, handle.ReleaseCount);
            Done(loader.UnloadAsync(scene));
        });
        Run("reset keeps a scene in use until a subsequent unload succeeds", () =>
        {
            Assert(Done(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var text = Done(ResourceManager.LoadAssetAsync<TextAsset>("held"));
            var scene = Done(ResourceManager.LoadSceneAsync("live", UnityEngine.SceneManagement.LoadSceneMode.Additive));
            Assert(scene.IsValid());
            package.Scenes[0].FailUnload = true;
            Assert(!Done(ResourceManager.ResetAsync()));
            Equal(0, package.DestroyCalls);
            Assert(!ResourceManager.IsReady && package.Assets[0].IsValid);
            package.Scenes[0].FailUnload = false;
            Assert(Done(ResourceManager.ResetAsync()));
            Assert(!ResourceManager.Release(text));
            Equal(1, package.DestroyCalls);
            Equal(1, package.Scenes[0].ReleaseCount);
        });
        Console.WriteLine("RESULT integration audit passed=" + passed + " failed=" + failed + "; Unity/YooAsset API models, not native I/O or device performance.");
        if (failed != 0)
        {
            Environment.ExitCode = 1;
        }
    }

    private static RemoteDownloadSetting ForVersion(string version) =>
        new RemoteDownloadSetting { MainUrl = "https://example.test", PackageVersion = version };

    private static TaskCompletionSource<bool> Gate() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void Run(string name, Action body)
    {
        Object.ResetTestState();
        YooAssets.Package = new ResourcePackage();
        context = new Pump();
        SynchronizationContext.SetSynchronizationContext(context);
        UnityThread.Reset();
        UnityThread.Initialize();
        try
        {
            body();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error);
        }
        finally
        {
            // Every held native gate is opened in the scenario before asserting the regression.
            Done(ResourceManager.ShutdownAsync());
            ResourceManager.Dispose();
            UnityThread.Reset();
            Object.ResetTestState();
        }
    }

    private static void Done(Task task)
    {
        context.Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static T Done<T>(Task<T> task)
    {
        Done((Task)task);
        return task.GetAwaiter().GetResult();
    }

    private static void Assert(bool condition, string message = "Assertion failed")
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        Assert(EqualityComparer<T>.Default.Equals(expected, actual), "Expected " + expected + "; actual " + actual);
    }

    private sealed class Pump : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> pending = new ConcurrentQueue<(SendOrPostCallback, object)>();
        public override void Post(SendOrPostCallback callback, object state) => pending.Enqueue((callback, state));
        internal void Until(Func<bool> condition)
        {
            long started = Stopwatch.GetTimestamp();
            while (!condition())
            {
                if ((Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency > 10)
                {
                    throw new TimeoutException("Model main-thread wait did not complete.");
                }
                int count = pending.Count;
                for (int i = 0; i < count && pending.TryDequeue(out var item); i++)
                {
                    item.Callback(item.State);
                }
                Thread.Sleep(1);
            }
        }
    }
}

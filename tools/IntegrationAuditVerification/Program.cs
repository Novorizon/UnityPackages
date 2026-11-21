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
        Run("concurrent exact versions get a busy result before the first version is ready", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var gate = Gate();
            package.InitializeGate = gate.Task;
            Assert(Done(resources.InitializeAsync(ResourceMode.Online)));
            var first = resources.DownloadAsync(ForVersion("release-a"));
            var second = Done(resources.DownloadAsync(ForVersion("release-b")));
            gate.SetResult(true);
            Assert(Done(first).IsReady);
            Equal(ResourceDownloadStatus.Busy, second.Status);
            Equal("release-a", resources.PackageVersion);
            Equal(1, package.InitializeCalls);
            Equal(1, package.ManifestRequests);
        });
        Run("download snapshots version and download settings before the first await", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var gate = Gate();
            package.InitializeGate = gate.Task;
            Assert(Done(resources.InitializeAsync(ResourceMode.Online)));
            var setting = ForVersion("pinned");
            setting.ConcurrentDownloads = 6;
            setting.FailedTryAgain = 1;
            var loading = resources.DownloadAsync(setting);
            setting.PackageVersion = "mutated";
            setting.ConcurrentDownloads = 1;
            setting.FailedTryAgain = 9;
            gate.SetResult(true);
            Assert(Done(loading).IsReady);
            Equal("pinned", resources.PackageVersion);
            Equal(6, package.DownloadConcurrency);
            Equal(1, package.DownloadRetries);
            Equal(1, package.Downloader.Begun);
        });
        Run("cancelled native download remains owned until shutdown observes its completion", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var gate = Gate();
            package.Downloader.Task = gate.Task;
            using var stop = new CancellationTokenSource();
            Assert(Done(resources.InitializeAsync(ResourceMode.Online)));
            var loading = resources.DownloadAsync(ForVersion("cancelled"), stop.Token);
            stop.Cancel();
            var cancelled = Done(loading);
            Task shutdown = resources.ShutdownAsync();
            bool finishedBeforeNative = shutdown.IsCompleted;
            gate.SetResult(true);
            Done(shutdown);
            Equal(ResourceDownloadStatus.Cancelled, cancelled.Status);
            Assert(!finishedBeforeNative, "Shutdown completed before the cancelled native download.");
            Assert(!resources.IsReady);
            Equal(1, package.Downloader.Cancelled);
        });
        Run("a retry cannot replace the manifest while a cancelled download is still native-active", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var gate = Gate();
            package.Downloader.Task = gate.Task;
            using var stop = new CancellationTokenSource();
            Assert(Done(resources.InitializeAsync(ResourceMode.Online)));
            var loading = resources.DownloadAsync(ForVersion("old"), stop.Token);
            stop.Cancel();
            var cancelled = Done(loading);
            var retry = Done(resources.DownloadAsync(ForVersion("new")));
            int manifestRequests = package.ManifestRequests;
            gate.SetResult(true);
            Equal(ResourceDownloadStatus.Cancelled, cancelled.Status);
            Equal(ResourceDownloadStatus.Busy, retry.Status);
            Equal(1, manifestRequests);
        });
        Run("concurrent offline callers share the same native initialization", () =>
        {
            var resources = ResourceManager.Instance;
            var gate = Gate();
            YooAssets.Package.InitializeGate = gate.Task;
            var first = resources.InitializeAsync(ResourceMode.Offline);
            var second = resources.InitializeAsync(ResourceMode.Offline);
            gate.SetResult(true);
            Assert(Done(first) && Done(second));
            Equal(1, YooAssets.Package.InitializeCalls);
            Equal(1, YooAssets.Package.ManifestRequests);
        });
        Run("an offline package never satisfies a request for a remote download", () =>
        {
            var resources = ResourceManager.Instance;
            var gate = Gate();
            YooAssets.Package.InitializeGate = gate.Task;
            var offline = resources.InitializeAsync(ResourceMode.Offline);
            var duringInitialization = Done(resources.DownloadAsync(ForVersion("1")));
            gate.SetResult(true);
            Assert(Done(offline));
            Equal(ResourceDownloadStatus.Failed, duringInitialization.Status);
            Equal(ResourceDownloadStatus.Failed, Done(resources.DownloadAsync(ForVersion("1"))).Status);
            Equal(0, YooAssets.Package.Downloader.Begun);
        });
        Run("a previous Play session waiting on shutdown cannot initialize the replacement session", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var lease = Done(resources.AcquireAsync<TextAsset>("native-consumer", keepAliveUntilDisposed: true));
            Task shutdown = resources.ShutdownAsync();
            Assert(!shutdown.IsCompleted);
            var oldOffline = resources.InitializeAsync(ResourceMode.Offline);
            typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resources, null);
            Done(shutdown);
            Assert(!Done(oldOffline) && !resources.IsReady, "An old initialization waiter resurrected content after Play reset.");
            Assert(!lease.IsValid);
        });
        Run("worker cancellation dispatches downloader cancellation onto the initialized main thread", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var gate = Gate();
            package.Downloader.Task = gate.Task;
            using var stop = new CancellationTokenSource();
            Assert(Done(resources.InitializeAsync(ResourceMode.Online)));
            var loading = resources.DownloadAsync(ForVersion("worker-cancel"), stop.Token);
            Task.Run(() => stop.Cancel()).GetAwaiter().GetResult();
            bool ranBeforeMainThreadPump = package.Downloader.Cancelled != 0;
            context.Until(() => package.Downloader.Cancelled == 1);
            gate.SetResult(true);
            Equal(ResourceDownloadStatus.Cancelled, Done(loading).Status);
            Assert(!ranBeforeMainThreadPump, "Cancellation touched the downloader from the worker thread.");
        });
        Run("two sequential loads of one asset retain two handles and release one at a time", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var first = Done(resources.LoadAssetAsync<TextAsset>("shared"));
            var second = Done(resources.LoadAssetAsync<TextAsset>("shared"));
            Assert(first != null && ReferenceEquals(first, second));
            Equal(2, package.Assets.Count);
            Assert(!ReferenceEquals(package.Assets[0], package.Assets[1]));
            Assert(resources.Release(first));
            Equal(1, package.Assets.Count(handle => handle.IsValid));
            Assert(second != null);
            Assert(resources.Release(second));
            Equal(0, package.Assets.Count(handle => handle.IsValid));
            Assert(package.Assets.All(handle => handle.ReleaseCount == 1));
            Assert(!resources.Release(first) && !resources.Release(null) && !resources.Release(new TextAsset()));
        });
        Run("concurrent loads of one asset keep independent handles before and after completion", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var gate = Gate();
            package.AssetGate = gate.Task;
            var first = resources.LoadAssetAsync<TextAsset>("shared");
            var second = resources.LoadAssetAsync<TextAsset>("shared");
            int handlesBeforeNative = package.Assets.Count;
            gate.SetResult(true);
            var asset = Done(first);
            Assert(asset != null && ReferenceEquals(asset, Done(second)));
            Equal(2, handlesBeforeNative);
            Assert(resources.Release(asset));
            Equal(1, package.Assets.Count(handle => handle.IsValid));
            Assert(resources.Release(asset));
            Equal(0, package.Assets.Count(handle => handle.IsValid));
        });
        Run("object release cannot consume a legacy lease for the same object", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            using var lease = Done(resources.AcquireAsync<TextAsset>("shared"));
            var asset = Done(resources.LoadAssetAsync<TextAsset>("shared"));
            Assert(ReferenceEquals(lease.Asset, asset) && resources.Release(asset));
            Assert(!resources.Release(asset) && lease.IsValid && package.Assets[0].IsValid);
            lease.Dispose();
            Equal(0, package.Assets.Count(handle => handle.IsValid));
        });
        Run("cancelling one waiter releases only its handle while another caller still loads", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var gate = Gate();
            package.AssetGate = gate.Task;
            using var stop = new CancellationTokenSource();
            var cancelled = resources.LoadAssetAsync<TextAsset>("shared", stop.Token);
            var waiting = resources.LoadAssetAsync<TextAsset>("shared");
            Task.Run(() => stop.Cancel()).GetAwaiter().GetResult();
            var stopped = Done(cancelled);
            bool remainingIsValid = package.Assets[1].IsValid && !waiting.IsCompleted;
            gate.SetResult(true);
            Assert(stopped == null && remainingIsValid);
            Equal(1, package.Assets[0].ReleaseCount);
            var asset = Done(waiting);
            Assert(asset != null && resources.Release(asset));
            Equal(1, package.Assets[1].ReleaseCount);
        });
        Run("synchronous and callback loads acquire references and unused unload does not release them", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var first = resources.LoadAsset<TextAsset>("shared");
            TextAsset second = null;
            Done(resources.LoadAssetAsync<TextAsset>("shared", result => second = result));
            resources.LoadAsset<TextAsset>("unused", callback: null);
            Done(resources.LoadAssetAsync<TextAsset>("unused", callback: null));
            Assert(first != null && ReferenceEquals(first, second));
            Equal(2, package.Assets.Count);
            resources.TryUnloadAsset("shared");
            Equal(2, package.Assets.Count(handle => handle.IsValid));
            Assert(resources.Release(first));
            Assert(resources.Release(second));
        });
        Run("failed loads and type mismatches return null and immediately release their own handles", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            package.FailNextAsset = true;
            TextAsset result = new TextAsset();
            Done(resources.LoadAssetAsync<TextAsset>("failed", asset => result = asset));
            Assert(result == null);
            Equal(1, package.Assets[0].ReleaseCount);

            var first = resources.LoadAsset<TextAsset>("wrong-type");
            Assert(first != null && Done(resources.LoadAssetAsync<GameObject>("wrong-type")) == null);
            Equal(1, package.Assets[2].ReleaseCount);
            Assert(package.Assets[1].IsValid && resources.Release(first));
        });
        Run("sprites from one atlas each own their atlas handle and a missing sprite retires its handle", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var first = Done(resources.LoadSpriteAsync("atlas", "one"));
            var second = resources.LoadSprite("atlas", "one");
            Sprite third = null;
            Done(resources.LoadSpriteAsync("atlas", "two", sprite => third = sprite));
            var missing = Done(resources.LoadSpriteAsync("atlas", "missing"));
            Assert(first != null && second != null && third != null && !ReferenceEquals(first, second));
            Equal(4, package.Assets.Count);
            Assert(package.Assets.All(handle => ReferenceEquals(handle.AssetObject, package.Assets[0].AssetObject)));
            Assert(missing == null && package.Assets[3].ReleaseCount == 1);
            Assert(resources.Release(second));
            Assert(package.Assets[0].IsValid && !package.Assets[1].IsValid && package.Assets[2].IsValid);
            Assert(first != null && third != null && second == null);
            Assert(!resources.Release(second));
            Assert(resources.Release(first) && resources.Release(third));
            Equal(0, package.Assets.Count(handle => handle.IsValid));
        });
        Run("automatic atlas bindings keep their reference until package shutdown", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            SpriteAtlas bound = null;
            SpriteAtlasManager.RequestForTest("auto", atlas => bound = atlas);
            context.Until(() => bound != null);
            var explicitAtlas = Done(resources.LoadSpriteAtlasAsync("Assets/GameData/SpriteAtlas/auto.spriteatlas"));
            Assert(ReferenceEquals(bound, explicitAtlas));
            Assert(resources.Release(explicitAtlas) && !resources.Release(bound));
            Assert(package.Assets[0].IsValid && !package.Assets[1].IsValid);
            Done(resources.ShutdownAsync());
            Assert(package.Assets.All(handle => handle.ReleaseCount == 1));
        });
        Run("a destroyed prefab instance can still return its acquisition", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var instance = Done(resources.LoadInstantiateAsync("prefab"));
            Assert(instance != null && package.Assets[0].IsValid);
            Object.Destroy(instance);
            Assert(instance == null && resources.Release(instance));
            Equal(1, package.Assets[0].ReleaseCount);
        });
        Run("reset retires outstanding and pending handles without publishing a late object", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var package = YooAssets.Package;
            var asset = Done(resources.LoadAssetAsync<TextAsset>("remaining"));
            var sprite = Done(resources.LoadSpriteAsync("atlas", "remaining"));
            var gate = Gate();
            package.AssetGate = gate.Task;
            var pending = resources.LoadAssetAsync<TextAsset>("pending");
            bool reset = Done(resources.ResetAsync());
            var result = Done(pending);
            gate.SetResult(true);
            Assert(reset && result == null);
            Assert(!resources.Release(asset) && !resources.Release(sprite));
            Assert(package.Assets.All(handle => handle.ReleaseCount == 1));
            Equal(1, package.DestroyCalls);
        });
        Run("play reset drops old references and late tasks without touching the next package", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var old = YooAssets.Package;
            var gate = Gate();
            old.AssetGate = gate.Task;
            var pending = resources.LoadAssetAsync<TextAsset>("shared");
            typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resources, null);
            YooAssets.Package = new ResourcePackage();
            Assert(Done(resources.InitializeAsync(ResourceMode.Offline)));
            var next = Done(resources.LoadAssetAsync<TextAsset>("shared"));
            gate.SetResult(true);
            Assert(Done(pending) == null);
            Assert(YooAssets.Package.Assets[0].IsValid && resources.Release(next));
            Equal(1, old.Assets[0].ReleaseCount);
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
            if (ResourceManager.TryGetInstance(out var resources))
            {
                Done(resources.ShutdownAsync());
            }
            Singleton<ResourceManager>.Release();
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

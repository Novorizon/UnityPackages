using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Resource;
using UnityEngine;
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
            Assert(Done(resources.InitializeAsync()));
            var lease = Done(resources.AcquireAsync<TextAsset>("native-consumer", keepAliveUntilDisposed: true));
            Task shutdown = resources.ShutdownAsync();
            Assert(!shutdown.IsCompleted);
            var oldDefault = resources.InitializeAsync();
            var oldOffline = resources.InitializeAsync(ResourceMode.Offline);
            typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resources, null);
            Done(shutdown);
            Assert(!Done(oldDefault) && !Done(oldOffline) && !resources.IsReady, "An old initialization waiter resurrected content after Play reset.");
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
        Run("prewarmed shared-cache queries allocate no managed memory", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync()));
            var loaded = resources.LoadAsset<TextAsset>("cached");
            for (int i = 0; i < 10000; i++)
            {
                resources.TryGetLoaded<TextAsset>("cached", out _);
            }
            const int samples = 20;
            const int operations = 10000;
            var timings = new double[samples];
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int sample = 0; sample < samples; sample++)
            {
                long started = Stopwatch.GetTimestamp();
                for (int i = 0; i < operations; i++)
                {
                    if (!resources.TryGetLoaded<TextAsset>("cached", out var value) || !ReferenceEquals(value, loaded))
                    {
                        throw new Exception("The measured cache query changed behavior.");
                    }
                }
                timings[sample] = (Stopwatch.GetTimestamp() - started) * (1000000000d / Stopwatch.Frequency) / operations;
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            Array.Sort(timings);
            Console.WriteLine("PERF resource-cache-hit operations=" + samples * operations + " allocatedBytes=" + allocated +
                " medianNanoseconds=" + timings[samples / 2].ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                " p95Nanoseconds=" + timings[samples - 2].ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            Equal(0L, allocated);
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

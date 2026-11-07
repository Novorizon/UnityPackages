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
        Run("concurrent exact content versions cannot report readiness for another version", () =>
        {
            var resources = ResourceManager.Instance;
            var gate = Gate();
            YooAssets.Package.InitializeGate = gate.Task;
            Task<bool> first = resources.PrepareContentAsync(new ResourceInitializationOptions { PackageVersion = "release-a" });
            Task<bool> second = resources.PrepareContentAsync(new ResourceInitializationOptions { PackageVersion = "release-b" });
            gate.SetResult(true);
            Assert(Done(first));
            Throws<InvalidOperationException>(second);
            Equal("release-a", resources.PackageVersion);
            Equal(1, YooAssets.Package.InitializeCalls);
            Equal(1, YooAssets.Package.ManifestRequests);
        });
        Run("initialization snapshots version and download options before its first await", () =>
        {
            var resources = ResourceManager.Instance;
            var gate = Gate();
            YooAssets.Package.InitializeGate = gate.Task;
            var options = new ResourceInitializationOptions { PackageVersion = "pinned", DownloadAll = true };
            Task<bool> loading = resources.PrepareContentAsync(options);
            options.PackageVersion = "mutated";
            options.DownloadAll = false;
            gate.SetResult(true);
            Assert(Done(loading));
            Equal("pinned", resources.PackageVersion);
            Equal(1, YooAssets.Package.Downloader.Begun);
        });
        Run("cancelled native download remains owned until shutdown observes its completion", () =>
        {
            var resources = ResourceManager.Instance;
            var gate = Gate();
            YooAssets.Package.Downloader.Task = gate.Task;
            using var stop = new CancellationTokenSource();
            Task<bool> loading = resources.PrepareContentAsync(new ResourceInitializationOptions
            {
                PackageVersion = "cancelled",
                DownloadAll = true
            }, stop.Token);
            stop.Cancel();
            Throws<OperationCanceledException>(loading);
            Task shutdown = resources.ShutdownAsync();
            bool finishedBeforeNative = shutdown.IsCompleted;
            gate.SetResult(true);
            Done(shutdown);
            Assert(!finishedBeforeNative, "Shutdown completed before the cancelled native download.");
            Assert(!resources.Initialized);
            Equal(1, YooAssets.Package.Downloader.Cancelled);
        });
        Run("a retry cannot replace the manifest while a cancelled download is still native-active", () =>
        {
            var resources = ResourceManager.Instance;
            var gate = Gate();
            YooAssets.Package.Downloader.Task = gate.Task;
            using var stop = new CancellationTokenSource();
            Task<bool> loading = resources.PrepareContentAsync(new ResourceInitializationOptions
            {
                PackageVersion = "old",
                DownloadAll = true
            }, stop.Token);
            stop.Cancel();
            Throws<OperationCanceledException>(loading);
            Task<bool> retry = resources.PrepareContentAsync(new ResourceInitializationOptions { PackageVersion = "new" });
            bool changedBeforeNative = YooAssets.Package.ManifestRequests != 1;
            gate.SetResult(true);
            try
            {
                Done(retry);
            }
            catch (OperationCanceledException)
            {
                // A caller that joined the retiring operation is allowed to observe its cancellation.
            }
            catch (InvalidOperationException)
            {
                // Conflicting exact versions may be rejected while the previous one retires.
            }
            Assert(!changedBeforeNative, "A retry prepared a new manifest before native cancellation completed.");
        });
        Run("compatible callers still share the same native initialization", () =>
        {
            var resources = ResourceManager.Instance;
            var gate = Gate();
            YooAssets.Package.InitializeGate = gate.Task;
            Task<bool> first = resources.PrepareContentAsync(new ResourceInitializationOptions { PackageVersion = "shared" });
            Task<bool> second = resources.PrepareContentAsync(new ResourceInitializationOptions { PackageVersion = "shared" });
            gate.SetResult(true);
            Assert(Done(first) && Done(second));
            Equal(1, YooAssets.Package.InitializeCalls);
            Equal(1, YooAssets.Package.ManifestRequests);
        });
        Run("an on-demand initialization cannot silently satisfy a full-download waiter", () =>
        {
            var resources = ResourceManager.Instance;
            var gate = Gate();
            YooAssets.Package.InitializeGate = gate.Task;
            Task<bool> first = resources.PrepareContentAsync(new ResourceInitializationOptions { PackageVersion = "on-demand" });
            Task<bool> full = resources.PrepareContentAsync(new ResourceInitializationOptions
            {
                PackageVersion = "on-demand",
                DownloadAll = true
            });
            gate.SetResult(true);
            Assert(Done(first));
            Throws<InvalidOperationException>(full);
            Throws<InvalidOperationException>(resources.PrepareContentAsync(new ResourceInitializationOptions
            {
                PackageVersion = "on-demand",
                DownloadAll = true
            }));
            Equal(0, YooAssets.Package.Downloader.Begun);
        });
        Run("a previous Play session waiting on shutdown cannot initialize the replacement session", () =>
        {
            var resources = ResourceManager.Instance;
            Assert(Done(resources.InitializeAsync()));
            var lease = Done(resources.AcquireAsync<TextAsset>("native-consumer", keepAliveUntilDisposed: true));
            Task shutdown = resources.ShutdownAsync();
            Assert(!shutdown.IsCompleted);
            Task<bool> oldDefault = resources.InitializeAsync();
            Task<bool> oldPinned = resources.PrepareContentAsync(new ResourceInitializationOptions { PackageVersion = "retired-session" });
            typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resources, null);
            Done(shutdown);
            bool defaultReady = Done(oldDefault);
            bool pinnedReady;
            try
            {
                pinnedReady = Done(oldPinned);
            }
            catch (InvalidOperationException)
            {
                pinnedReady = true;
            }
            Assert(!defaultReady && !pinnedReady && !resources.Initialized, "An old initialization waiter resurrected content after Play reset.");
            Assert(!lease.IsValid);
        });
        Run("worker cancellation dispatches downloader cancellation onto the initialized main thread", () =>
        {
            var resources = ResourceManager.Instance;
            var gate = Gate();
            YooAssets.Package.Downloader.Task = gate.Task;
            using var stop = new CancellationTokenSource();
            Task<bool> loading = resources.PrepareContentAsync(new ResourceInitializationOptions
            {
                PackageVersion = "worker-cancel",
                DownloadAll = true
            }, stop.Token);
            Task.Run(() => stop.Cancel()).GetAwaiter().GetResult();
            bool ranBeforeMainThreadPump = YooAssets.Package.Downloader.Cancelled != 0;
            context.Until(() => YooAssets.Package.Downloader.Cancelled == 1);
            gate.SetResult(true);
            Throws<OperationCanceledException>(loading);
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

    private static void Throws<T>(Task task) where T : Exception
    {
        try
        {
            Done(task);
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name);
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

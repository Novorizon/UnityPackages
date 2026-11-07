using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Diagnostics.Editor;
using Game.Resource;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using Object = UnityEngine.Object;

internal static class Program
{
    private static Pump context;
    private static int passed;

    private static void Main()
    {
        Run("sampling never creates resource service", () =>
        {
            Assert(!ResourceManager.TryGetInstance(out _));
            var snapshot = PackageDiagnostics.Capture();
            Assert(!ResourceManager.TryGetInstance(out _));
            Equal(0, snapshot.leases.Length);
        });
        Run("original literal-default token API remains unambiguous", () =>
        {
            var resources = Ready();
            var lease = Done(resources.AcquireAsync<TextAsset>("default-token", default));
            Equal("Unassigned", resources.CaptureLeaseDiagnostics().Single().Owner);
            lease.Dispose();
        });
        Run("owners remain distinct and direct leases preserve independent lifecycle", () =>
        {
            var resources = Ready();
            var first = Done(resources.AcquireOwnedAsync<TextAsset>("same", owner: "Inventory"));
            var second = Done(resources.AcquireOwnedAsync<TextAsset>("same", owner: "Map"));
            var before = resources.CaptureLeaseDiagnostics();
            Equal(2, before.Length);
            Equal("Inventory", before[0].Owner);
            Equal("Map", before[1].Owner);
            Equal("Active", before[0].State);
            Assert(before[0].LeaseId != before[1].LeaseId);
            first.Dispose();
            Equal(1, resources.CaptureLeaseDiagnostics().Length);
            Equal("Inventory", before[0].Owner);
            Assert(second.IsValid);
            second.Dispose();
            Equal(0, resources.CaptureLeaseDiagnostics().Length);
        });
        Run("already-cancelled and failed acquisitions leave no ownership history", () =>
        {
            var resources = Ready();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Fails<OperationCanceledException>(resources.AcquireOwnedAsync<TextAsset>("cancelled", "Owner", cancelled.Token));
            Equal(0, YooAssets.Package.Assets.Count);
            Equal(0, resources.CaptureLeaseDiagnostics().Length);
            YooAssets.Package.FailNextAsset = true;
            Fails<InvalidOperationException>(resources.AcquireOwnedAsync<TextAsset>("failed", "Owner"));
            Equal(0, resources.CaptureLeaseDiagnostics().Length);
            Equal(1, YooAssets.Package.Assets.Single().ReleaseCount);
        });
        Run("scope labels attach before pending acquisitions and cancellation remains observable", () =>
        {
            var resources = Ready();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            YooAssets.Package.AssetGate = gate.Task;
            var scope = resources.CreateScope("UI/Inventory");
            var pending = scope.AcquireAsync<TextAsset>("late");
            var loading = resources.CaptureLeaseDiagnostics().Single();
            Equal("Loading", loading.State);
            Equal(scope.DiagnosticId, loading.ScopeId);
            Equal("UI/Inventory", loading.Owner);
            scope.Dispose();
            Fails<OperationCanceledException>(pending);
            var releasing = resources.CaptureLeaseDiagnostics().Single();
            Equal("Releasing", releasing.State);
            Equal(loading.LeaseId, releasing.LeaseId);
            Equal(0, YooAssets.Package.Assets[0].ReleaseCount);
            gate.SetResult(true);
            context.Until(() => resources.OwnedAssetCount == 0);
            Equal(1, YooAssets.Package.Assets[0].ReleaseCount);
        });
        Run("scene ownership is scalar and scene unload cannot release another owner", () =>
        {
            var resources = Ready();
            var scene = SceneManager.CreateForTest("Battle");
            var scope = resources.CreateSceneScope(scene, "Battle/HUD");
            var own = Done(scope.AcquireAsync<TextAsset>("same"));
            var other = Done(resources.AcquireOwnedAsync<TextAsset>("same", owner: "Global"));
            var row = resources.CaptureLeaseDiagnostics().Single(item => item.ScopeId == scope.DiagnosticId);
            Equal(scene.handle, row.SceneHandle);
            Equal("Battle", row.SceneName);
            SceneManager.UnloadSceneAsync(scene);
            Assert(!own.IsValid && other.IsValid);
            Equal("Global", resources.CaptureLeaseDiagnostics().Single().Owner);
            other.Dispose();
            scope.Dispose();
        });
        Run("stack recording is opt-in for future acquisitions only", () =>
        {
            var resources = Ready();
            var normal = Done(resources.AcquireAsync<TextAsset>("normal"));
            Assert(resources.CaptureLeaseDiagnostics().Single().CreationStack == null);
            resources.CaptureLeaseCreationStacks = true;
            var traced = Done(resources.AcquireAsync<TextAsset>("traced"));
            var tracedRow = resources.CaptureLeaseDiagnostics().Single(item => item.Location == "traced");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Assert(resources.CaptureLeaseCreationStacks);
            Assert(!string.IsNullOrEmpty(tracedRow.CreationStack));
            Assert(tracedRow.CreationStack.Length <= 8192);
#else
            Assert(!resources.CaptureLeaseCreationStacks);
            Assert(tracedRow.CreationStack == null);
#endif
            Assert(resources.CaptureLeaseDiagnostics().Single(item => item.Location == "normal").CreationStack == null);
            resources.CaptureLeaseCreationStacks = false;
            var later = Done(resources.AcquireAsync<TextAsset>("later"));
            Assert(resources.CaptureLeaseDiagnostics().Single(item => item.Location == "later").CreationStack == null);
            normal.Dispose();
            traced.Dispose();
            later.Dispose();
        });
        Run("snapshots and differences capture public ownership without confusing same-address leases", () =>
        {
            var resources = Ready();
            var first = Done(resources.AcquireOwnedAsync<TextAsset>("same", owner: "First"));
            var baseline = PackageDiagnostics.Capture();
            Equal(1, baseline.leases.Length);
            var second = Done(resources.AcquireOwnedAsync<TextAsset>("same", owner: "Second"));
            var added = DiagnosticSnapshotComparer.Compare(baseline, PackageDiagnostics.Capture());
            Equal(1, added.leases.Length);
            Equal("Added", added.leases[0].kind);
            Equal("Second", added.leases[0].after.owner);
            first.Dispose();
            var changed = DiagnosticSnapshotComparer.Compare(baseline, PackageDiagnostics.Capture());
            Equal(2, changed.leases.Length);
            Assert(changed.leases.Any(item => item.kind == "Removed" && item.before.owner == "First"));
            Assert(changed.leases.Any(item => item.kind == "Added" && item.after.owner == "Second"));
            Equal(1, baseline.leases.Length);
            second.Dispose();
        });
        Run("manager recreation cannot reuse an observed lease identity", () =>
        {
            var first = Ready();
            var lease = Done(first.AcquireAsync<TextAsset>("asset"));
            var old = first.CaptureLeaseDiagnostics().Single();
            lease.Dispose();
            Done(first.ShutdownAsync());
            Singleton<ResourceManager>.Release();
            YooAssets.Package = new ResourcePackage();
            var replacement = Ready();
            var next = Done(replacement.AcquireAsync<TextAsset>("asset"));
            var current = replacement.CaptureLeaseDiagnostics().Single();
            Equal(old.LeaseId, current.LeaseId);
            Assert(old.SessionId != current.SessionId);
            next.Dispose();
        });
        Run("logical release state is visible in a baseline diff until native loading ends", () =>
        {
            var resources = Ready();
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            YooAssets.Package.AssetGate = gate.Task;
            using var cancel = new CancellationTokenSource();
            var pending = resources.AcquireOwnedAsync<TextAsset>("late", owner: "Scene/A", token: cancel.Token);
            var before = PackageDiagnostics.Capture();
            cancel.Cancel();
            Fails<OperationCanceledException>(pending);
            var diff = DiagnosticSnapshotComparer.Compare(before, PackageDiagnostics.Capture());
            Equal("Changed", diff.leases.Single().kind);
            Equal("Loading", diff.leases[0].before.state);
            Equal("Releasing", diff.leases[0].after.state);
            gate.SetResult(true);
            context.Until(() => resources.OwnedAssetCount == 0);
            Equal("Removed", DiagnosticSnapshotComparer.Compare(before, PackageDiagnostics.Capture()).leases.Single().kind);
        });
        Console.WriteLine("PASS " + passed + " resource ownership/diff scenarios; Unity/YooAsset boundaries are models, not native loading.");
    }

    private static ResourceManager Ready()
    {
        var resources = ResourceManager.Instance;
        Done(resources.InitializeAsync());
        return resources;
    }

    private static void Run(string name, Action action)
    {
        Object.ResetTestState();
        YooAssets.Package = new ResourcePackage();
        context = new Pump();
        SynchronizationContext.SetSynchronizationContext(context);
        UnityThread.Reset();
        UnityThread.Initialize();
        try
        {
            action();
        }
        finally
        {
            if (ResourceManager.TryGetInstance(out var resources))
            {
                Done(resources.ShutdownAsync());
            }
            Singleton<ResourceManager>.Release();
            UnityThread.Reset();
            Object.ResetTestState();
        }
        passed++;
        Console.WriteLine("PASS " + name);
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

    private static void Fails<T>(Task task) where T : Exception
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

    private static void Assert(bool value)
    {
        if (!value)
        {
            throw new Exception("Assertion failed");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception("Expected " + expected + "; actual " + actual);
        }
    }

    private sealed class Pump : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object State)> pending = new ConcurrentQueue<(SendOrPostCallback, object)>();
        public override void Post(SendOrPostCallback callback, object state) => pending.Enqueue((callback, state));
        internal void Until(Func<bool> condition)
        {
            var timeout = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                if (timeout.ElapsedMilliseconds > 10000)
                {
                    throw new TimeoutException("Model main-thread wait did not complete");
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

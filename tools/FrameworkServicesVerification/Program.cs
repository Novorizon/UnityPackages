using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Network;
using Google.Protobuf;
using UnityEngine;
using Object = UnityEngine.Object;

internal static partial class Program
{
    private static int passed;
    private static void Main()
    {
        VerifyDispatchLifecycle();
#if UNITY_EDITOR
        Check("retained GPU manager keeps playback configuration across lifecycle cleanup", () =>
        {
            Object.ResetTestState();
            var manager = GpuAnimatorManager.Instance;
            manager.PlaybackSpeed = .25f;
            manager.AutomaticUpdate = false;
            manager.IsPaused = true;
            Object.ResetSingletonsForPlay();
            True(ReferenceEquals(manager, GpuAnimatorManager.Instance));
            Equal(.25f, manager.PlaybackSpeed);
            True(!manager.AutomaticUpdate && manager.IsPaused);
        });
#endif
        Check("legacy lazy singletons remain compatible", () =>
        {
            True(!Simple.IsCreated);
            True(ReferenceEquals(Simple.Instance, Simple.Instance));
        });
        Check("ordinary singletons explicitly release without creating and can be reused", () =>
        {
            var first = Resettable.Instance;
            Resettable.Release();
            Equal(1, first.Disposals);
            True(!Resettable.IsCreated);
            var second = Resettable.Instance;
            True(!ReferenceEquals(first, second));
            Resettable.Release();
            Equal(1, second.Disposals);
            True(!Resettable.IsCreated);
            var third = Resettable.Instance;
            Resettable.Release();
            Resettable.Release();
            Equal(1, third.Disposals);
        });
        Check("Release does not create an unused singleton and application quit blocks creation", () =>
        {
            True(!NeverCreated.IsCreated);
            NeverCreated.Release();
            Object.MarkQuittingForTest();
            Equal(0, NeverCreated.Created);
            Throws<InvalidOperationException>(() =>
            {
                _ = NeverCreated.Instance;
            });
            Object.ResetSingletonsForPlay();
        });
        Check("singleton owners release instances before quitting", () =>
        {
            var old = Service.Instance;
            Singleton<Service>.Release();
            True(old.Disposed && !Service.IsCreated);
            Object.MarkQuittingForTest();
            Throws<InvalidOperationException>(() =>
            {
                _ = Service.Instance;
            });
            Object.ResetSingletonsForPlay();
            True(!ReferenceEquals(old, Service.Instance));
        });
        Check("quit prevents service access and recreation", () =>
        {
            var current = Service.Instance;
            Object.MarkQuittingForTest();
            Throws<InvalidOperationException>(() => _ = Service.Instance);
            Singleton<Service>.Release();
            Throws<InvalidOperationException>(() => _ = Service.Instance);
            Object.ResetSingletonsForPlay();
        });
        Check("central explicit cleanup continues after failure without constructing services during quit", () =>
        {
            var old = ThrowingReset.Instance;
            var next = Resettable.Instance;
            Object.MarkQuittingForTest();
            ThrowingReset.Release();
            Resettable.Release();
            True(old.Attempted && !ThrowingReset.IsCreated);
            Equal(1, next.Disposals);
            True(!NeverCreated.IsCreated);
            Object.ResetSingletonsForPlay();
        });
#if UNITY_EDITOR
        Check("resetting a retained component calls OnExit once and OnStart on its next use", () =>
        {
            Object.ResetTestState();
            var value = RetainedHook.Instance;
            int starts = value.Starts;
            Object.ResetSingletonsForPlay();
            Equal(1, value.Exits);
            True(!RetainedHook.TryGetInstance(out _));
            True(ReferenceEquals(value, RetainedHook.Instance));
            Equal(starts + 1, value.Starts);
            var quit = typeof(MonoSingleton<RetainedHook>).GetMethod("OnApplicationQuit", BindingFlags.Instance | BindingFlags.NonPublic);
            quit.Invoke(value, null);
            quit.Invoke(value, null);
            Equal(2, value.Exits);
            Object.ResetSingletonsForPlay();
            Equal(2, value.Exits);
        });
#endif
        Check("ECS uses its own Unity reset hook and explicit World Entity Pool ownership order", () =>
        {
            var reset = typeof(ECS.Ecs).GetMethod("OnSubsystemRegistration", BindingFlags.Static | BindingFlags.NonPublic);
            ECS.EcsBoundary.Calls.Clear();
            reset.Invoke(null, null);
            Equal("world,entity,pool", string.Join(",", ECS.EcsBoundary.Calls));
            ECS.EcsBoundary.Calls.Clear();
            reset.Invoke(null, null);
            Equal("world,entity,pool", string.Join(",", ECS.EcsBoundary.Calls));
            typeof(ECS.Ecs).GetMethod("ObserveApplicationQuit", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Application.QuitForTest();
            True(ECS.Ecs.IsQuitting);
            reset.Invoke(null, null);
            Object.ResetSingletonsForPlay();
            True(!ECS.Ecs.IsQuitting);
        });
        Check("singleton supports concurrent acquisition", () =>
        {
            Service.Reset();
            int created = Service.Created;
            var values = new Service[64];
            Parallel.For(0, values.Length, i => values[i] = Service.Instance);
            foreach (var value in values)
            {
                True(ReferenceEquals(values[0], value));
            }

            Equal(created + 1, Service.Created);
        });
        Check("TryGetInstance never creates and configured services cannot be replaced implicitly", () =>
        {
            Service.Reset();
            True(!Service.TryGetInstance(out _));
            Service.Configure(() => Service.New("configured"));
            Equal("configured", Service.Instance.Name);
            Throws<InvalidOperationException>(() => Service.Configure(() => Service.New("replacement")));
        });
        Check("reset releases outside the lock and blocks creation during cleanup", () =>
        {
            Service.Reset();
            var old = Service.Instance;
            Service.ResetWith(value =>
            {
                True(Task.Run(() => !Service.IsCreated).Wait(2000));
                Throws<InvalidOperationException>(() =>
                {
                    _ = Service.Instance;
                });
                value.Dispose();
            });
            True(old.Disposed);
            True(!ReferenceEquals(old, Service.Instance));
        });
        Check("a stale release cannot clear a newer service", () =>
        {
            var old = Service.Instance;
            old.Dispose();
            var next = Service.Instance;
            Service.Release(old);
            True(ReferenceEquals(next, Service.Instance));
        });
        Check("stop prevents resurrection until an explicit reset", () =>
        {
            Service.Reset(stopping: true);
            Throws<InvalidOperationException>(() =>
            {
                _ = Service.Instance;
            });
            Throws<InvalidOperationException>(() => Service.Configure(() => Service.New("stopped")));
            Service.Reset();
            True(Service.Instance != null);
        });
        Check("failed, null and reentrant factories leave creation retryable", () =>
        {
            Service.Reset();
            Throws<InvalidOperationException>(() => Service.Configure(() => throw new InvalidOperationException("factory")));
            Throws<InvalidOperationException>(() => Service.Configure(() => null));
            Throws<InvalidOperationException>(() => Service.Configure(() => Service.Instance));
            True(!Service.IsCreated);
            True(Service.Instance != null);
        });
        Check("cleanup exceptions release the reset gate", () =>
        {
            _ = Service.Instance;
            Throws<InvalidOperationException>(() => Service.ResetWith(_ => throw new InvalidOperationException("cleanup")));
            True(Service.Instance != null);
        });
        Check("NetworkManager uses the Framework singleton", NetworkLifecycle);
        Check("Release never creates a service and preserves the quit guard", () =>
        {
            Service.Reset();
            int created = Service.Created;
            Singleton<Service>.Release();
            Equal(created, Service.Created);
            var value = Service.Instance;
            Singleton<Service>.Release();
            True(value.Disposed && !Service.IsCreated);
            Service.Reset(stopping: true);
            Singleton<Service>.Release();
            Throws<InvalidOperationException>(() =>
            {
                _ = Service.Instance;
            });
            Service.Reset();
        });
        Check("MonoSingleton OnExit runs once across release and deferred native destruction", () =>
        {
            Object.ResetTestState();
            ExitHook.Exits = 0;
            ExitHook.Release();
            Equal(0, Object.FindObjectsOfType<ExitHook>().Length);
            var value = ExitHook.Instance;
            Object.DeferGameObjectDestructionForTest = true;
            ExitHook.Release();
            Equal(1, ExitHook.Exits);
            True(!ExitHook.TryGetInstance(out _));
            value.Dispose();
            Object.FlushDestroyQueueForTest();
            Equal(1, ExitHook.Exits);
            Object.DeferGameObjectDestructionForTest = false;
        });
        Check("MonoSingleton starts once per run even when Instance is accessed repeatedly", () =>
        {
            Object.ResetTestState();
            ExitHook.Starts = 0;
            var value = ExitHook.Instance;
            for (int i = 0; i < 10; i++)
            {
                True(ReferenceEquals(value, ExitHook.Instance));
            }

            Equal(1, ExitHook.Starts);
            value.Dispose();
        });
        Check("duplicate components cannot run the active singleton's exit hook", () =>
        {
            Object.ResetTestState();
            ExitHook.Exits = 0;
            var active = ExitHook.Instance;
            new GameObject("duplicate").AddComponent<ExitHook>();
            Equal(0, ExitHook.Exits);
            True(ReferenceEquals(active, ExitHook.Instance));
            active.Dispose();
            Equal(1, ExitHook.Exits);
        });
        Check("quit and subsequent destruction share the same OnExit invocation", () =>
        {
            Object.ResetTestState();
            ExitHook.Exits = 0;
            var value = ExitHook.Instance;
            typeof(ExitHook).GetMethod("OnApplicationQuit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, null);
            Equal(1, ExitHook.Exits);
            True(ExitHook.Instance == null);
            ExitHook.Release();
            Equal(1, ExitHook.Exits);
        });
        Check("failed component startup cleans only its own lifetime inside the singleton", () =>
        {
            Object.ResetTestState();
            FailingExitHook.Exits = 0;
            Throws<Exception>(() =>
            {
                _ = FailingExitHook.Instance;
            });
            Equal(1, FailingExitHook.Exits);
            True(!FailingExitHook.TryGetInstance(out _));
        });
        Check("ECS releases its initialized state through OnExit exactly once", () =>
        {
            Object.ResetTestState();
            ECS.SingletonRuntimeState.IsQuitting = false;
            EcsExitHook.Exits = 0;
            var value = EcsExitHook.Instance;
            EcsExitHook.Release();
            value.Dispose();
            Equal(1, EcsExitHook.Exits);
            True(!EcsExitHook.TryGetInstance(out _));
        });
        Check("network stop and restart reuse the generic lifecycle", () =>
        {
            ResetNetwork(true);
            Throws<InvalidOperationException>(() =>
            {
                _ = NetworkManager.Instance;
            });
            ResetNetwork();
            True(NetworkManager.Instance != null);
            ResetNetwork();
        });
        Check("GPU player has no individual Unity Update callback", () =>
        {
            True(typeof(GpuAnimator).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic) == null);
        });
        Check("GPU auto registration and single manager tick", GpuSingleTick);
        Check("GPU disable, enable, clear and destruction remove registrations", GpuLifecycle);
        Check("GPU manual mode stays outside the automatic scheduler", GpuManual);
        Check("GPU notifications include direct calls and completion callbacks", GpuNotifications);
        Check("GPU callback mutations defer new registrations until next tick", GpuMutations);
        Check("GPU pause, speed and invalid time inputs", GpuPause);
        Check("GPU completion callback can replay without a duplicate tick", GpuReplay);
        Check("GPU manager recreation discovers live automatic players", () =>
        {
            var manager = Manager();
            var player = Player();
            player.TryPlay("A");
            GpuAnimatorManager.DestroyInstance();
            var next = GpuAnimatorManager.Instance;
            next.AutomaticUpdate = false;
            True(!ReferenceEquals(manager, next));
            Equal(1, next.Count);
            next.Tick(.1f);
            Equal(1, player.CurrentFrame);
        });
        Check("GPU clear rejects reentrant replay from stop notifications", () =>
        {
            var manager = Manager();
            var player = Player();
            bool replayed = true;
            player.AnimationStopped += (p, clip) => replayed = p.TryPlay("A");
            player.TryPlay("A");
            player.Clear();
            True(!replayed && !player.IsInitialized && !player.IsPlaying);
            Equal(0, manager.Count);
        });
        Check("GPU notification failures do not prevent completion callbacks", () =>
        {
            var manager = Manager();
            var player = Player();
            int completed = 0, callback = 0;
            player.AnimationCompleted += (p, clip) => throw new InvalidOperationException("listener");
            manager.AnimationCompleted += (p, clip) => completed++;
            player.TryPlay("B", (p, state) => callback++);
            manager.Tick(1);
            Equal(1, completed);
            Equal(1, callback);
            Equal(0, manager.Count);
        });
        Check("GPU manual players still publish global notifications", () =>
        {
            var manager = Manager();
            var player = Player(automatic: false);
            int changed = 0;
            manager.AnimationChanged += (p, old, next) => changed++;
            player.TryPlay("A");
            Equal(1, changed);
            Equal(0, manager.Count);
        });
        Check("GPU reentrant animation switches preserve notification order", () =>
        {
            var manager = Manager();
            var player = Player();
            var calls = new List<string>();
            manager.AnimationChanged += (p, old, next) => calls.Add("changed:" + next);
            manager.AnimationStarted += (p, clip) => calls.Add("started:" + clip);
            player.AnimationChanged += (p, old, next) =>
            {
                if (next == "A")
                {
                    p.TryPlay("B");
                }
            };
            player.TryPlay("A");
            Equal("changed:A,started:A,changed:B,started:B", string.Join(",", calls));
            Equal("B", player.CurrentClipName);
        });
        Check("GPU steady scheduler updates allocate no managed memory", () =>
        {
            var manager = Manager();
            var player = Player();
            player.TryPlay("A");
            for (int i = 0; i < 128; i++)
            {
                manager.Tick(.01f);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
            {
                manager.Tick(.01f);
            }

            Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        });
        Console.WriteLine("PASS " + passed + " singleton, actual network and GPU scheduling scenarios; Unity APIs are boundary models.");
    }

    private static void Check(string name, Action action)
    {
        try
        {
            Object.ResetSingletonsForPlay();
            action();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL " + name + ": " + exception);
            Environment.Exit(1);
        }
    }

    private static void True(bool value)
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
            throw new Exception("Expected " + expected + ", got " + actual);
        }
    }

    private static void Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new Exception("Expected " + typeof(T).Name);
    }

    public sealed partial class RetainedHook : MonoSingleton<RetainedHook>
    {
        public int Starts;
        public int Exits;
        protected override void OnStart() => Starts++;
        protected override void OnExit() => Exits++;
    }

    public sealed partial class Resettable : Singleton<Resettable>, IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }

    public sealed partial class NeverCreated : Singleton<NeverCreated>
    {
        public static int Created;
        public NeverCreated()
        {
            Created++;
        }
    }

    public sealed partial class ThrowingReset : Singleton<ThrowingReset>, IDisposable
    {
        public bool Attempted;
        public void Dispose()
        {
            Attempted = true;
            Throws<InvalidOperationException>(() =>
            {
                _ = NeverCreated.Instance;
            });
            throw new InvalidOperationException("Expected cleanup failure");
        }
    }

    public sealed partial class Simple : Singleton<Simple>
    {
    }

    public sealed partial class ExitHook : MonoSingleton<ExitHook>
    {
        public static int Exits;
        public static int Starts;
        protected override void OnStart() => Starts++;
        protected override void OnExit() => Exits++;
    }

    public sealed partial class FailingExitHook : MonoSingleton<FailingExitHook>
    {
        public static int Exits;
        protected override void OnStart() => throw new InvalidOperationException("startup failed");
        protected override void OnExit() => Exits++;
    }

    public sealed partial class EcsExitHook : ECS.SingletonBehaviour<EcsExitHook>
    {
        public static int Exits;
        protected override void OnDelete() => Exits++;
    }

    private sealed partial class Service : Singleton<Service>, IDisposable
    {
        public static int Created;
        public readonly string Name;
        public bool Disposed;
        private Service(string name)
        {
            Name = name;
            Interlocked.Increment(ref Created);
        }

        public Service() : this("default")
        {
        }

        public static Service New(string name) => new Service(name);
        public static void Configure(Func<Service> factory) => ConfigureInstance(factory);
        public static void Reset(bool stopping = false) => ResetInstance(value => value.Dispose(), stopping);
        public static void ResetWith(Action<Service> release) => ResetInstance(release);
        public static void Release(Service value) => ReleaseInstance(value);
        public void Dispose()
        {
            Disposed = true;
            ReleaseInstance(this);
        }
    }

    private static void ResetNetwork(bool stopping = false)
    {
        typeof(NetworkManager).GetMethod("ResetSingleton", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { stopping });
    }

    private static void NetworkLifecycle()
    {
        ResetNetwork();
        True(typeof(Singleton<NetworkManager>).IsAssignableFrom(typeof(NetworkManager)));
        True(!NetworkManager.TryGetInstance(out _));
        var transport = new Transport();
        NetworkManager.Configure(transport, new NetworkClientOptions { AutomaticDispatch = false });
        var first = NetworkManager.Instance;
        True(first.IsInitialized && NetworkManager.IsCreated);
        Throws<InvalidOperationException>(() => NetworkManager.Configure(new Transport()));
        first.Dispose();
        True(transport.Disposed && !NetworkManager.IsCreated);
        var second = NetworkManager.Instance;
        first.Dispose();
        True(ReferenceEquals(second, NetworkManager.Instance));
        ResetNetwork();
    }

    private sealed class Transport : INetworkTransport
    {
        public bool Disposed;
        public bool IsConnected => false;
        public long ConnectionId => 0;
        public Task ConnectionCompletion => Task.CompletedTask;
        public int PendingSendCount => 0;
        public int PendingSendBytes => 0;

        public event Action<long, NetworkPacket> PacketReceived;
        public event Action<long, Exception> Closed;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public void Abort(Exception error = null)
        {
        }

        public Task SendAsync(int id, long requestId, IMessage message, CancellationToken ct = default, long expectedConnectionId = 0) => Task.CompletedTask;
        public void Dispose()
        {
            Disposed = true;
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }

    private static GpuAnimatorManager Manager()
    {
        Object.ResetTestState();
        var manager = GpuAnimatorManager.Instance;
        manager.AutomaticUpdate = false;
        return manager;
    }

    private static GpuAnimator Player(bool automatic = true)
    {
        var go = new GameObject("actor");
        var player = go.AddComponent<GpuAnimator>();
        player.AutomaticUpdate = automatic;
        True(player.Initialize(go.AddComponent<Renderer>(), new AnimationData { totalFrame = 6, clips = new[] { new AnimationClipData("A", 0, 2, true, 10), new AnimationClipData("B", 3, 5, false, 10) } }));
        return player;
    }

    private static void GpuSingleTick()
    {
        var manager = Manager();
        var player = Player();
        True(player.TryPlay("A"));
        Equal(1, manager.Count);
        True(!manager.Register(player));
        manager.Tick(.1f);
        Equal(1, player.CurrentFrame);
        manager.Tick(.1f);
        Equal(2, player.CurrentFrame);
    }

    private static void GpuLifecycle()
    {
        var manager = Manager();
        var player = Player();
        player.TryPlay("A");
        player.gameObject.SetActive(false);
        Equal(0, manager.Count);
        player.gameObject.SetActive(true);
        Equal(1, manager.Count);
        player.Clear();
        Equal(0, manager.Count);
        var next = Player();
        next.TryPlay("A");
        Object.Destroy(next.gameObject);
        Equal(0, manager.Count);
    }

    private static void GpuManual()
    {
        var manager = Manager();
        var player = Player(automatic: false);
        player.TryPlay("A");
        Equal(0, manager.Count);
        manager.Tick(.1f);
        Equal(0, player.CurrentFrame);
        player.Tick(.1f);
        Equal(1, player.CurrentFrame);
        player.AutomaticUpdate = true;
        Equal(1, manager.Count);
        player.AutomaticUpdate = false;
        Equal(0, manager.Count);
    }

    private static void GpuNotifications()
    {
        var manager = Manager();
        var player = Player();
        int changed = 0, started = 0, completed = 0, stopped = 0, callback = 0;
        manager.AnimationChanged += (p, old, next) => changed++;
        manager.AnimationStarted += (p, clip) => started++;
        manager.AnimationCompleted += (p, clip) => completed++;
        manager.AnimationStopped += (p, clip) => stopped++;
        player.TryPlay("A");
        player.TryPlay("A");
        player.TryPlay("B", (p, state) => callback++);
        manager.Tick(1);
        manager.Tick(1);
        Equal(2, changed);
        Equal(3, started);
        Equal(1, completed);
        Equal(1, callback);
        Equal(0, manager.Count);
        player.TryPlay("A");
        player.Clear();
        Equal(1, stopped);
        Equal(0, manager.Count);
    }

    private static void GpuMutations()
    {
        var manager = Manager();
        var first = Player();
        var second = Player();
        GpuAnimator added = null;
        first.TryPlay("B", (p, state) =>
        {
            manager.Unregister(second);
            manager.Register(second);
            added = Player();
            added.TryPlay("A");
        });
        second.TryPlay("A");
        manager.Tick(1);
        Equal(0, second.CurrentFrame);
        Equal(0, added.CurrentFrame);
        manager.Tick(.1f);
        Equal(1, second.CurrentFrame);
        Equal(1, added.CurrentFrame);
        Equal(2, manager.Count);
    }

    private static void GpuPause()
    {
        var manager = Manager();
        var player = Player();
        player.TryPlay("A");
        manager.IsPaused = true;
        manager.Tick(.1f);
        Equal(0, player.CurrentFrame);
        manager.IsPaused = false;
        manager.PlaybackSpeed = .5f;
        manager.Tick(.2f);
        Equal(1, player.CurrentFrame);
        manager.Tick(float.NaN);
        manager.Tick(-1);
        Equal(1, player.CurrentFrame);
        Throws<ArgumentOutOfRangeException>(() => manager.PlaybackSpeed = float.PositiveInfinity);
    }

    private static void GpuReplay()
    {
        var manager = Manager();
        var player = Player();
        int complete = 0;
        player.TryPlay("B", (p, state) =>
        {
            complete++;
            p.TryPlay("A");
            manager.Tick(1);
        });
        manager.Tick(1);
        Equal(1, complete);
        Equal("A", player.CurrentClipName);
        Equal(0, player.CurrentFrame);
        Equal(1, manager.Count);
        manager.Tick(.1f);
        Equal(1, player.CurrentFrame);
    }
}

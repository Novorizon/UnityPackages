using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Event;
using Game.Framework.Network;
using Game.Framework.Network.Dispatching;
using Game.Framework.Pooling;
using Game.Framework.Timing;
using Google.Protobuf;

internal static class Program
{
    private enum Topic : long
    {
        Changed = long.MaxValue
    }
    private sealed class ReferenceMessage : Game.Framework.Event.IMessage
    {
        internal int Value = 1;
    }
    private readonly struct ValueMessage : Game.Framework.Event.IMessage
    {
        internal readonly int Value;
        internal ValueMessage(int value)
        {
            Value = value;
        }
    }
    private sealed class Pooled : IReferencePoolable
    {
        internal int Value;
        public void Reset()
        {
            Value = 0;
        }
    }
    private sealed class Measurement
    {
        public string name
        {
            get; set;
        }
        public int iterationsPerSample
        {
            get; set;
        }
        public int sampleCount
        {
            get; set;
        }
        public double medianNanosecondsPerOperation
        {
            get; set;
        }
        public double minimumNanosecondsPerOperation
        {
            get; set;
        }
        public double maximumNanosecondsPerOperation
        {
            get; set;
        }
        public double allocatedBytesPerOperation
        {
            get; set;
        }
    }
    private static long consumed;
    private static int passed;

    private static void Main(string[] args)
    {
        DispatcherFailureCompletesConnectionWaiters().GetAwaiter().GetResult();
        ConnectionWaitersDoNotReuseRetiredReadiness().GetAwaiter().GetResult();
        UnityThread.InitializeManual();
        try
        {
            VerifyTimerFuzz();
            VerifyEventSnapshotAndUnsubscription();
            VerifyTypedMessagesPreserveTheirContracts();
            VerifyPostedTypedSnapshots();
            VerifyReferencePoolOwnership();
            VerifyDispatchHostReplacement();
            Console.WriteLine("PASS " + passed + " foundation audit scenarios; actual Core/Network sources, no Unity execution.");
            if (args.Contains("--benchmark"))
            {
                Benchmarks();
            }
        }
        finally
        {
            UnityThread.Reset();
        }
    }

    private static async Task DispatcherFailureCompletesConnectionWaiters()
    {
        var transport = new ControlledTransport();
        var dispatcher = new ThrowingDispatcher();
        var client = new NetworkManager(
            transport,
            new NetworkClientOptions { AutoReconnect = false, HeartbeatIntervalMilliseconds = 0 },
            dispatcher);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Task connecting = client.ConnectAsync();
                if (await Task.WhenAny(connecting, Task.Delay(1000)) != connecting)
                {
                    throw new Exception("ConnectAsync remained pending after its dispatcher terminated the connection loop.");
                }
                try
                {
                    await connecting;
                    throw new Exception("A failed dispatcher must not produce a successful connection.");
                }
                catch (InvalidOperationException error) when (ReferenceEquals(error, dispatcher.Failure))
                {
                }
                Equal(NetworkState.Disconnected, client.State);
                Assert(!transport.IsConnected, "The failed connection loop must release the transport.");
            }
        }
        finally
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (AggregateException)
            {
                // Disposal reports the already-failed run, while still releasing the transport.
            }
        }
        Assert(transport.Disposed, "Disposal must still reach the owned transport.");
        passed++;
    }

    private static void VerifyTimerFuzz()
    {
        var random = new Random(83921);
        var timers = new TimerManager();
        timers.CallbackBudget = TimerManager.MaximumTimers;
        var pending = new Dictionary<int, double>();
        var handles = new Dictionary<int, TimerHandle>();
        var observed = new List<int>();
        double now = 0;
        for (int id = 0; id < 4000; id++)
        {
            int current = id;
            double delay = random.Next(1, 100) / 100.0;
            pending.Add(id, now + delay);
            handles.Add(id, timers.Schedule(delay, () => observed.Add(current)));
            if (id % 5 == 0)
            {
                Assert(handles[id].Cancel(), "An active timer must cancel once.");
                Assert(!handles[id].Cancel(), "Cancellation must be idempotent.");
                pending.Remove(id);
            }
            if (id % 9 != 0)
            {
                continue;
            }
            double advance = random.Next(1, 5) / 100.0;
            now += advance;
            int[] expected = pending.Where(pair => pair.Value <= now).OrderBy(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => pair.Key).ToArray();
            observed.Clear();
            timers.Update(advance, advance);
            Assert(expected.SequenceEqual(observed), "Timer heap order differs from the deterministic oracle.");
            foreach (int fired in expected)
            {
                pending.Remove(fired);
                Assert(!handles[fired].IsActive, "A fired one-shot handle must expire.");
            }
            Equal(pending.Count, timers.PendingCount);
        }
        observed.Clear();
        int[] rest = pending.OrderBy(pair => pair.Value).ThenBy(pair => pair.Key).Select(pair => pair.Key).ToArray();
        timers.Update(2, 2);
        Assert(rest.SequenceEqual(observed), "Final timer drain differs from the oracle.");
        Equal(0, timers.PendingCount);
        timers.Dispose();
        passed++;
    }

    private static async Task ConnectionWaitersDoNotReuseRetiredReadiness()
    {
        var transport = new GatedDisconnectTransport();
        using var dispatcher = new QueuedNetworkDispatcher();
        var client = new NetworkManager(
            transport,
            new NetworkClientOptions
            {
                AutoReconnect = true,
                HeartbeatIntervalMilliseconds = 0,
                ReconnectMinimumMilliseconds = 1,
                ReconnectMaximumMilliseconds = 1
            },
            dispatcher);
        try
        {
            await Deadline(client.ConnectAsync());
            Equal(1L, client.ConnectionId);
            transport.Drop();
            await Deadline(transport.DisconnectEntered.Task);
            Task reconnecting = client.ConnectAsync();
            Assert(!reconnecting.IsCompleted, "A connection waiter must not reuse the successful readiness of an already closed transport.");
            transport.AllowDisconnect.TrySetResult(true);
            await Deadline(reconnecting);
            Equal(2L, client.ConnectionId);
            Assert(client.IsConnected, "Readiness belongs to the replacement connection.");
        }
        finally
        {
            transport.AllowDisconnect.TrySetResult(true);
            await Deadline(client.DisposeAsync().AsTask());
        }
        Assert(transport.Disposed, "Reconnect cleanup must release its transport.");
        passed++;
    }

    private static async Task Deadline(Task task)
    {
        if (await Task.WhenAny(task, Task.Delay(3000)) != task)
        {
            throw new Exception("The controlled network lifecycle operation timed out.");
        }
        await task;
    }

    private static void VerifyEventSnapshotAndUnsubscription()
    {
        var events = new EventManager();
        int first = 0;
        int second = 0;
        ISubscription retired = null;
        using (events.Subscribe(Topic.Changed, () =>
        {
            first++;
            retired.Dispose();
        }))
        {
            retired = events.Subscribe(Topic.Changed, () => second++);
            events.Notify(Topic.Changed);
            events.Notify(Topic.Changed);
            Equal(2, first);
            Equal(0, second);
        }
        events.Clear();
        passed++;
    }

    private static void VerifyReferencePoolOwnership()
    {
        var pool = new ReferencePool<Pooled>(8);
        pool.Prewarm(8);
        var rental = pool.Rent();
        rental.Value = 42;
        pool.Clear();
        pool.Return(rental);
        Equal(0, rental.Value);
        Equal(0, pool.CountInactive);
        Throws<InvalidOperationException>(() => pool.Return(rental));
        Throws<InvalidOperationException>(() => pool.Return(new Pooled()));
        pool.Prewarm(8);
        Equal(8, pool.CountInactive);
        pool.Trim(2);
        Equal(2, pool.CountInactive);
        passed++;
    }

    private static void VerifyTypedMessagesPreserveTheirContracts()
    {
        var events = new EventManager();
        Enum erasedTopic = Topic.Changed;
        int values = 0;
        int references = 0;
        Action<ValueMessage> addValue = message => values += message.Value;
        using (events.Subscribe<ValueMessage>(erasedTopic, addValue))
        using (events.Subscribe<Topic, ValueMessage>(Topic.Changed, message => message.Value > 1, message => values += message.Value * 10))
        using (events.Subscribe<ReferenceMessage>(erasedTopic, message => references += message.Value))
        {
            events.Notify(Topic.Changed, new ValueMessage(1));
            Equal(1, values);
            events.Notify<ValueMessage>(erasedTopic, new ValueMessage(2));
            Equal(23, values);
            events.Notify<Topic, ReferenceMessage>(Topic.Changed, null);
            Equal(0, references);
            events.Notify(Topic.Changed, new ReferenceMessage());
            Equal(1, references);
            events.Unsubscribe(Topic.Changed, addValue);
            events.Notify(Topic.Changed, new ValueMessage(3));
            Equal(53, values);
            events.Clear();
            events.Notify(Topic.Changed, new ValueMessage(4));
            Equal(53, values);
        }

        using (events.Subscribe<Topic, ValueMessage>(Topic.Changed, addValue))
        {
            for (int i = 0; i < 1000; i++)
            {
                events.Notify(Topic.Changed, new ValueMessage(1));
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++)
            {
                events.Notify(Topic.Changed, new ValueMessage(1));
            }
            Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        passed++;
    }

    private static void VerifyPostedTypedSnapshots()
    {
        var events = new EventManager();
        int first = 0;
        int second = 0;
        var old = events.Subscribe<Topic, ValueMessage>(Topic.Changed, message => first += message.Value);
        Task.Run(() => events.Notify(Topic.Changed, new ValueMessage(7))).GetAwaiter().GetResult();
        old.Dispose();
        using (events.Subscribe<Topic, ValueMessage>(Topic.Changed, message => second += message.Value))
        {
            UnityThread.Update();
            Equal(0, first);
            Equal(0, second);
            Task.Run(() => events.Notify(Topic.Changed, new ValueMessage(3))).GetAwaiter().GetResult();
            UnityThread.Update();
            Equal(3, second);
        }
        passed++;
    }

    private static void VerifyDispatchHostReplacement()
    {
        int oldCalls = 0;
        int newCalls = 0;
        UnityThread.Post(() =>
        {
            oldCalls++;
            UnityThread.Reset();
            UnityThread.InitializeManual();
            UnityThread.Post(() => newCalls++);
        });
        UnityThread.Post(() => oldCalls++);
        Equal(1, UnityThread.Update());
        Equal(1, oldCalls);
        Equal(0, newCalls);
        Equal(1, UnityThread.Update());
        Equal(1, newCalls);
        passed++;
    }

    private static void Benchmarks()
    {
        var results = new List<Measurement>();
        var events = new EventManager();
        var reference = new ReferenceMessage();
        using (events.Subscribe(Topic.Changed, () => consumed++))
        {
            results.Add(Measure("EventManager.Notify/no-payload", 100000, () => events.Notify(Topic.Changed), true));
        }
        using (events.Subscribe<Topic, ReferenceMessage>(Topic.Changed, message => consumed += message.Value))
        {
            results.Add(Measure("EventManager.Notify/reference-message", 100000, () => events.Notify(Topic.Changed, reference), true));
        }
        using (events.Subscribe<Topic, ValueMessage>(Topic.Changed, message => consumed += message.Value))
        {
            results.Add(Measure("EventManager.Notify/value-message", 100000, () => events.Notify(Topic.Changed, new ValueMessage(1)), true));
        }
        var bus = new MessageBus();
        bus.Subscribe<ValueMessage>(message => consumed += message.Value);
        results.Add(Measure("MessageBus.Publish/value-message", 100000, () => bus.Publish(new ValueMessage(1)), true));
        var references = new ReferencePool<Pooled>(8);
        references.Prewarm(8);
        results.Add(Measure("ReferencePool.Rent+Return/prewarmed", 100000, () => references.Return(references.Rent()), true));
        Action callback = () => consumed++;
        for (int i = 0; i < 64; i++)
        {
            UnityThread.Post(callback);
        }
        UnityThread.Update();
        results.Add(Measure("UnityThread.Post+Update/64-callback-batch", 10000, () =>
        {
            for (int i = 0; i < 64; i++)
            {
                UnityThread.Post(callback);
            }
            Equal(64, UnityThread.Update());
        }, true));
        using (var queue = new QueuedNetworkDispatcher())
        {
            queue.TryPost(callback, 0);
            queue.Pump();
            results.Add(Measure("QueuedNetworkDispatcher.Post+Pump", 100000, () =>
            {
                Assert(queue.TryPost(callback, 0), "The empty dispatcher must accept one callback.");
                Equal(1, queue.Pump());
            }, true));
        }
        var timers = new TimerManager();
        timers.Schedule(0, callback);
        timers.Update(0, 0);
        results.Add(Measure("TimerManager.Schedule+Cancel/cached-entry", 100000, () => timers.Schedule(10, callback).Cancel(), true));
        results.Add(Measure("TimerManager.Update/empty", 100000, () => timers.Update(0, 0), true));
        TimerHandle last = default;
        for (int i = 0; i < TimerManager.MaximumTimers; i++)
        {
            last = timers.Schedule(10000, callback);
        }
        results.Add(Measure("TimerManager.Update/65536-future-timers", 100000, () => timers.Update(0, 0), true));
        Assert(last.Cancel(), "One capacity slot must be available for the heap exercise.");
        results.Add(Measure("TimerManager.Schedule+Cancel/65535-future-timers", 100000, () => timers.Schedule(1, callback).Cancel(), true));
        timers.Dispose();
        Console.WriteLine("FOUNDATION_PERFORMANCE_JSON " + JsonSerializer.Serialize(new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            operatingSystem = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
            scope = "Actual pure Core/Network code under .NET; excludes Unity engine, graphics, I/O and device performance.",
            measurements = results,
            consumer = consumed
        }));
    }

    private static Measurement Measure(string name, int iterations, Action operation, bool requiresZeroAllocation)
    {
        for (int i = 0; i < 10000; i++)
        {
            operation();
        }
        const int samples = 9;
        var nanos = new double[samples];
        var allocations = new long[samples];
        for (int sample = 0; sample < samples; sample++)
        {
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                operation();
            }
            long ended = Stopwatch.GetTimestamp();
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            nanos[sample] = (ended - started) * (1000000000.0 / Stopwatch.Frequency) / iterations;
        }
        long bytes = allocations.Sum();
        if (requiresZeroAllocation)
        {
            Equal(0L, bytes);
        }
        Array.Sort(nanos);
        return new Measurement
        {
            name = name,
            iterationsPerSample = iterations,
            sampleCount = samples,
            medianNanosecondsPerOperation = nanos[samples / 2],
            minimumNanosecondsPerOperation = nanos[0],
            maximumNanosecondsPerOperation = nanos[samples - 1],
            allocatedBytesPerOperation = bytes / (double)(iterations * samples)
        };
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception("Expected " + expected + ", actual " + actual);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void Throws<T>(Action action) where T : Exception
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

    private sealed class ThrowingDispatcher : INetworkDispatcher
    {
        internal readonly InvalidOperationException Failure = new InvalidOperationException("Main-thread host queue is full.");
        public bool TryPost(Action callback, int bytes) => throw Failure;
    }

    private sealed class ControlledTransport : INetworkTransport
    {
        private readonly TaskCompletionSource<bool> closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed;
        public bool IsConnected
        {
            get; private set;
        }
        public long ConnectionId => 1;
        public Task ConnectionCompletion => closed.Task;
        public int PendingSendCount => 0;
        public int PendingSendBytes => 0;
#pragma warning disable CS0067
        public event Action<long, NetworkPacket> PacketReceived;
        public event Action<long, Exception> Closed;
#pragma warning restore CS0067
        public Task ConnectAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            IsConnected = true;
            return Task.CompletedTask;
        }
        public void Abort(Exception error = null)
        {
            IsConnected = false;
            closed.TrySetResult(true);
        }
        public Task DisconnectAsync()
        {
            Abort();
            return Task.CompletedTask;
        }
        public Task SendAsync(int id, long requestId, Google.Protobuf.IMessage message, CancellationToken token = default, long expectedConnectionId = 0) => Task.CompletedTask;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }

    private sealed class GatedDisconnectTransport : INetworkTransport
    {
        private TaskCompletionSource<bool> completion;
        private long connectionId;
        private int connected;
        internal readonly TaskCompletionSource<bool> DisconnectEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<bool> AllowDisconnect = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed;
        public bool IsConnected => Volatile.Read(ref connected) != 0;
        public long ConnectionId => Interlocked.Read(ref connectionId);
        public Task ConnectionCompletion => completion.Task;
        public int PendingSendCount => 0;
        public int PendingSendBytes => 0;
#pragma warning disable CS0067
        public event Action<long, NetworkPacket> PacketReceived;
#pragma warning restore CS0067
        public event Action<long, Exception> Closed;

        public Task ConnectAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Increment(ref connectionId);
            Volatile.Write(ref connected, 1);
            return Task.CompletedTask;
        }

        internal void Drop()
        {
            Abort(new InvalidOperationException("The controlled peer closed."));
        }

        public void Abort(Exception error = null)
        {
            if (Interlocked.Exchange(ref connected, 0) != 0)
            {
                Closed?.Invoke(ConnectionId, error);
                completion.TrySetResult(true);
            }
        }

        public async Task DisconnectAsync()
        {
            Abort();
            if (ConnectionId == 1)
            {
                DisconnectEntered.TrySetResult(true);
                await AllowDisconnect.Task;
            }
        }

        public Task SendAsync(int id, long requestId, Google.Protobuf.IMessage message, CancellationToken token = default, long expectedConnectionId = 0) => Task.CompletedTask;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}

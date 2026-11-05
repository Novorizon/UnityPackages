using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework
{
    // Shared bounded main-thread scheduler. Unity installs its context automatically.
    public static class UnityThread
    {
        private sealed class Host
        {
            internal SynchronizationContext Context;
            internal int ThreadId;
            internal bool Scheduled;
        }
        private sealed class Work
        {
            internal Action Action;
            internal Action<Exception> Rejected;
            internal void Reject(Exception error)
            { Interlocked.Exchange(ref Action, null); Interlocked.Exchange(ref Rejected, null)?.Invoke(error); }
        }
        // Plain posts store their delegate directly; only cancellable/awaitable work needs an object.
        private readonly struct WorkItem
        {
            private readonly Action callback;
            private readonly Work waiting;
            internal WorkItem(Action callback) { this.callback = callback; waiting = null; }
            internal WorkItem(Work waiting) { callback = null; this.waiting = waiting; }
            internal Action Take() => waiting == null ? callback : Interlocked.Exchange(ref waiting.Action, null);
            internal void Reject(Exception error) { waiting?.Reject(error); }
            internal void Fail(Exception error) { if (waiting != null) waiting.Reject(error); else Logger.Exception(error); }
        }
        private static readonly object gate = new object();
        private static readonly Queue<WorkItem> queue = new Queue<WorkItem>();
        private static readonly SendOrPostCallback drain = Drain;
        private static Host host;
        public const int MaximumPendingActions = 4096;
        public const int MaximumCallbacksPerBatch = 64;
        public static bool IsInitialized => Volatile.Read(ref host) != null;
        public static bool IsMainThread => Volatile.Read(ref host)?.ThreadId == Thread.CurrentThread.ManagedThreadId;
        public static int PendingCount { get { lock (gate) return queue.Count; } }

        public static void Initialize()
        {
            var context = SynchronizationContext.Current;
            if (context == null) throw new InvalidOperationException("Initialize UnityThread on the Unity main thread, or use InitializeManual for a non-Unity host.");
            lock (gate)
            {
                if (host != null)
                {
                    if (host.ThreadId != Thread.CurrentThread.ManagedThreadId || !ReferenceEquals(host.Context, context))
                        throw new InvalidOperationException("The main-thread dispatcher is already initialized for another host.");
                    return;
                }
                host = new Host { Context = context, ThreadId = Thread.CurrentThread.ManagedThreadId };
            }
        }
        public static void InitializeManual()
        {
            lock (gate)
            {
                if (host != null) throw new InvalidOperationException("Reset the previous host before initializing another dispatcher.");
                host = new Host { ThreadId = Thread.CurrentThread.ManagedThreadId };
            }
        }
        public static void Reset()
        {
            WorkItem[] abandoned;
            lock (gate) { host = null; abandoned = queue.ToArray(); queue.Clear(); }
            foreach (var work in abandoned) work.Reject(new OperationCanceledException("Main-thread host stopped."));
        }
        public static void Post(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            Enqueue(new WorkItem(action));
        }
        internal static void Post(Action action, Action<Exception> rejected)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (rejected == null) throw new ArgumentNullException(nameof(rejected));
            Enqueue(new Work { Action = action, Rejected = rejected });
        }
        public static bool TryPost(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            return Enqueue(new WorkItem(action), false);
        }
        private static bool Enqueue(Work work) => Enqueue(new WorkItem(work));
        private static bool Enqueue(WorkItem work, bool throwWhenFull = true)
        {
            Host current;
            lock (gate)
            {
                current = host ?? throw new InvalidOperationException("UnityThread has not been initialized by its main-thread host.");
                if (queue.Count >= MaximumPendingActions)
                {
                    if (throwWhenFull) throw new InvalidOperationException("Main-thread dispatch queue is full.");
                    return false;
                }
                queue.Enqueue(work);
            }
            Schedule(current);
            return true;
        }
        private static void Schedule(Host current)
        {
            lock (gate)
            {
                if (!ReferenceEquals(host, current) || current.Context == null || current.Scheduled || queue.Count == 0) return;
                current.Scheduled = true;
            }
            try { current.Context.Post(drain, current); }
            catch (Exception error)
            {
                WorkItem[] abandoned = null;
                lock (gate)
                {
                    current.Scheduled = false;
                    if (ReferenceEquals(host, current))
                    { host = null; abandoned = queue.ToArray(); queue.Clear(); }
                }
                if (abandoned != null) foreach (var work in abandoned) work.Reject(error);
                throw;
            }
        }
        private static void Drain(object state)
        {
            var current = (Host)state;
            lock (gate) { if (!ReferenceEquals(host, current)) return; }
            try { Update(); }
            finally
            {
                lock (gate) current.Scheduled = false;
                Schedule(current);
            }
        }
        public static int Update(int maximumCallbacks = MaximumCallbacksPerBatch)
        {
            if (!IsMainThread) throw new InvalidOperationException("Dispatch must run on the initialized main thread.");
            if (maximumCallbacks < 1) throw new ArgumentOutOfRangeException(nameof(maximumCallbacks));
            int count;
            lock (gate) count = Math.Min(maximumCallbacks, queue.Count);
            int executed = 0;
            while (executed < count)
            {
                WorkItem work;
                lock (gate) { if (queue.Count == 0) break; work = queue.Dequeue(); }
                var action = work.Take();
                try { action?.Invoke(); }
                catch (Exception error) { work.Fail(error); }
                executed++;
            }
            return executed;
        }
        public static async Task RunAsync(Action action, CancellationToken ct = default)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            ct.ThrowIfCancellationRequested();
            if (IsMainThread) { action(); return; }
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = new Work
            {
                Action = () => { ct.ThrowIfCancellationRequested(); action(); completed.TrySetResult(true); },
                Rejected = error => { if (error is OperationCanceledException) completed.TrySetCanceled(); else completed.TrySetException(error); }
            };
            using (ct.Register(() => work.Reject(new OperationCanceledException(ct))))
            {
                try { if (!completed.Task.IsCompleted) Enqueue(work); }
                catch (Exception error) { work.Reject(error); }
                await completed.Task.ConfigureAwait(false);
            }
        }
        public static MainThreadAwaitable SwitchToMainThread(CancellationToken ct = default)
        {
            if (!IsInitialized) throw new InvalidOperationException("UnityThread has not been initialized.");
            return new MainThreadAwaitable(ct);
        }

        public readonly struct MainThreadAwaitable
        {
            private readonly CancellationToken cancellation;
            internal MainThreadAwaitable(CancellationToken ct) { cancellation = ct; }
            public Awaiter GetAwaiter() => new Awaiter(cancellation);
            public sealed class Awaiter : ICriticalNotifyCompletion
            {
                private readonly CancellationToken cancellation;
                private ExceptionDispatchInfo failure;
                internal Awaiter(CancellationToken ct) { cancellation = ct; }
                public bool IsCompleted => cancellation.IsCancellationRequested || IsMainThread;
                public void GetResult()
                {
                    cancellation.ThrowIfCancellationRequested();
                    failure?.Throw();
                    if (!IsMainThread) throw new InvalidOperationException("Main-thread continuation was not dispatched by the host.");
                }
                public void OnCompleted(Action continuation)
                {
                    Action<Exception> rejected = error =>
                    {
                        failure = ExceptionDispatchInfo.Capture(error);
                        ThreadPool.QueueUserWorkItem(_ => continuation());
                    };
                    var work = new Work { Action = continuation, Rejected = rejected };
                    try { Enqueue(work); }
                    catch (Exception error) { work.Reject(error); }
                }
                public void UnsafeOnCompleted(Action continuation) => OnCompleted(continuation);
            }
        }
    }
}

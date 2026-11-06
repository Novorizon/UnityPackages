using System;
using System.Collections.Generic;
using System.Threading;
using Game.Framework;

internal static partial class Program
{
    private static void VerifyDispatchLifecycle()
    {
        Check("manual dispatch stops when a callback replaces its host", () =>
        {
            UnityThread.Reset();
            UnityThread.InitializeManual();
            int oldCalls = 0, newCalls = 0;
            try
            {
                UnityThread.Post(() =>
                {
                    UnityThread.Reset();
                    UnityThread.InitializeManual();
                    UnityThread.Post(() => newCalls++);
                });
                UnityThread.Post(() => oldCalls++);
                Equal(1, UnityThread.Update());
                Equal(0, oldCalls);
                Equal(0, newCalls);
                Equal(1, UnityThread.PendingCount);
                Equal(1, UnityThread.Update());
                Equal(1, newCalls);
            }
            finally
            {
                UnityThread.Reset();
            }
        });

        Check("stale context drains leave replacement-host work for its own context", () =>
        {
            var previous = SynchronizationContext.Current;
            var oldContext = new DispatchContext();
            var newContext = new DispatchContext();
            UnityThread.Reset();
            try
            {
                SynchronizationContext.SetSynchronizationContext(oldContext);
                UnityThread.Initialize();
                int newCalls = 0;
                UnityThread.Post(() =>
                {
                    UnityThread.Reset();
                    SynchronizationContext.SetSynchronizationContext(newContext);
                    UnityThread.Initialize();
                    UnityThread.Post(() => newCalls++);
                });
                UnityThread.Post(() => throw new Exception("Abandoned old-host work executed"));
                oldContext.RunOne();
                Equal(0, newCalls);
                Equal(1, newContext.Pending);
                newContext.RunOne();
                Equal(1, newCalls);
                Equal(0, UnityThread.PendingCount);
            }
            finally
            {
                UnityThread.Reset();
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });

        Check("prewarmed plain dispatch preserves its allocation-free steady path", () =>
        {
            UnityThread.Reset();
            UnityThread.InitializeManual();
            Action callback = () => { };
            try
            {
                for (int i = 0; i < 128; i++)
                {
                    UnityThread.Post(callback);
                    UnityThread.Update();
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1024; i++)
                {
                    UnityThread.Post(callback);
                    UnityThread.Update();
                }

                Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            finally
            {
                UnityThread.Reset();
            }
        });
    }

    private sealed class DispatchContext : SynchronizationContext
    {
        private readonly Queue<Action> callbacks = new Queue<Action>();
        internal int Pending => callbacks.Count;

        public override void Post(SendOrPostCallback callback, object state)
        {
            callbacks.Enqueue(() => callback(state));
        }

        internal void RunOne()
        {
            callbacks.Dequeue()();
        }
    }
}

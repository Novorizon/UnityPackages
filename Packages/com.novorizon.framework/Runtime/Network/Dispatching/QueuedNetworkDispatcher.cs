using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.Framework.Network.Dispatching
{
    /// <summary>定义网络调度器的调用契约。</summary>
    public interface INetworkDispatcher
    {
        // Enqueue and return; custom dispatchers must not synchronously invoke callbacks on the I/O loop.
        /// <summary>尝试处理 Post，返回是否成功。</summary>
        bool TryPost(Action callback, int retainedPayloadBytes);
    }

    // Create on the Unity main thread and Pump in Update. No UnityEngine dependency.
    /// <summary>提供排队的网络调度器的实现。</summary>
    public sealed class QueuedNetworkDispatcher : INetworkDispatcher, IDisposable
    {
        private readonly object sync = new object();
        private readonly Queue<(Action Callback, int Bytes)> queue = new Queue<(Action, int)>();
        private readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        private readonly int maximumCount, maximumBytes;
        private int pendingBytes;
        private bool disposed;
        /// <summary>使用调用方提供的maximumCount、maximumBytes创建 QueuedNetworkDispatcher。</summary>
        public QueuedNetworkDispatcher(int maximumCount = 256, int maximumBytes = 4 * 1024 * 1024)
        {
            if (maximumCount < 1 || maximumBytes < 1) throw new ArgumentOutOfRangeException(nameof(maximumCount));
            this.maximumCount = maximumCount; this.maximumBytes = maximumBytes;
        }
        public int PendingCount { get { lock (sync) return queue.Count; } }
        public int PendingPayloadBytes { get { lock (sync) return pendingBytes; } }
        /// <summary>尝试处理 Post，返回是否成功。</summary>
        public bool TryPost(Action callback, int retainedPayloadBytes)
        {
            if (callback == null || retainedPayloadBytes < 0) throw new ArgumentException("Invalid callback.");
            lock (sync)
            {
                if (disposed || queue.Count >= maximumCount || retainedPayloadBytes > maximumBytes - pendingBytes) return false;
                pendingBytes += retainedPayloadBytes; queue.Enqueue((callback, retainedPayloadBytes)); return true;
            }
        }
        /// <summary>根据传入参数取得Pump。</summary>
        public int Pump(int maximumCallbacks = 64)
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThread) throw new InvalidOperationException("Pump must run on the dispatcher's owning thread.");
            if (maximumCallbacks < 1) throw new ArgumentOutOfRangeException(nameof(maximumCallbacks));
            int count = 0;
            while (count < maximumCallbacks)
            {
                (Action Callback, int Bytes) next;
                lock (sync)
                {
                    if (queue.Count == 0) break;
                    next = queue.Dequeue(); pendingBytes -= next.Bytes;
                }
                next.Callback(); count++;
            }
            return count;
        }
        /// <summary>结束排队的网络调度器对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose() { lock (sync) { disposed = true; queue.Clear(); pendingBytes = 0; } }
    }
}

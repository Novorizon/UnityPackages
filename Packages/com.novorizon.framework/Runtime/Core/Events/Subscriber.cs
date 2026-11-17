using System;
using System.Collections.Generic;
namespace Game.Framework.Event
{
    /// <summary>提供订阅者的实现。</summary>
    public class Subscriber : IDisposable
    {
        private readonly object gate = new object();
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private bool disposed;
        /// <summary>添加订阅者。</summary>
        public void Add(IDisposable subscription)
        {
            if (subscription == null) return;
            lock (gate) { if (!disposed) { subscriptions.Add(subscription); return; } }
            subscription.Dispose();
        }
        /// <summary>清空订阅者。</summary>
        public void Clear()
        {
            IDisposable[] snapshot;
            lock (gate) { snapshot = subscriptions.ToArray(); subscriptions.Clear(); }
            foreach (var value in snapshot) { try { value.Dispose(); } catch (Exception error) { Logger.Exception(error); } }
        }
        /// <summary>结束订阅者对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose() { lock (gate) disposed = true; Clear(); }
    }
}

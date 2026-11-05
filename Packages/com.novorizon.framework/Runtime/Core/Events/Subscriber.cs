using System;
using System.Collections.Generic;
namespace Game.Framework.Event
{
    public class Subscriber : IDisposable
    {
        private readonly object gate = new object();
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();
        private bool disposed;
        public void Add(IDisposable subscription)
        {
            if (subscription == null) return;
            lock (gate) { if (!disposed) { subscriptions.Add(subscription); return; } }
            subscription.Dispose();
        }
        public void Clear()
        {
            IDisposable[] snapshot;
            lock (gate) { snapshot = subscriptions.ToArray(); subscriptions.Clear(); }
            foreach (var value in snapshot) { try { value.Dispose(); } catch (Exception error) { Logger.Exception(error); } }
        }
        public void Dispose() { lock (gate) disposed = true; Clear(); }
    }
}

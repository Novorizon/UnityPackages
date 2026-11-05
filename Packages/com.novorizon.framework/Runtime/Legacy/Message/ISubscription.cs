using System;
using System.Threading;
namespace Game.Framework
{
    public interface ISubscription : Event.ISubscription { }
    internal sealed class Subscription : ISubscription
    {
        private IDisposable inner;
        internal Subscription(IDisposable value) { inner = value; }
        public void Dispose() { Interlocked.Exchange(ref inner, null)?.Dispose(); }
    }
}

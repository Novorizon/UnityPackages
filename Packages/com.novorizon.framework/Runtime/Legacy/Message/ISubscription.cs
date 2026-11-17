using System;
using System.Threading;
namespace Game.Framework
{
    /// <summary>定义订阅的调用契约。</summary>
    public interface ISubscription : Event.ISubscription { }
    /// <summary>提供订阅的实现。</summary>
    internal sealed class Subscription : ISubscription
    {
        private IDisposable inner;
        /// <summary>使用调用方提供的value创建 Subscription。</summary>
        internal Subscription(IDisposable value) { inner = value; }
        /// <summary>结束订阅对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose() { Interlocked.Exchange(ref inner, null)?.Dispose(); }
    }
}

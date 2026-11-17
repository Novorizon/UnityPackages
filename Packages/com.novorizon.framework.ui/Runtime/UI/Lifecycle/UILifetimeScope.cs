#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace UI
{
    /// <summary>A cancellation and subscription lifetime. Cancellation sources and lists are lazy.</summary>
    /// <remarks>界面生命周期对应的作用域。保存作用域内需要统一管理的信息。</remarks>
    public sealed class UILifetimeScope : IDisposable
    {
        readonly object gate = new();
        CancellationTokenSource? cancellation;
        List<IDisposable>? subscriptions;
        bool disposed;

        public bool IsDisposed { get { lock (gate) return disposed; } }
        public CancellationToken Token
        {
            get
            {
                lock (gate)
                {
                    if (disposed) return new CancellationToken(true);
                    return (cancellation ??= new CancellationTokenSource()).Token;
                }
            }
        }
        /// <summary>跟踪界面生命周期作用域。</summary>
        public T Track<T>(T subscription) where T : IDisposable
        {
            if (subscription is null) throw new ArgumentNullException(nameof(subscription));
            lock (gate)
            {
                if (!disposed)
                {
                    (subscriptions ??= new List<IDisposable>()).Add(subscription);
                    return subscription;
                }
            }
            DisposeOne(subscription);
            return subscription;
        }
        /// <summary>结束界面生命周期作用域对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            CancellationTokenSource? source;
            List<IDisposable>? snapshot;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                source = cancellation; cancellation = null;
                snapshot = subscriptions; subscriptions = null;
            }
            if (source != null)
            {
                try { source.Cancel(); }
                catch (Exception exception) { Debug.LogException(exception); }
                finally { source.Dispose(); }
            }
            if (snapshot == null) return;
            for (int i = snapshot.Count - 1; i >= 0; i--) DisposeOne(snapshot[i]);
        }
        /// <summary>释放一个。</summary>
        static void DisposeOne(IDisposable subscription)
        {
            try { subscription.Dispose(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}

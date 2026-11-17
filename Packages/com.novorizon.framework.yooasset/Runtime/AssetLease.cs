using System;
using Game.Framework;
using UnityEngine;

namespace Game.Resource
{
    /// <summary>一次独立资源获取。使用完毕后 Dispose，不会释放其他调用方或共享缓存的句柄。</summary>
    public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        private T asset;
        private Action release;
        public bool IsValid => release != null && asset != null;
        public T Asset => IsValid ? asset : throw new ObjectDisposedException(nameof(AssetLease<T>));

        /// <summary>使用调用方提供的asset、release创建 AssetLease。</summary>
        internal AssetLease(T asset, Action release)
        {
            this.asset = asset;
            this.release = release;
        }

        /// <summary>使失效资源租约。</summary>
        internal void Invalidate()
        {
            asset = null;
            release = null;
        }

        /// <summary>结束资源租约对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            if (release == null)
            {
                return;
            }

            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Dispose resource leases on Unity's main thread.");
            }

            var callback = release;
            Invalidate();
            callback();
        }
    }
}

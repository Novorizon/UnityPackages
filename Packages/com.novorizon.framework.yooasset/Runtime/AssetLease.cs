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

        internal AssetLease(T asset, Action release)
        {
            this.asset = asset;
            this.release = release;
        }

        internal void Invalidate()
        {
            asset = null;
            release = null;
        }

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

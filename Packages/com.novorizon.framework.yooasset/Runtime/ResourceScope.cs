using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using UnityEngine.SceneManagement;

namespace Game.Resource
{
    /// <summary>一组资源获取的生命周期，可由窗口 Track 或场景卸载自动 Dispose。</summary>
    public sealed class ResourceScope : IDisposable
    {
        private readonly ResourceManager resources;
        private readonly List<IDisposable> leases = new List<IDisposable>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken token;
        private readonly int sceneHandle;
        private bool disposed;
        public CancellationToken Token => token;

        internal ResourceScope(ResourceManager resources, Scene? scene = null)
        {
            this.resources = resources;
            token = lifetime.Token;
            if (scene.HasValue)
            {
                sceneHandle = scene.Value.handle;
                SceneManager.sceneUnloaded += OnSceneUnloaded;
            }
        }

        public async Task<AssetLease<T>> AcquireAsync<T>(string location, CancellationToken token = default)
            where T : UnityEngine.Object
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ResourceScope));
            }

            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                var lease = await resources.AcquireAsync<T>(location, cancellation.Token);
                if (disposed || cancellation.IsCancellationRequested)
                {
                    lease.Dispose();
                    cancellation.Token.ThrowIfCancellationRequested();
                    throw new ObjectDisposedException(nameof(ResourceScope));
                }

                leases.Add(lease);
                return lease;
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (scene.handle == sceneHandle)
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Dispose resource scopes on Unity's main thread.");
            }

            disposed = true;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            try
            {
                lifetime.Cancel();
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }

            for (int i = leases.Count - 1; i >= 0; i--)
            {
                try
                {
                    leases[i].Dispose();
                }
                catch (Exception error)
                {
                    Logger.Exception(error);
                }
            }

            leases.Clear();
            lifetime.Dispose();
        }
    }
}

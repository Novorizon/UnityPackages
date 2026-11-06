using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace Game.Resource
{
    /// <summary>每次加载独立持有一个 YooAsset 场景句柄，由场景管理器负责此加载器的生命周期。</summary>
    public sealed class YooAssetSceneLoader : ISceneLoader, IDisposable
    {
        private readonly ResourcePackage package;
        private readonly Dictionary<int, SceneHandle> handles = new Dictionary<int, SceneHandle>();
        private readonly HashSet<SceneHandle> pending = new HashSet<SceneHandle>();
        private bool disposed;
        public YooAssetSceneLoader(ResourcePackage package)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        public async Task<Scene> LoadAsync(string location, LoadSceneMode mode, IProgress<float> progress)
        {
            EnsureAvailable();
            var handle = package.LoadSceneAsync(location, mode);
            pending.Add(handle);
            try
            {
                while (!handle.Task.IsCompleted)
                {
                    Report(progress, handle.Progress);
                    await Task.Yield();
                }

                await handle.Task;
                EnsureAvailable();
                if (handle.Status != EOperationStatus.Succeed)
                {
                    throw new InvalidOperationException(handle.LastError);
                }

                var scene = handle.SceneObject;
                handles.Add(scene.handle, handle);
                Report(progress, 1);
                return scene;
            }
            catch
            {
                if (handle.IsValid)
                {
                    handle.Release();
                }

                throw;
            }
            finally
            {
                pending.Remove(handle);
            }
        }

        public async Task UnloadAsync(Scene scene)
        {
            EnsureAvailable();
            if (!handles.TryGetValue(scene.handle, out var handle))
            {
                throw new InvalidOperationException("Scene handle is not owned by this loader.");
            }

            var operation = handle.UnloadAsync();
            await operation.Task;
            if (operation.Status != EOperationStatus.Succeed)
            {
                throw new InvalidOperationException(operation.Error);
            }

            OnSceneUnloaded(scene);
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (handles.TryGetValue(scene.handle, out var handle))
            {
                handles.Remove(scene.handle);
                if (handle.IsValid)
                {
                    handle.Release();
                }
            }
        }

        private static void Report(IProgress<float> progress, float value)
        {
            try
            {
                progress?.Report(value);
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }
        }

        private void EnsureAvailable()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(YooAssetSceneLoader));
            }

            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("YooAsset scenes must run on Unity's main thread.");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            EnsureAvailable();
            disposed = true;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
            foreach (var handle in handles.Values)
            {
                if (handle.IsValid)
                {
                    handle.Release();
                }
            }

            handles.Clear();
        // 正在加载的句柄由 LoadAsync 在原生操作完成后释放，不能提前使 handle.Task 失效。
        }
    }
}

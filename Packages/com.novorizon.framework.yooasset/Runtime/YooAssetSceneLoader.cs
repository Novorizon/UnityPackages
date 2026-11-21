using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Scenes;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using Logger = Game.Framework.Logger;

namespace Game.Resource
{
    /// <summary>每次加载独立持有一个 YooAsset 场景句柄，由场景管理器负责此加载器的生命周期。</summary>
    public sealed class YooAssetSceneLoader : ISceneLoader, IDisposable
    {
        private readonly ResourcePackage package;
        private readonly Dictionary<int, SceneHandle> handles = new Dictionary<int, SceneHandle>();
        private readonly Dictionary<int, Task> unloading = new Dictionary<int, Task>();
        private bool disposed;

        /// <summary>创建场景加载器。包不可用或未在主线程创建时，加载和卸载会安全失败。</summary>
        public YooAssetSceneLoader(ResourcePackage package)
        {
            if (package != null && ResourceManager.CheckMainThread(nameof(YooAssetSceneLoader)))
            {
                this.package = package;
                UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
            }
        }

        /// <summary>加载失败、加载器已关闭或线程错误时返回默认场景。</summary>
        public async Task<Scene> LoadAsync(string location, LoadSceneMode mode, IProgress<float> progress)
        {
            if (!CanUsePackage(nameof(LoadAsync)) || !ResourceManager.CheckSceneRequest(location, mode))
            {
                return default;
            }

            SceneHandle handle = null;
            bool retained = false;
            try
            {
                handle = package.LoadSceneAsync(location, mode);
                var loading = handle?.Task;
                if (loading == null)
                {
                    Logger.Warn("Could not load scene: " + location);
                    return default;
                }
                while (!loading.IsCompleted && !disposed)
                {
                    Report(progress, handle.Progress);
                    await Task.Yield();
                }

                await loading;
                if (disposed)
                {
                    return default;
                }
                if (!handle.IsValid || handle.Status != EOperationStatus.Succeed)
                {
                    Logger.Warn("Could not load scene " + location + ": " + (handle.IsValid ? handle.LastError : "Invalid handle."));
                    return default;
                }

                var scene = handle.SceneObject;
                handles[scene.handle] = handle;
                retained = true;
                Report(progress, 1);
                return scene;
            }
            catch (OperationCanceledException)
            {
                return default;
            }
            catch (Exception error) when (ResourceManager.IsResourceIoFailure(error))
            {
                Logger.Warn("Could not load scene " + location + ": " + error.Message);
                return default;
            }
            finally
            {
                if (!retained)
                {
                    ReleaseIfSceneUnloaded(handle);
                }
            }
        }

        /// <summary>同一场景重复卸载时等待原操作。原生卸载失败时保留句柄，便于稍后重试。</summary>
        public Task UnloadAsync(Scene scene)
        {
            if (!CanUsePackage(nameof(UnloadAsync)) || !handles.TryGetValue(scene.handle, out var handle))
            {
                return Task.CompletedTask;
            }
            if (unloading.TryGetValue(scene.handle, out var pending))
            {
                return pending;
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            unloading[scene.handle] = completion.Task;
            CompleteUnloadAsync(scene, handle, completion).Forget();
            return completion.Task;
        }

        private async Task CompleteUnloadAsync(Scene scene, SceneHandle handle, TaskCompletionSource<bool> completion)
        {
            try
            {
                var operation = handle.UnloadAsync();
                await operation.Task;
                if (operation.Status == EOperationStatus.Succeed)
                {
                    OnSceneUnloaded(scene);
                }
                else
                {
                    Logger.Warn("Could not unload scene " + scene.name + ": " + operation.Error);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }
            finally
            {
                unloading.Remove(scene.handle);
                completion.TrySetResult(true);
            }
        }

        /// <summary>响应场景已卸载。</summary>
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

        /// <summary>释放 If 场景已卸载。</summary>
        internal static void ReleaseIfSceneUnloaded(SceneHandle handle)
        {
            if (handle == null || !handle.IsValid)
            {
                return;
            }
            if (handle.Status == EOperationStatus.Succeed && handle.SceneObject.IsValid() && handle.SceneObject.isLoaded)
            {
                // YooAsset 自己的 SceneHandles 列表持有此句柄，sceneUnloaded 时自动释放。
                // 仅解绑管理器时不能卸载仍被场景使用的 Bundle。
                return;
            }
            handle.Release();
        }

        /// <summary>上报 Yoo 资源场景加载器。</summary>
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

        private bool CanUsePackage(string operation)
        {
            if (!ResourceManager.CheckMainThread(operation))
            {
                return false;
            }
            if (disposed || package == null || package.InitializeStatus != EOperationStatus.Succeed)
            {
                Logger.Warn("YooAsset scene loader is unavailable for " + operation + ".");
                return false;
            }
            return true;
        }

        /// <summary>结束Yoo 资源场景加载器对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            if (!ResourceManager.CheckMainThread(nameof(Dispose)) || disposed)
            {
                return;
            }
            disposed = true;
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
            foreach (var handle in handles.Values)
            {
                ReleaseIfSceneUnloaded(handle);
            }

            handles.Clear();
            // 待加载场景由 LoadAsync 在原生操作结束后清理。
        }
    }
}

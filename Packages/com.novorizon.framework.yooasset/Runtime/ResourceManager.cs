using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Framework;
using Logger = Game.Framework.Logger;
using UnityEngine;
using UnityEngine.SceneManagement;
using Game.Framework.Scenes;
using UnityEngine.U2D;
using YooAsset;
using Object = UnityEngine.Object;

namespace Game.Resource
{
    // A shared cache: ReleaseHandle/ReleaseAllAssets affect every consumer of that location.
    // YooAsset itself remains unmodified. All handle operations run on Unity's main thread.
    public partial class ResourceManager : Singleton<ResourceManager>
    {
        private sealed class AssetLoad
        {
            internal readonly TaskCompletionSource<Object> Result = new TaskCompletionSource<Object>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool Invalidated;
        }

        private sealed class SceneLoad
        {
            internal SceneHandle Handle;
            internal bool Invalidated;
            internal LoadSceneMode Mode;
            internal readonly TaskCompletionSource<Scene> Result = new TaskCompletionSource<Scene>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task Unload;
        }

        private ResourcePackage package;
        private int generation;
        private bool initialized, atlasRegistered, shuttingDown;
        private Task shutdown;
        private Task<bool> initialization;
        private readonly Dictionary<string, AssetHandle> assetHandles = new Dictionary<string, AssetHandle>();
        private readonly Dictionary<string, AssetLoad> assetLoads = new Dictionary<string, AssetLoad>();
        private readonly Dictionary<string, SceneLoad> scenes = new Dictionary<string, SceneLoad>();
        private readonly Dictionary<int, string> sceneLocations = new Dictionary<int, string>();
        public bool Initialized => initialized;

        /// <summary>场景协调器使用独立场景句柄，不借用共享的按地址缓存。</summary>
        public ISceneLoader CreateSceneLoader()
        {
            Ready();
            return new YooAssetSceneLoader(package);
        }

        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("ResourceManager must be called on the initialized Unity main thread.");
            }
        }

        private void Ready()
        {
            MainThread();
            if (shuttingDown || !initialized || package == null)
            {
                throw new InvalidOperationException("Await ResourceManager.InitializeAsync first.");
            }
        }

        public Task<bool> InitializeAsync()
        {
            MainThread();
            if (shuttingDown)
            {
                return InitializeAfterShutdownAsync(shutdown);
            }

            if (initialized)
            {
                return Task.FromResult(true);
            }

            if (initialization == null || initialization.IsCompleted)
            {
                initialization = InitializeCoreAsync();
            }

            return initialization;
        }

        private async Task<bool> InitializeAfterShutdownAsync(Task previous)
        {
            await previous;
            return await InitializeAsync();
        }

        public async Task InitializeAsync(Action<bool> callback)
        {
            var result = await InitializeAsync();
            callback?.Invoke(result);
        }

        private async Task<bool> InitializeCoreAsync()
        {
            YooAssets.Initialize();
            package = YooAssets.TryGetPackage("DefaultPackage") ?? YooAssets.CreatePackage("DefaultPackage");
            YooAssets.SetDefaultPackage(package);
            int startedGeneration = generation;
            bool success = await InitPackageAsync(package, YooAssetsSettings.CreateParameters());
            if (startedGeneration != generation)
            {
                return false;
            }

            initialized = success;
            if (initialized && !atlasRegistered)
            {
                SpriteAtlasManager.atlasRequested += RequestAtlas;
                atlasRegistered = true;
            }

            return initialized;
        }

        public async Task<bool> InitPackageAsync(ResourcePackage resourcePackage, InitializeParameters parameters)
        {
            MainThread();
            if (resourcePackage.InitializeStatus != EOperationStatus.Succeed)
            {
                var operation = resourcePackage.InitializeAsync(parameters);
                await operation.Task;
                if (operation.Status != EOperationStatus.Succeed)
                {
                    Logger.Error(operation.Error);
                    return false;
                }
            }

            var version = resourcePackage.RequestPackageVersionAsync();
            await version.Task;
            if (version.Status != EOperationStatus.Succeed)
            {
                Logger.Error(version.Error);
                return false;
            }

            var manifest = resourcePackage.UpdatePackageManifestAsync(version.PackageVersion);
            await manifest.Task;
            if (manifest.Status != EOperationStatus.Succeed)
            {
                Logger.Error(manifest.Error);
                return false;
            }

            return true;
        }

        public async Task<T> LoadAssetAsync<T>(string location)
            where T : Object
        {
            Ready();
            Validate(location);
            if (assetHandles.TryGetValue(location, out var cached) && cached.IsValid && cached.AssetObject != null)
            {
                return Cast<T>(cached.AssetObject, location);
            }

            if (!assetLoads.TryGetValue(location, out var load))
            {
                load = new AssetLoad();
                assetLoads.Add(location, load);
                CompleteAssetAsync<T>(location, load).Forget();
            }

            return Cast<T>(await load.Result.Task, location);
        }

        // Cache-only lookup for frequently refreshed UI/gameplay; never starts a load or creates a Task.
        public bool TryGetLoaded<T>(string location, out T value)
            where T : Object
        {
            MainThread();
            Validate(location);
            value = null;
            if (!initialized || shuttingDown || package == null)
            {
                return false;
            }

            if (!assetHandles.TryGetValue(location, out var handle) || !handle.IsValid)
            {
                return false;
            }

            value = handle.AssetObject as T;
            return value != null;
        }

        public async Task LoadAssetAsync<T>(string location, Action<T> callback)
            where T : Object
        {
            var result = await LoadAssetAsync<T>(location);
            callback?.Invoke(result);
        }

        private async Task CompleteAssetAsync<T>(string location, AssetLoad load)
            where T : Object
        {
            AssetHandle handle = null;
            try
            {
                handle = package.LoadAssetAsync<T>(location);
                await handle.Task;
                if (load.Invalidated)
                {
                    throw new OperationCanceledException("Resource load was released before completion.");
                }

                if (handle.Status != EOperationStatus.Succeed || handle.AssetObject == null)
                {
                    throw new InvalidOperationException(handle.LastError);
                }

                Cast<T>(handle.AssetObject, location);
                assetHandles[location] = handle;
                load.Result.TrySetResult(handle.AssetObject);
                handle = null; // Ownership transferred to the shared cache.
            }
            catch (OperationCanceledException)
            {
                load.Result.TrySetCanceled();
            }
            catch (Exception error)
            {
                load.Result.TrySetException(error);
            }
            finally
            {
                if (handle != null && handle.IsValid)
                {
                    handle.Release();
                }

                if (assetLoads.TryGetValue(location, out var current) && ReferenceEquals(current, load))
                {
                    assetLoads.Remove(location);
                }
            }
        }

        public T LoadAsset<T>(string location)
            where T : Object
        {
            Ready();
            Validate(location);
            if (assetLoads.ContainsKey(location))
            {
                throw new InvalidOperationException("This location is loading asynchronously; await its shared load.");
            }

            if (assetHandles.TryGetValue(location, out var cached) && cached.IsValid && cached.AssetObject != null)
            {
                return Cast<T>(cached.AssetObject, location);
            }

            var handle = package.LoadAssetSync<T>(location);
            try
            {
                if (handle.Status != EOperationStatus.Succeed || handle.AssetObject == null)
                {
                    throw new InvalidOperationException(handle.LastError);
                }

                var result = Cast<T>(handle.AssetObject, location);
                assetHandles[location] = handle;
                return result;
            }
            catch
            {
                if (handle.IsValid)
                {
                    handle.Release();
                }

                throw;
            }
        }

        public void LoadAsset<T>(string location, Action<T> callback)
            where T : Object
        {
            var result = LoadAsset<T>(location);
            callback?.Invoke(result);
        }

        private static T Cast<T>(Object value, string location)
            where T : Object => value as T ?? throw new InvalidCastException("Resource type mismatch at " + location);
        private static void Validate(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Resource location is required.", nameof(location));
            }
        }

        public void ReleaseHandle(string location)
        {
            MainThread();
            if (assetLoads.TryGetValue(location, out var load))
            {
                load.Invalidated = true;
                load.Result.TrySetCanceled();
                assetLoads.Remove(location);
            }

            if (assetHandles.TryGetValue(location, out var handle))
            {
                assetHandles.Remove(location);
                if (handle.IsValid)
                {
                    handle.Release();
                }
            }
        }

        public void ReleaseAllAssets()
        {
            MainThread();
            foreach (var load in assetLoads.Values)
            {
                load.Invalidated = true;
                load.Result.TrySetCanceled();
            }

            assetLoads.Clear();
            foreach (var handle in assetHandles.Values)
            {
                if (handle.IsValid)
                {
                    handle.Release();
                }
            }

            assetHandles.Clear();
        }

        public void TryUnloadAsset(string location, int loopCount = 10)
        {
            Ready();
            ReleaseHandle(location);
            package.TryUnloadUnusedAsset(location, loopCount);
        }

        public async Task TryUnloadUnusedAsset(int loopCount = 10)
        {
            Ready();
            await package.UnloadUnusedAssetsAsync(loopCount).Task;
            var operation = Resources.UnloadUnusedAssets();
            if (!operation.isDone)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;
            }
        }

        private SceneLoad GetScene(string location, LoadSceneMode mode)
        {
            Ready();
            Validate(location);
            if (scenes.TryGetValue(location, out var existing))
            {
                if (existing.Handle.IsValid && (!existing.Result.Task.IsCompleted || existing.Handle.SceneObject.isLoaded))
                {
                    if (existing.Mode != mode || existing.Unload != null)
                    {
                        throw new InvalidOperationException("Scene is already loading/loaded with another mode or unloading.");
                    }

                    return existing;
                }

                scenes.Remove(location);
                var staleKeys = new List<int>();
                foreach (var pair in sceneLocations)
                {
                    if (pair.Value == location)
                    {
                        staleKeys.Add(pair.Key);
                    }
                }

                foreach (var key in staleKeys)
                {
                    sceneLocations.Remove(key);
                }

                if (existing.Handle.IsValid)
                {
                    existing.Handle.Release();
                }
            }

            var load = new SceneLoad
            {
                Mode = mode,
                Handle = package.LoadSceneAsync(location, mode)
            };
            scenes.Add(location, load);
            CompleteSceneAsync(location, load).Forget();
            return load;
        }

        private async Task CompleteSceneAsync(string location, SceneLoad load)
        {
            try
            {
                await load.Handle.Task;
                if (load.Invalidated)
                {
                    YooAssetSceneLoader.ReleaseIfSceneUnloaded(load.Handle);
                    return;
                }

                if (load.Handle.Status != EOperationStatus.Succeed)
                {
                    throw new InvalidOperationException(load.Handle.LastError);
                }

                sceneLocations[load.Handle.SceneObject.handle] = location;
                load.Result.TrySetResult(load.Handle.SceneObject);
            }
            catch (Exception error)
            {
                if (scenes.TryGetValue(location, out var current) && ReferenceEquals(current, load))
                {
                    scenes.Remove(location);
                }

                YooAssetSceneLoader.ReleaseIfSceneUnloaded(load.Handle);
                load.Result.TrySetException(error);
            }
        }

        public Task<Scene> LoadSceneAsync(string location) => GetScene(location, LoadSceneMode.Single).Result.Task;
        public Task<Scene> LoadSceneAsync(string location, LoadSceneMode mode) => GetScene(location, mode).Result.Task;
        public async Task LoadSceneAsync(string location, Action<Scene> callback)
        {
            var scene = await LoadSceneAsync(location);
            callback?.Invoke(scene);
        }

        public async Task LoadSceneAsync(string location, LoadSceneMode mode, Action<Scene> callback)
        {
            var scene = await LoadSceneAsync(location, mode);
            callback?.Invoke(scene);
        }

        public SceneHandle LoadSceneHandleAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, Action<Scene> callback = null)
        {
            var load = GetScene(location, mode);
            if (callback != null)
            {
                CompleteSceneCallbackAsync(load.Result.Task, callback).Forget();
            }

            return load.Handle;
        }

        private static async Task CompleteSceneCallbackAsync(Task<Scene> task, Action<Scene> callback)
        {
            callback(await task);
        }

        public Task UnloadSceneAsync(string location)
        {
            MainThread();
            if (!scenes.TryGetValue(location, out var load))
            {
                return Task.CompletedTask;
            }

            if (load.Unload != null)
            {
                return load.Unload;
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            load.Unload = completion.Task;
            CompleteUnloadAsync(location, load, completion).Forget();
            return completion.Task;
        }

        private async Task CompleteUnloadAsync(string location, SceneLoad load, TaskCompletionSource<bool> completion)
        {
            try
            {
                var scene = await load.Result.Task;
                if (load.Invalidated)
                {
                    completion.TrySetCanceled();
                    return;
                }

                var operation = load.Handle.UnloadAsync();
                await operation.Task;
                if (load.Invalidated)
                {
                    completion.TrySetCanceled();
                    return;
                }

                if (operation.Status != EOperationStatus.Succeed)
                {
                    throw new InvalidOperationException(operation.Error);
                }

                scenes.Remove(location);
                sceneLocations.Remove(scene.handle);
                completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                load.Unload = null;
                completion.TrySetException(error);
            }
        }

        public Task UnloadSceneAsync(Scene scene) => sceneLocations.TryGetValue(scene.handle, out var location) ? UnloadSceneAsync(location) : Task.CompletedTask;
        public async Task UnloadSceneAsync(string location, Action callback)
        {
            await UnloadSceneAsync(location);
            callback?.Invoke();
        }

        internal void ResetForPlay()
        {
            generation++;
            ReleaseOwnedAssetsAsync().Forget();
            if (atlasRegistered)
            {
                SpriteAtlasManager.atlasRequested -= RequestAtlas;
                atlasRegistered = false;
            }

            foreach (var load in assetLoads.Values)
            {
                load.Invalidated = true;
                load.Result.TrySetCanceled();
            }

            assetLoads.Clear();
            foreach (var handle in assetHandles.Values)
            {
                if (handle.IsValid)
                {
                    handle.Release();
                }
            }

            assetHandles.Clear();
            foreach (var load in scenes.Values)
            {
                load.Invalidated = true;
                load.Result.TrySetCanceled();
                RetireSceneHandleAsync(load.Handle).Forget();
            }

            scenes.Clear();
            sceneLocations.Clear();
            package = null;
            initialized = false;
            shuttingDown = false;
            initialization = null;
            shutdown = null;
        }

        /// <summary>入口退出时发起内部清理；不创建尚未使用的资源管理器。</summary>
        public static void Release()
        {
            if (IsCreated)
            {
                Instance.ShutdownAsync().Forget();
            }
        }

        public Task ShutdownAsync()
        {
            MainThread();
            if (shutdown != null && !shutdown.IsCompleted)
            {
                return shutdown;
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdown = completion.Task;
            CompleteShutdownAsync(completion).Forget();
            return shutdown;
        }

        private async Task CompleteShutdownAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                await ShutdownCoreAsync();
                completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
            }
        }

        private static async Task RetireSceneHandleAsync(SceneHandle handle)
        {
            if (!handle.IsValid)
            {
                return;
            }

            try
            {
                await handle.Task;
            }
            catch
            {
            }

            YooAssetSceneLoader.ReleaseIfSceneUnloaded(handle);
        }

        private async Task ShutdownCoreAsync()
        {
            int startedGeneration = generation;
            shuttingDown = true;
            var errors = new List<Exception>();
            try
            {
                try
                {
                    if (initialization != null && !initialization.IsCompleted)
                    {
                        await initialization;
                    }
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }

                if (startedGeneration != generation)
                {
                    return;
                }

                if (atlasRegistered)
                {
                    SpriteAtlasManager.atlasRequested -= RequestAtlas;
                    atlasRegistered = false;
                }

                var locations = new List<string>(scenes.Keys);
                foreach (var location in locations)
                {
                    try
                    {
                        await UnloadSceneAsync(location);
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }

                    if (startedGeneration != generation)
                    {
                        return;
                    }
                }
            }
            finally
            {
                // 旧运行的延迟完成不得清空新一轮 Play 中建立的资源缓存。
                if (startedGeneration == generation)
                {
                    try
                    {
                        ReleaseAllAssets();
                        await ReleaseOwnedAssetsAsync(respectOwner: true);
                    }
                    finally
                    {
                        if (startedGeneration == generation)
                        {
                            initialized = false;
                            initialization = null;
                            shuttingDown = false;
                        }
                    }
                }
            }

            if (errors.Count != 0)
            {
                throw new AggregateException("Resource shutdown failed; asset cleanup was still performed.", errors);
            }
        }
    }
}

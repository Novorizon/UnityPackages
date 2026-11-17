using System;
using System.Collections.Generic;
using System.Threading;
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
    /// <summary>资源对应的管理器。管理对应模块的状态和操作入口。</summary>
    public partial class ResourceManager : Singleton<ResourceManager>, IDisposable
    {
        /// <summary>结束资源管理器对相关资源的持有，并执行该类型的清理流程。</summary>
        public override void Dispose()
        {
            base.Release();
#if UNITY_EDITOR
            if (EditorStaticReset.IsResetting)
            {
                ResetForPlay();
                return;
            }

#endif
            ShutdownAsync().Forget();
        }

        /// <summary>表示资源管理器中的资源 Load。</summary>
        private sealed class AssetLoad
        {
            internal readonly TaskCompletionSource<Object> Result = new TaskCompletionSource<Object>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool Invalidated;
        }

        /// <summary>表示资源管理器中的场景 Load。</summary>
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
        private bool initialized, packageInitialized, atlasRegistered, shuttingDown, contentFullyDownloaded;
        private Task shutdown;
        private Task<bool> initialization;
        private Task<bool> packageInitialization;
        private int packageInitializationGeneration;
        private readonly Dictionary<string, AssetHandle> assetHandles = new Dictionary<string, AssetHandle>();
        private readonly Dictionary<string, AssetLoad> assetLoads = new Dictionary<string, AssetLoad>();
        private readonly Dictionary<string, SceneLoad> scenes = new Dictionary<string, SceneLoad>();
        private readonly Dictionary<int, string> sceneLocations = new Dictionary<int, string>();
        public bool Initialized => initialized;
        public bool PackageInitialized => packageInitialized;
        public string PackageVersion { get; private set; }

        /// <summary>场景协调器使用独立场景句柄，不借用共享的按地址缓存。</summary>
        public ISceneLoader CreateSceneLoader()
        {
            Ready();
            return new YooAssetSceneLoader(package);
        }

        /// <summary>校验当前调用位于 Unity 主线程。</summary>
        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("ResourceManager must be called on the initialized Unity main thread.");
            }
        }

        /// <summary>调用 MainThread 完成就绪。</summary>
        private void Ready()
        {
            MainThread();
            if (shuttingDown || !initialized || package == null)
            {
                throw new InvalidOperationException("Await ResourceManager.InitializeAsync first.");
            }
        }

        /// <summary>此服务需要异步加载，调用方应等待 InitializeAsync 完成。</summary>
        public override void Initialize()
        {
            throw new InvalidOperationException("Use and await InitializeAsync to initialize ResourceManager.");
        }

        /// <summary>仅初始化 DefaultPackage 文件系统，不查询版本、读取清单或下载资源。取消仅结束本次等待。</summary>
        public async Task<bool> InitializePackageAsync(InitializeParameters parameters, CancellationToken token = default)
        {
            MainThread();
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            token.ThrowIfCancellationRequested();
            int requestedGeneration = generation;
            if (shuttingDown)
            {
                await TaskUtil.WaitAsync(shutdown, token);
            }

            if (packageInitialization != null && !packageInitialization.IsCompleted && packageInitializationGeneration != generation)
            {
                // 上轮 Play 的原生初始化不能中止；必须结束后才能复用或重新初始化文件系统。
                await TaskUtil.WaitAsync(packageInitialization, token);
            }

            token.ThrowIfCancellationRequested();
            if (requestedGeneration != generation)
            {
                return false;
            }

            if (packageInitialized)
            {
                return true;
            }

            if (packageInitialization == null || packageInitialization.IsCompleted)
            {
                packageInitializationGeneration = generation;
                packageInitialization = InitializePackageCoreAsync(parameters);
            }

            return await TaskUtil.WaitAsync(packageInitialization, token);
        }

        /// <summary>异步初始化包核心。</summary>
        private async Task<bool> InitializePackageCoreAsync(InitializeParameters parameters)
        {
            YooAssets.Initialize();
            var current = YooAssets.TryGetPackage("DefaultPackage") ?? YooAssets.CreatePackage("DefaultPackage");
            package = current;
            YooAssets.SetDefaultPackage(current);
            int startedGeneration = generation;
            bool success = await InitializeFileSystemsAsync(current, parameters);
            if (startedGeneration != generation || shuttingDown)
            {
                return false;
            }

            packageInitialized = success;
            return success;
        }

        /// <summary>异步初始化资源管理器。</summary>
        public Task<bool> InitializeAsync()
        {
            MainThread();
            if (shuttingDown)
            {
                return InitializeAfterShutdownAsync(shutdown, generation);
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

        /// <summary>兼容入口：准备精确版本的内容，必要时先初始化资源文件系统。</summary>
        public Task<bool> InitializeAsync(ResourceInitializationOptions options, CancellationToken token = default)
        {
            return PrepareContentAsync(options, token);
        }

        /// <summary>准备精确版本清单与缺失资源；成功后内容 API 才可用。</summary>
        public async Task<bool> PrepareContentAsync(ResourceInitializationOptions options, CancellationToken token = default)
        {
            MainThread();
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.ConcurrentDownloads < 1 || options.FailedTryAgain < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }

            // Options are caller-owned and mutable. Keep the requested content stable across
            // native awaits; the provider/progress references themselves remain caller-owned.
            options = new ResourceInitializationOptions
            {
                Parameters = options.Parameters,
                PackageVersion = string.IsNullOrWhiteSpace(options.PackageVersion) ? null : options.PackageVersion,
                DownloadAll = options.DownloadAll,
                ConcurrentDownloads = options.ConcurrentDownloads,
                FailedTryAgain = options.FailedTryAgain,
                Progress = options.Progress
            };
            token.ThrowIfCancellationRequested();
            int requestedGeneration = generation;
            if (shuttingDown)
            {
                await TaskUtil.WaitAsync(shutdown, token);
            }

            if (requestedGeneration != generation)
            {
                return false;
            }

            if (initialized)
            {
                if (string.IsNullOrEmpty(options.PackageVersion) || PackageVersion == options.PackageVersion)
                {
                    RequireDownloadMode(options);
                    return true;
                }

                throw new InvalidOperationException("Release resources before selecting another package version.");
            }

            if (initialization == null || initialization.IsCompleted)
            {
                initialization = InitializeCoreAsync(options, token);
            }

            bool ready = await TaskUtil.WaitAsync(initialization, token);
            if (ready && options.PackageVersion != null && PackageVersion != options.PackageVersion)
            {
                throw new InvalidOperationException("Another content version was prepared by the shared initialization. Release resources before selecting another package version.");
            }

            if (ready)
            {
                RequireDownloadMode(options);
            }

            return ready;
        }

        /// <summary>检查 Download 模式是否满足前置条件。</summary>
        private void RequireDownloadMode(ResourceInitializationOptions options)
        {
            if (options.DownloadAll && !contentFullyDownloaded)
            {
                throw new InvalidOperationException("Content was initialized for on-demand loading. Release resources before requiring a full download.");
            }
        }

        /// <summary>异步初始化之后 Shutdown。</summary>
        private async Task<bool> InitializeAfterShutdownAsync(Task previous, int requestedGeneration)
        {
            await previous;
            if (requestedGeneration != generation)
            {
                return false;
            }
            return await InitializeAsync();
        }

        /// <summary>异步初始化资源管理器。</summary>
        public async Task InitializeAsync(Action<bool> callback)
        {
            var result = await InitializeAsync();
            callback?.Invoke(result);
        }

        /// <summary>异步初始化资源管理器。</summary>
        private async Task<bool> InitializeCoreAsync(ResourceInitializationOptions options = null, CancellationToken token = default)
        {
            int startedGeneration = generation;
            var parameters = options?.Parameters ?? YooAssetsSettings.CreateParameters();
            if (!await InitializePackageAsync(parameters, token))
            {
                return false;
            }

            if (startedGeneration != generation || shuttingDown)
            {
                return false;
            }

            bool success = await InitPackageCoreAsync(package, parameters, options, token);
            if (startedGeneration != generation || shuttingDown)
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

        /// <summary>异步初始化包。</summary>
        public Task<bool> InitPackageAsync(ResourcePackage resourcePackage, InitializeParameters parameters)
        {
            return InitPackageCoreAsync(resourcePackage, parameters, null, CancellationToken.None);
        }

        /// <summary>异步初始化文件 Systems。</summary>
        private static async Task<bool> InitializeFileSystemsAsync(ResourcePackage resourcePackage, InitializeParameters parameters)
        {
            if (resourcePackage.InitializeStatus != EOperationStatus.Succeed)
            {
                var operation = resourcePackage.InitializeAsync(parameters);
                // YooAsset 的初始化不能中止；保留任务直到原生操作结束，避免取消后并发初始化或销毁。
                await operation.Task;
                if (operation.Status != EOperationStatus.Succeed)
                {
                    Logger.Error(operation.Error);
                    return false;
                }
            }

            return true;
        }

        /// <summary>异步初始化包核心。</summary>
        private async Task<bool> InitPackageCoreAsync(
            ResourcePackage resourcePackage,
            InitializeParameters parameters,
            ResourceInitializationOptions options,
            CancellationToken token)
        {
            MainThread();
            token.ThrowIfCancellationRequested();
            int startedGeneration = generation;
            if (!await InitializeFileSystemsAsync(resourcePackage, parameters))
            {
                return false;
            }

            token.ThrowIfCancellationRequested();
            if (startedGeneration != generation || shuttingDown)
            {
                return false;
            }

            string version = options?.PackageVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                var request = resourcePackage.RequestPackageVersionAsync();
                await request.Task;
                token.ThrowIfCancellationRequested();
                if (request.Status != EOperationStatus.Succeed)
                {
                    Logger.Error(request.Error);
                    return false;
                }

                version = request.PackageVersion;
            }

            var manifest = resourcePackage.UpdatePackageManifestAsync(version);
            await manifest.Task;
            token.ThrowIfCancellationRequested();
            if (manifest.Status != EOperationStatus.Succeed)
            {
                Logger.Error(manifest.Error);
                return false;
            }

            if (options?.DownloadAll == true)
            {
                var downloader = resourcePackage.CreateResourceDownloader(options.ConcurrentDownloads, options.FailedTryAgain);
                downloader.DownloadUpdateCallback = value => Report(options.Progress,
                    new ResourceDownloadProgress(value.TotalDownloadCount, value.CurrentDownloadCount, value.TotalDownloadBytes, value.CurrentDownloadBytes));
                await RunDownloadAsync(downloader, token);

                token.ThrowIfCancellationRequested();
                if (downloader.Status != EOperationStatus.Succeed)
                {
                    Logger.Error(downloader.Error);
                    return false;
                }

                Report(options.Progress, new ResourceDownloadProgress(downloader.TotalDownloadCount, downloader.TotalDownloadCount,
                    downloader.TotalDownloadBytes, downloader.TotalDownloadBytes));
            }

            if (startedGeneration != generation || shuttingDown)
            {
                return false;
            }

            PackageVersion = version;
            contentFullyDownloaded = options?.DownloadAll == true;
            return true;
        }

        /// <summary>异步运行 Download。</summary>
        private static async Task RunDownloadAsync(ResourceDownloaderOperation downloader, CancellationToken token)
        {
            Task nativeDownload = downloader.Task;
            // YooAsset must enter OperationSystem before cancellation can mark it Done.
            // Cancel-before-Begin prevents registration and leaves the captured Task unfinished.
            downloader.BeginDownload();
            using (token.Register(() => UnityThread.RunAsync(() =>
            {
                if (!nativeDownload.IsCompleted)
                {
                    downloader.CancelDownload();
                }
            }).Forget()))
            {
                try
                {
                    await TaskUtil.WaitAsync(nativeDownload, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // The public waiter can already return cancellation. Retain the shared
                    // initialization until YooAsset acknowledges it so shutdown/retry cannot
                    // replace the manifest while the previous native download is still active.
                    try
                    {
                        await nativeDownload;
                    }
                    catch
                    {
                        // The caller cancellation remains the result; the native task is observed.
                    }
                    throw;
                }
            }
        }

        /// <summary>上报资源管理器。</summary>
        private static void Report(IProgress<ResourceDownloadProgress> progress, ResourceDownloadProgress value)
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

        /// <summary>异步加载资源。</summary>
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
        /// <summary>尝试取得已加载，返回是否成功。</summary>
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

        /// <summary>异步加载资源。</summary>
        public async Task LoadAssetAsync<T>(string location, Action<T> callback)
            where T : Object
        {
            var result = await LoadAssetAsync<T>(location);
            callback?.Invoke(result);
        }

        /// <summary>异步完成资源。</summary>
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

        /// <summary>加载资源。</summary>
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

        /// <summary>加载资源。</summary>
        public void LoadAsset<T>(string location, Action<T> callback)
            where T : Object
        {
            var result = LoadAsset<T>(location);
            callback?.Invoke(result);
        }

        /// <summary>根据传入参数取得施法。</summary>
        private static T Cast<T>(Object value, string location)
            where T : Object => value as T ?? throw new InvalidCastException("Resource type mismatch at " + location);
        /// <summary>验证传入的 location。</summary>
        private static void Validate(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Resource location is required.", nameof(location));
            }
        }

        /// <summary>释放句柄。</summary>
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

        /// <summary>释放全部资源。</summary>
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

        /// <summary>尝试处理 Unload 资源。</summary>
        public void TryUnloadAsset(string location, int loopCount = 10)
        {
            Ready();
            ReleaseHandle(location);
            package.TryUnloadUnusedAsset(location, loopCount);
        }

        /// <summary>尝试处理 Unload 未使用资源。</summary>
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

        /// <summary>获取场景。</summary>
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

        /// <summary>异步完成场景。</summary>
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

        /// <summary>异步加载场景。</summary>
        public Task<Scene> LoadSceneAsync(string location) => GetScene(location, LoadSceneMode.Single).Result.Task;
        /// <summary>异步加载场景。</summary>
        public Task<Scene> LoadSceneAsync(string location, LoadSceneMode mode) => GetScene(location, mode).Result.Task;
        /// <summary>异步加载场景。</summary>
        public async Task LoadSceneAsync(string location, Action<Scene> callback)
        {
            var scene = await LoadSceneAsync(location);
            callback?.Invoke(scene);
        }

        /// <summary>异步加载场景。</summary>
        public async Task LoadSceneAsync(string location, LoadSceneMode mode, Action<Scene> callback)
        {
            var scene = await LoadSceneAsync(location, mode);
            callback?.Invoke(scene);
        }

        /// <summary>异步加载场景句柄。</summary>
        public SceneHandle LoadSceneHandleAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, Action<Scene> callback = null)
        {
            var load = GetScene(location, mode);
            if (callback != null)
            {
                CompleteSceneCallbackAsync(load.Result.Task, callback).Forget();
            }

            return load.Handle;
        }

        /// <summary>异步完成场景回调。</summary>
        private static async Task CompleteSceneCallbackAsync(Task<Scene> task, Action<Scene> callback)
        {
            callback(await task);
        }

        /// <summary>异步卸载场景。</summary>
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

        /// <summary>异步完成 Unload。</summary>
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

        /// <summary>异步卸载场景。</summary>
        public Task UnloadSceneAsync(Scene scene) => sceneLocations.TryGetValue(scene.handle, out var location) ? UnloadSceneAsync(location) : Task.CompletedTask;
        /// <summary>异步卸载场景。</summary>
        public async Task UnloadSceneAsync(string location, Action callback)
        {
            await UnloadSceneAsync(location);
            callback?.Invoke();
        }

        /// <summary>重置 ForPlay。</summary>
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
            packageInitialized = false;
            PackageVersion = null;
            contentFullyDownloaded = false;
            shuttingDown = false;
            initialization = null;
            shutdown = null;
        }

        /// <summary>异步关闭资源管理器。</summary>
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

        /// <summary>异步完成 Shutdown。</summary>
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

        /// <summary>异步终止使用场景句柄。</summary>
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

        /// <summary>异步关闭资源管理器。</summary>
        private async Task ShutdownCoreAsync()
        {
            int startedGeneration = generation;
            shuttingDown = true;
            var errors = new List<Exception>();
            try
            {
                foreach (var pending in new Task[] { packageInitialization, initialization })
                {
                    try
                    {
                        if (pending != null && !pending.IsCompleted)
                        {
                            await pending;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // 取消初始化是正常退出路径；原生操作结束后继续释放资源。
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
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
                            packageInitialized = false;
                            PackageVersion = null;
                            contentFullyDownloaded = false;
                            initialization = null;
                            packageInitialization = null;
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

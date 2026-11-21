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
    // YooAsset itself remains unmodified. All handle operations run on Unity's main thread.
    /// <summary>资源对应的管理器。管理对应模块的状态和操作入口。</summary>
    public partial class ResourceManager : Singleton<ResourceManager>, IDisposable
    {
        /// <summary>结束资源管理器对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            if (!CheckMainThread(nameof(Dispose)))
            {
                return;
            }
#if UNITY_EDITOR
            if (EditorStaticReset.IsResetting)
            {
                ResetForPlay();
                return;
            }

#endif
            ShutdownAsync().Forget();
        }

        /// <summary>表示资源管理器中的场景 Load。</summary>
        private sealed class SceneLoad
        {
            internal SceneHandle Handle;
            internal bool Invalidated;
            internal LoadSceneMode Mode;
            internal readonly TaskCompletionSource<Scene> Result = new TaskCompletionSource<Scene>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task<bool> Unload;
        }

        private ResourcePackage package;
        private int generation;
        private bool initialized, packageInitialized, atlasRegistered, shuttingDown;
        private Task<bool> shutdown;
        private Task<bool> initialization;
        private Task<bool> packageInitialization;
        private int packageInitializationGeneration;
        private readonly Dictionary<string, SceneLoad> scenes = new Dictionary<string, SceneLoad>();
        private readonly Dictionary<int, string> sceneLocations = new Dictionary<int, string>();
        public bool Initialized => initialized;
        public bool PackageInitialized => packageInitialized;
        public string PackageVersion { get; private set; }

        /// <summary>资源未就绪时返回 null。调用方取得加载器后负责其生命周期。</summary>
        public ISceneLoader CreateSceneLoader() =>
            CheckReady(nameof(CreateSceneLoader)) ? new YooAssetSceneLoader(package) : null;

        internal static bool CheckMainThread(string operation)
        {
            if (UnityThread.IsMainThread)
            {
                return true;
            }
            Logger.Error("Resource operation " + operation + " requires the initialized Unity main thread.");
            return false;
        }

        private bool CheckReady(string operation)
        {
            if (!CheckMainThread(operation))
            {
                return false;
            }
            if (IsReady && package != null)
            {
                return true;
            }
            Logger.Warn("Resource package is unavailable for " + operation + ". Initialize it first.");
            return false;
        }

        /// <summary>资源需要异步初始化。调用 InitializeAsync 并等待结果。</summary>
        public override void Initialize()
        {
            Logger.Error("Use and await ResourceManager.InitializeAsync to initialize ResourceManager.");
        }

        // 记录原生文件系统操作，关闭和下一轮 Play 可以等待它安全结束。
        private Task<bool> MountDefaultPackageAsync(InitializeParameters parameters)
        {
            if (packageInitialized)
            {
                return Task.FromResult(true);
            }

            if (packageInitialization == null || packageInitialization.IsCompleted)
            {
                packageInitializationGeneration = generation;
                packageInitialization = MountDefaultPackageCoreAsync(parameters);
            }

            return packageInitialization;
        }

        private async Task<bool> MountDefaultPackageCoreAsync(InitializeParameters parameters)
        {
            var current = GetDefaultPackage();
            int startedGeneration = generation;
            if (current.InitializeStatus != EOperationStatus.Succeed)
            {
                var operation = current.InitializeAsync(parameters);
                await operation.Task;
                if (operation.Status != EOperationStatus.Succeed)
                {
                    Logger.Error(operation.Error);
                    return false;
                }
            }

            if (startedGeneration != generation || shuttingDown)
            {
                return false;
            }

            packageInitialized = true;
            return true;
        }

        // 初始化 YooAsset 驱动和包对象；文件系统在需要来源参数时才挂载。
        private ResourcePackage GetDefaultPackage()
        {
            if (!YooAssets.Initialized)
            {
                YooAssets.Initialize();
            }
            package = YooAssets.TryGetPackage("DefaultPackage") ?? YooAssets.CreatePackage("DefaultPackage");
            YooAssets.SetDefaultPackage(package);
            return package;
        }

        // 文件系统和清单就绪后才启用资源读取与图集请求。
        private async Task<bool> InitializeCoreAsync(InitializeParameters parameters, CancellationToken token, PackageDownload? download)
        {
            int startedGeneration = generation;
            if (token.IsCancellationRequested || !await MountDefaultPackageAsync(parameters))
            {
                return false;
            }

            if (startedGeneration != generation || shuttingDown)
            {
                return false;
            }

            bool success = await LoadPackageContentAsync(package, token, download);
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

        // 使用已挂载的包更新清单。下载设置存在时，再下载该版本缺失的资源。
        private async Task<bool> LoadPackageContentAsync(ResourcePackage resourcePackage, CancellationToken token, PackageDownload? download)
        {
            int startedGeneration = generation;
            if (token.IsCancellationRequested)
            {
                return false;
            }

            string version = download?.PackageVersion;
            if (string.IsNullOrWhiteSpace(version))
            {
                var request = resourcePackage.RequestPackageVersionAsync();
                await request.Task;
                if (token.IsCancellationRequested || startedGeneration != generation || shuttingDown)
                {
                    return false;
                }
                if (request.Status != EOperationStatus.Succeed || string.IsNullOrWhiteSpace(request.PackageVersion))
                {
                    Logger.Error(request.Error);
                    return false;
                }

                version = request.PackageVersion;
            }

            var manifest = resourcePackage.UpdatePackageManifestAsync(version);
            await manifest.Task;
            if (token.IsCancellationRequested || startedGeneration != generation || shuttingDown)
            {
                return false;
            }
            if (manifest.Status != EOperationStatus.Succeed)
            {
                Logger.Error(manifest.Error);
                return false;
            }

            if (download is { } setting)
            {
                var downloader = resourcePackage.CreateResourceDownloader(setting.ConcurrentDownloads, setting.FailedTryAgain);
                void Publish(ResourceDownloadProgress value)
                {
                    if (!token.IsCancellationRequested && startedGeneration == generation && !shuttingDown)
                    {
                        DownloadProgress = value;
                        Report(setting.Progress, value);
                    }
                }
                downloader.DownloadUpdateCallback = value => Publish(new ResourceDownloadProgress(value.TotalDownloadCount,
                    value.CurrentDownloadCount, value.TotalDownloadBytes, value.CurrentDownloadBytes));
                await RunDownloadAsync(downloader, token);

                if (token.IsCancellationRequested || startedGeneration != generation || shuttingDown)
                {
                    return false;
                }
                if (downloader.Status != EOperationStatus.Succeed)
                {
                    Logger.Error(downloader.Error);
                    return false;
                }

                Publish(new ResourceDownloadProgress(downloader.TotalDownloadCount, downloader.TotalDownloadCount,
                    downloader.TotalDownloadBytes, downloader.TotalDownloadBytes));
            }

            if (token.IsCancellationRequested || startedGeneration != generation || shuttingDown)
            {
                return false;
            }

            PackageVersion = version;
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
                // 调用方可先结束等待，管理器保留此任务直到原生下载器退出。
                await nativeDownload;
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

        internal static bool CheckSceneRequest(string location, LoadSceneMode mode)
        {
            if (!string.IsNullOrWhiteSpace(location) && (mode == LoadSceneMode.Single || mode == LoadSceneMode.Additive))
            {
                return true;
            }
            Logger.Warn("Invalid resource scene location or load mode: " + location);
            return false;
        }

        /// <summary>请 YooAsset 回收该地址的闲置资源。已持有的引用继续有效。</summary>
        public void TryUnloadAsset(string location, int loopCount = 10)
        {
            if (loopCount > 0 && CanLoadAsset(location, nameof(TryUnloadAsset)))
            {
                package.TryUnloadUnusedAsset(location, loopCount);
            }
        }

        /// <summary>尝试处理 Unload 未使用资源。</summary>
        public async Task TryUnloadUnusedAsset(int loopCount = 10)
        {
            if (loopCount <= 0 || !CheckReady(nameof(TryUnloadUnusedAsset)))
            {
                return;
            }
            int startedGeneration = generation;
            var unload = package.UnloadUnusedAssetsAsync(loopCount);
            await unload.Task;
            if (startedGeneration != generation || shuttingDown)
            {
                return;
            }
            if (unload.Status != EOperationStatus.Succeed)
            {
                Logger.Warn("Could not unload unused resources: " + unload.Error);
                return;
            }
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
            if (!CheckReady(nameof(LoadSceneAsync)) || !CheckSceneRequest(location, mode))
            {
                return null;
            }
            if (scenes.TryGetValue(location, out var existing))
            {
                if (existing.Handle.IsValid && (!existing.Result.Task.IsCompleted || existing.Handle.SceneObject.isLoaded))
                {
                    if (existing.Mode != mode || existing.Unload != null)
                    {
                        Logger.Warn("Scene is already loading or loaded with another mode, or is unloading: " + location);
                        return null;
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

            SceneHandle handle;
            try
            {
                handle = package.LoadSceneAsync(location, mode);
            }
            catch (Exception error) when (IsResourceIoFailure(error))
            {
                Logger.Warn("Could not start loading scene " + location + ": " + error.Message);
                return null;
            }
            if (handle == null)
            {
                Logger.Warn("YooAsset did not provide a scene handle: " + location);
                return null;
            }

            var load = new SceneLoad { Mode = mode, Handle = handle };
            scenes.Add(location, load);
            CompleteSceneAsync(location, load).Forget();
            return load;
        }

        private async Task CompleteSceneAsync(string location, SceneLoad load)
        {
            try
            {
                var loading = load.Handle.Task;
                if (loading != null)
                {
                    await loading;
                    if (!load.Invalidated && load.Handle.IsValid && load.Handle.Status == EOperationStatus.Succeed)
                    {
                        var scene = load.Handle.SceneObject;
                        sceneLocations[scene.handle] = location;
                        load.Result.TrySetResult(scene);
                        return;
                    }
                }

                if (!load.Invalidated)
                {
                    Logger.Warn("Could not load scene " + location + ": " + (load.Handle.IsValid ? load.Handle.LastError : "Invalid handle."));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }

            if (scenes.TryGetValue(location, out var current) && ReferenceEquals(current, load))
            {
                scenes.Remove(location);
            }
            YooAssetSceneLoader.ReleaseIfSceneUnloaded(load.Handle);
            load.Result.TrySetResult(default);
        }

        /// <summary>异步加载场景。</summary>
        public Task<Scene> LoadSceneAsync(string location) => LoadSceneAsync(location, LoadSceneMode.Single);
        /// <summary>异步加载场景。</summary>
        public Task<Scene> LoadSceneAsync(string location, LoadSceneMode mode) => GetScene(location, mode)?.Result.Task ?? Task.FromResult(default(Scene));
        /// <summary>异步加载场景。</summary>
        public async Task LoadSceneAsync(string location, Action<Scene> callback)
        {
            if (callback != null && CheckMainThread(nameof(LoadSceneAsync)))
            {
                callback(await LoadSceneAsync(location));
            }
        }

        /// <summary>异步加载场景。</summary>
        public async Task LoadSceneAsync(string location, LoadSceneMode mode, Action<Scene> callback)
        {
            if (callback != null && CheckMainThread(nameof(LoadSceneAsync)))
            {
                callback(await LoadSceneAsync(location, mode));
            }
        }

        /// <summary>异步加载场景句柄。</summary>
        public SceneHandle LoadSceneHandleAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, Action<Scene> callback = null)
        {
            if (!CheckMainThread(nameof(LoadSceneHandleAsync)))
            {
                return null;
            }
            var load = GetScene(location, mode);
            if (callback != null)
            {
                var result = load?.Result.Task ?? Task.FromResult(default(Scene));
                CompleteSceneCallbackAsync(result, callback).Forget();
            }

            return load?.Handle;
        }

        /// <summary>异步完成场景回调。</summary>
        private static async Task CompleteSceneCallbackAsync(Task<Scene> task, Action<Scene> callback)
        {
            callback(await task);
        }

        /// <summary>卸载已持有的场景。重复请求复用同一次卸载。</summary>
        public Task UnloadSceneAsync(string location) =>
            CheckMainThread(nameof(UnloadSceneAsync)) ? UnloadSceneCore(location) : Task.CompletedTask;

        private Task<bool> UnloadSceneCore(string location)
        {
            if (string.IsNullOrWhiteSpace(location) || !scenes.TryGetValue(location, out var load))
            {
                return Task.FromResult(true);
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
            bool success = false;
            try
            {
                var scene = await load.Result.Task;
                if (load.Invalidated)
                {
                    return;
                }
                if (scene.IsValid() && scene.isLoaded && load.Handle.IsValid)
                {
                    var operation = load.Handle.UnloadAsync();
                    await operation.Task;
                    if (load.Invalidated)
                    {
                        return;
                    }
                    if (operation.Status != EOperationStatus.Succeed)
                    {
                        Logger.Warn("Could not unload scene " + location + ": " + operation.Error);
                        return;
                    }
                }

                if (scenes.TryGetValue(location, out var current) && ReferenceEquals(current, load))
                {
                    scenes.Remove(location);
                    sceneLocations.Remove(scene.handle);
                }
                success = true;
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
                load.Unload = null;
                completion.TrySetResult(success);
            }
        }

        /// <summary>仅卸载此管理器拥有的场景。</summary>
        public Task UnloadSceneAsync(Scene scene)
        {
            if (!CheckMainThread(nameof(UnloadSceneAsync)) || !sceneLocations.TryGetValue(scene.handle, out var location))
            {
                return Task.CompletedTask;
            }
            return UnloadSceneCore(location);
        }

        /// <summary>异步卸载场景。</summary>
        public async Task UnloadSceneAsync(string location, Action callback)
        {
            if (CheckMainThread(nameof(UnloadSceneAsync)))
            {
                await UnloadSceneCore(location);
                callback?.Invoke();
            }
        }

        /// <summary>重置 ForPlay。</summary>
        internal void ResetForPlay()
        {
            generation++;
            CancelResourceOperation();
            if (atlasRegistered)
            {
                SpriteAtlasManager.atlasRequested -= RequestAtlas;
                atlasRegistered = false;
            }

            ReleaseAssetsCore();
            foreach (var load in scenes.Values)
            {
                load.Invalidated = true;
                load.Result.TrySetResult(default);
                RetireSceneHandleAsync(load.Handle).Forget();
            }

            scenes.Clear();
            sceneLocations.Clear();
            package = null;
            initialized = false;
            packageInitialized = false;
            PackageVersion = null;
            ClearResourceMode();
            shuttingDown = false;
            initialization = null;
            shutdown = null;
        }

        /// <summary>入口退出时发起清理。尚未使用时直接返回。</summary>
        public new static void Release()
        {
            if (TryGetInstance(out var current))
            {
                current.ShutdownAsync().Forget();
            }
        }

        /// <summary>异步关闭资源管理器。</summary>
        public Task ShutdownAsync() => ShutdownPackageAsync();

        private Task<bool> ShutdownPackageAsync()
        {
            if (!CheckMainThread(nameof(ShutdownAsync)))
            {
                return Task.FromResult(false);
            }
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
                completion.TrySetResult(await ShutdownCoreAsync());
            }
            catch (Exception error)
            {
                Logger.Exception(error);
                completion.TrySetResult(false);
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
        private async Task<bool> ShutdownCoreAsync()
        {
            int startedGeneration = generation;
            shuttingDown = true;
            CancelResourceOperation();
            CancelPendingAssetLoads();
            bool packageRetired = true;
            bool scenesReleased = true;
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
                        Logger.Exception(error);
                    }
                }

                if (startedGeneration != generation)
                {
                    return false;
                }

                if (atlasRegistered)
                {
                    SpriteAtlasManager.atlasRequested -= RequestAtlas;
                    atlasRegistered = false;
                }

                var locations = new List<string>(scenes.Keys);
                foreach (var location in locations)
                {
                    scenesReleased &= await UnloadSceneCore(location);

                    if (startedGeneration != generation)
                    {
                        return false;
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
                        // 场景若仍在使用，则保留它用到的资源和 Package，供下一次关闭重试。
                        if (scenesReleased)
                        {
                            ReleaseAssetsCore();
                        }
                        if (startedGeneration == generation && scenesReleased && package != null)
                        {
                            packageRetired = await RetirePackageAsync(package, startedGeneration);
                            if (packageRetired && startedGeneration == generation)
                            {
                                scenes.Clear();
                                sceneLocations.Clear();
                            }
                        }
                    }
                    finally
                    {
                        if (startedGeneration == generation)
                        {
                            initialized = false;
                            packageInitialized = false;
                            PackageVersion = null;
                            initialization = null;
                            packageInitialization = null;
                            ClearResourceMode();
                            shuttingDown = false;
                        }
                    }
                }
            }

            return scenesReleased && packageRetired;
        }
    }
}

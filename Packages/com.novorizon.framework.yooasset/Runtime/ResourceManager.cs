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
    public partial class ResourceManager : Singleton<ResourceManager>, IDisposable
    {
        public void Dispose()
        {
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
            internal Task Unload;
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

        /// <summary>此服务需要异步加载，调用方应等待 InitializeAsync 完成。</summary>
        public override void Initialize()
        {
            throw new InvalidOperationException("Use and await InitializeAsync to initialize ResourceManager.");
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

        /// <summary>根据传入参数取得施法。</summary>
        private static T Cast<T>(Object value, string location)
            where T : Object => value as T ?? throw new InvalidCastException("Resource type mismatch at " + location);
        private static void Validate(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Resource location is required.", nameof(location));
            }
        }

        /// <summary>请 YooAsset 回收该地址的闲置资源。已持有的引用继续有效。</summary>
        public void TryUnloadAsset(string location, int loopCount = 10)
        {
            Ready();
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
            CancelResourceOperation();
            ReleaseOwnedAssetsAsync().Forget();
            if (atlasRegistered)
            {
                SpriteAtlasManager.atlasRequested -= RequestAtlas;
                atlasRegistered = false;
            }

            ReleaseAssetsCore();
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
            ClearResourceMode();
            shuttingDown = false;
            initialization = null;
            shutdown = null;
        }

        /// <summary>入口退出时发起内部清理；不创建尚未使用的资源管理器。</summary>
        public new static void Release()
        {
            if (TryGetInstance(out var current))
            {
                current.ShutdownAsync().Forget();
            }
        }

        public Task ShutdownAsync()
        {
            return ShutdownAsync(retirePackage: resourceMode.HasValue);
        }

        private Task<bool> ShutdownAsync(bool retirePackage)
        {
            MainThread();
            retirePackageOnShutdown |= retirePackage;
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
                completion.TrySetResult(await ShutdownCoreAsync());
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

        /// <summary>异步关闭资源管理器。</summary>
        private async Task<bool> ShutdownCoreAsync()
        {
            int startedGeneration = generation;
            shuttingDown = true;
            CancelResourceOperation();
            bool packageRetired = true;
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
                        ReleaseAllAssets();
                        await ReleaseOwnedAssetsAsync(respectOwner: true);
                        if (startedGeneration == generation && retirePackageOnShutdown && package != null)
                        {
                            packageRetired = await RetirePackageAsync(package, startedGeneration);
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

            if (errors.Count != 0)
            {
                throw new AggregateException("Resource shutdown failed; asset cleanup was still performed.", errors);
            }

            return packageRetired;
        }
    }
}

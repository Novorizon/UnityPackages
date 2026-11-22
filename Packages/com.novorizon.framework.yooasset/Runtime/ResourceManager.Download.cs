using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using YooAsset;
using Logger = Game.Framework.Logger;

namespace Game.Resource
{
    public static partial class ResourceManager
    {
        // 在异步操作开始前保存版本、限额和进度回调，避免读到调用方后续修改的设置。
        private readonly struct PackageDownload
        {
            public readonly string PackageVersion;
            public readonly int ConcurrentDownloads;
            public readonly int FailedTryAgain;
            public readonly IProgress<ResourceDownloadProgress> Progress;

            public PackageDownload(string packageVersion, int concurrentDownloads, int failedTryAgain, IProgress<ResourceDownloadProgress> progress)
            {
                PackageVersion = packageVersion;
                ConcurrentDownloads = concurrentDownloads;
                FailedTryAgain = failedTryAgain;
                Progress = progress;
            }
        }

        private static ResourceMode? resourceMode;
        private static (InitializeParameters Parameters, string MainUrl, string FallbackUrl)? downloadSource;
        private static CancellationTokenSource resourceOperationStop;

        /// <summary>当前资源版本可用于读取资源和加载场景。</summary>
        public static bool IsReady => initialized && !shuttingDown;

        /// <summary>本次下载的进度快照。尚未开始下载时为空。</summary>
        public static ResourceDownloadProgress? DownloadProgress { get; private set; }

        /// <summary>初始化 YooAsset。离线或编辑器模拟时挂载本地清单，在线时等待 DownloadAsync 提供来源。</summary>
        public static async Task<bool> InitializeAsync(ResourceMode resourceMode, CancellationToken token = default)
        {
            if (!CheckMainThread(nameof(InitializeAsync)) || token.IsCancellationRequested)
            {
                return false;
            }

            if (resourceMode != ResourceMode.Offline && resourceMode != ResourceMode.Online && resourceMode != ResourceMode.EditorSimulation)
            {
                Logger.Warn("Unknown resource mode.");
                return false;
            }
#if !UNITY_EDITOR
            if (resourceMode == ResourceMode.EditorSimulation)
            {
                Logger.Warn("Resource simulation requires the Unity Editor.");
                return false;
            }
#endif
            int requestedGeneration = generation;
            try
            {
                Task<bool> stopping = shutdown;
                if (shuttingDown && (!await WaitForResourceOperationAsync(stopping, token) || !await stopping || token.IsCancellationRequested))
                {
                    return false;
                }
                Task<bool> previousPackageInitialization = packageInitialization;
                if (previousPackageInitialization is { IsCompleted: false } && packageInitializationGeneration != requestedGeneration &&
                    !await WaitForResourceOperationAsync(previousPackageInitialization, token))
                {
                    return false;
                }
                if (requestedGeneration != generation || token.IsCancellationRequested)
                {
                    return false;
                }
                if (ResourceManager.resourceMode.HasValue ? ResourceManager.resourceMode != resourceMode : HasPendingInitialization() || initialized)
                {
                    Logger.Warn("Reset the current resource package before changing modes.");
                    return false;
                }
                if (!ResourceManager.resourceMode.HasValue && GetDefaultPackage().InitializeStatus != EOperationStatus.None)
                {
                    Logger.Warn("Reset the existing DefaultPackage before initializing a resource mode.");
                    return false;
                }

                ResourceManager.resourceMode = resourceMode;
                if (resourceMode == ResourceMode.Online || IsReady)
                {
                    return true;
                }

                Task<bool> pending = initialization;
                if (pending == null || pending.IsCompleted)
                {
                    var parameters = resourceMode == ResourceMode.Offline
                        ? YooAssetsSettings.CreateOfflinePlayModeParameters()
                        : YooAssetsSettings.CreateEditorSimulateModeParameters();
                    if (parameters == null)
                    {
                        return false;
                    }
                    pending = BeginModeInitialization(parameters, token);
                }
                return await WaitForResourceOperationAsync(pending, token) && await pending &&
                    ReferenceEquals(initialization, pending) && IsReady;
            }
            catch (Exception error) when (IsResourceIoFailure(error))
            {
                Logger.Warn("Resource initialization failed: " + error.Message);
                return false;
            }
        }

        /// <summary>根据远端下载设置挂载清单并下载全部缺失资源。成功后才发布版本和就绪状态。</summary>
        public static async Task<ResourceDownloadResult> DownloadAsync(RemoteDownloadSetting setting, CancellationToken token = default)
        {
            if (token.IsCancellationRequested)
            {
                return new ResourceDownloadResult(ResourceDownloadStatus.Cancelled);
            }
            if (!CheckMainThread(nameof(DownloadAsync)))
            {
                return new ResourceDownloadResult(ResourceDownloadStatus.Failed);
            }
            if (resourceMode != ResourceMode.Online)
            {
                return DownloadFailure(ResourceDownloadStatus.Failed, "Initialize the online resource mode before downloading.");
            }
            if (shuttingDown || HasPendingInitialization())
            {
                return DownloadFailure(ResourceDownloadStatus.Busy, "A resource operation is still running.");
            }
            if (setting == null || setting.ConcurrentDownloads < 1 || setting.FailedTryAgain < 0)
            {
                return DownloadFailure(ResourceDownloadStatus.Failed, "Invalid resource download setting.");
            }

            string mainUrl = setting.Parameters == null ? setting.MainUrl?.Trim().TrimEnd('/') : null;
            string fallbackUrl = setting.Parameters == null
                ? (string.IsNullOrWhiteSpace(setting.FallbackUrl) ? mainUrl : setting.FallbackUrl.Trim().TrimEnd('/')) : null;
            if (setting.Parameters == null && (!YooAssetsSettings.IsRemoteUrl(mainUrl) || !YooAssetsSettings.IsRemoteUrl(fallbackUrl)))
            {
                return DownloadFailure(ResourceDownloadStatus.Failed, "Configure valid resource host URLs.");
            }
            var source = (setting.Parameters, mainUrl, fallbackUrl);
            string version = string.IsNullOrWhiteSpace(setting.PackageVersion) ? null : setting.PackageVersion;
            if (downloadSource.HasValue && downloadSource.Value != source ||
                IsReady && (version == null || version != PackageVersion))
            {
                return DownloadFailure(ResourceDownloadStatus.ResetRequired, "Reset the resource package before selecting a new source or version.");
            }
            if (IsReady)
            {
                return new ResourceDownloadResult(ResourceDownloadStatus.UpToDate, PackageVersion);
            }

            try
            {
                var parameters = setting.Parameters ?? YooAssetsSettings.CreateHostPlayModeParameters(mainUrl, fallbackUrl);
                if (parameters == null)
                {
                    return new ResourceDownloadResult(ResourceDownloadStatus.Failed);
                }
                downloadSource = source;
                DownloadProgress = null;
                var download = new PackageDownload(version, setting.ConcurrentDownloads, setting.FailedTryAgain, setting.Progress);
                Task<bool> pending = BeginModeInitialization(parameters, token, download);
                if (!await WaitForResourceOperationAsync(pending, token) || !await pending ||
                    !ReferenceEquals(initialization, pending) || !IsReady)
                {
                    return new ResourceDownloadResult(token.IsCancellationRequested || shuttingDown || !ReferenceEquals(initialization, pending)
                        ? ResourceDownloadStatus.Cancelled : ResourceDownloadStatus.Failed);
                }

                var status = DownloadProgress?.TotalCount > 0 ? ResourceDownloadStatus.Downloaded : ResourceDownloadStatus.UpToDate;
                return new ResourceDownloadResult(status, PackageVersion);
            }
            catch (Exception error) when (IsResourceIoFailure(error))
            {
                return DownloadFailure(ResourceDownloadStatus.Failed, "Resource download failed: " + error.Message);
            }
        }

        /// <summary>释放资源和场景、销毁 DefaultPackage，允许下一次初始化切换文件系统。</summary>
        public static async Task<bool> ResetAsync(CancellationToken token = default)
        {
            if (!CheckMainThread(nameof(ResetAsync)) || token.IsCancellationRequested)
            {
                return false;
            }

            try
            {
                Task<bool> pending = ShutdownPackageAsync();
                return await WaitForResourceOperationAsync(pending, token) && await pending;
            }
            catch (Exception error) when (IsResourceIoFailure(error))
            {
                Logger.Warn("Resource reset failed: " + error.Message);
                return false;
            }
        }

        private static bool HasPendingInitialization() =>
            initialization is { IsCompleted: false } || packageInitialization is { IsCompleted: false };

        private static Task<bool> BeginModeInitialization(InitializeParameters parameters, CancellationToken token, PackageDownload? download = null)
        {
            // 进度回调可能同步调用关闭；先公开本次任务，让关闭等待同一份原生操作。
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            initialization = completion.Task;
            resourceOperationStop = stop;
            CompleteModeInitializationAsync(parameters, stop, completion, download).Forget();
            return completion.Task;
        }

        private static async Task CompleteModeInitializationAsync(InitializeParameters parameters, CancellationTokenSource stop, TaskCompletionSource<bool> completion, PackageDownload? download)
        {
            try
            {
                completion.TrySetResult(await InitializeCoreAsync(parameters, stop.Token, download));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetResult(false);
            }
            catch (Exception error)
            {
                Logger.Exception(error);
                completion.TrySetResult(false);
            }
            finally
            {
                if (ReferenceEquals(resourceOperationStop, stop))
                {
                    resourceOperationStop = null;
                }
                stop.Dispose();
            }
        }

        private static void CancelResourceOperation()
        {
            try
            {
                resourceOperationStop?.Cancel();
            }
            catch (AggregateException error)
            {
                Logger.Exception(error);
            }
        }

        private static async Task<bool> WaitForResourceOperationAsync(Task operation, CancellationToken token)
        {
            if (!operation.IsCompleted && token.CanBeCanceled)
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (token.Register(() => cancelled.TrySetResult(true)))
                {
                    if (await Task.WhenAny(operation, cancelled.Task) != operation)
                    {
                        operation.Forget();
                        return false;
                    }
                }
            }
            try
            {
                await operation;
                return !token.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static async Task<bool> RetirePackageAsync(ResourcePackage current, int startedGeneration)
        {
            var destroy = current.DestroyAsync();
            await destroy.Task;
            if (startedGeneration != generation)
            {
                return false;
            }
            if (destroy.Status != EOperationStatus.Succeed || !YooAssets.RemovePackage(current))
            {
                Logger.Warn("Could not close DefaultPackage: " + destroy.Error);
                return false;
            }

            YooAssets.SetDefaultPackage(null);
            package = null;
            return true;
        }

        private static void ClearResourceMode()
        {
            resourceMode = null;
            downloadSource = null;
            DownloadProgress = null;
        }

        private static ResourceDownloadResult DownloadFailure(ResourceDownloadStatus status, string message)
        {
            Logger.Warn(message);
            return new ResourceDownloadResult(status);
        }

        internal static bool IsResourceIoFailure(Exception error) =>
            error is IOException || error is UnauthorizedAccessException || error is TimeoutException;
    }
}

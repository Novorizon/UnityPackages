using System;
using System.Reflection;
using System.Threading;
using Game.Resource;
using UnityEngine.U2D;
using YooAsset;

internal static partial class Program
{
    private static void ResourcePackageScenarios()
    {
        ResourceDownloadScenarios();

        Run("offline initialization keeps content unavailable until the manifest is mounted", () =>
        {
            var package = YooAssets.Package;
            var manifest = Gate<bool>();
            package.ManifestGate = manifest.Task;
            var loading = ResourceManager.InitializeAsync(ResourceMode.Offline);
            Equal(1, package.InitializeCalls);
            True(package.LastInitializeParameters is OfflinePlayModeParameters);
            Equal(1, package.ManifestRequests);
            True(!loading.IsCompleted && ResourceManager.PackageInitialized && !ResourceManager.IsReady);
            True(ResourceManager.PackageVersion == null);
            True(ResourceManager.CreateSceneLoader() == null);
            manifest.SetResult(true);
            True(Complete(loading));
            True(ResourceManager.PackageInitialized && ResourceManager.IsReady);
            Equal("1", ResourceManager.PackageVersion);
            Equal(0, package.DownloaderCalls);
        });
        Run("retrying an exact remote version reuses filesystems and waits for all downloads", () =>
        {
            var package = YooAssets.Package;
            var parameters = new HostPlayModeParameters();
            var setting = new RemoteDownloadSetting { Parameters = parameters, PackageVersion = "package-release-2" };
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Online)));
            package.FailManifest = true;
            Equal(ResourceDownloadStatus.Failed, Complete(ResourceManager.DownloadAsync(setting)).Status);
            package.FailManifest = false;
            var download = Gate<bool>();
            package.Downloader.Task = download.Task;
            var loading = ResourceManager.DownloadAsync(setting);
            Equal(1, package.InitializeCalls);
            Same(parameters, package.LastInitializeParameters);
            Equal(0, package.VersionRequests);
            Equal(2, package.ManifestRequests);
            Equal("package-release-2", package.SelectedVersion);
            Equal(1, package.DownloaderCalls);
            Equal(1, package.Downloader.Begun);
            True(!loading.IsCompleted && ResourceManager.PackageInitialized && !ResourceManager.IsReady);
            Equal(0, PackageAtlasSubscribers());
            download.SetResult(true);
            True(Complete(loading).IsReady);
            True(ResourceManager.PackageInitialized && ResourceManager.IsReady);
            Equal("package-release-2", ResourceManager.PackageVersion);
            Equal(1, PackageAtlasSubscribers());
            Equal(1, package.InitializeCalls);
        });
        Run("concurrent package initialization shares one unfinished native operation", () =>
        {
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            var first = ResourceManager.InitializeAsync(ResourceMode.Offline);
            var second = ResourceManager.InitializeAsync(ResourceMode.Offline);
            Equal(1, package.InitializeCalls);
            True(!first.IsCompleted && !second.IsCompleted && !ResourceManager.PackageInitialized);
            True(package.InitializeStatus != EOperationStatus.Succeed);
            pending.SetResult(true);
            True(Complete(first) && Complete(second));
            True(ResourceManager.PackageInitialized && ResourceManager.IsReady);
            Equal(1, package.InitializeCalls);
            Equal(1, package.ManifestRequests);
            Equal(0, package.DownloaderCalls);
        });
        Run("cancelling an offline waiter preserves the shared initialization", () =>
        {
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            using var stop = new CancellationTokenSource();
            var owner = ResourceManager.InitializeAsync(ResourceMode.Offline);
            var waiter = ResourceManager.InitializeAsync(ResourceMode.Offline, stop.Token);
            stop.Cancel();
            True(!Complete(waiter));
            True(!owner.IsCompleted && !ResourceManager.PackageInitialized);
            Equal(1, package.InitializeCalls);
            pending.SetResult(true);
            True(Complete(owner));
            True(ResourceManager.PackageInitialized && ResourceManager.IsReady);
            Equal(1, package.InitializeCalls);
            Equal(1, package.ManifestRequests);
            Equal(0, package.DownloaderCalls);
        });
        Run("shutdown retains cancelled package native work until completion without late readiness", () =>
        {
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            using var stop = new CancellationTokenSource();
            var loading = ResourceManager.InitializeAsync(ResourceMode.Offline, stop.Token);
            stop.Cancel();
            True(!Complete(loading));
            var shutdown = ResourceManager.ShutdownAsync();
            True(!shutdown.IsCompleted && !ResourceManager.PackageInitialized && !ResourceManager.Initialized);
            Equal(1, package.InitializeCalls);
            pending.SetResult(true);
            Complete(shutdown);
            context.Drain();
            True(!ResourceManager.PackageInitialized && !ResourceManager.Initialized && ResourceManager.PackageVersion == null);
            AssertNoPackageContentWork(package);
        });
        Run("package restart waits for shutdown before creating a new package", () =>
        {
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            var first = ResourceManager.InitializeAsync(ResourceMode.Offline);
            var shutdown = ResourceManager.ShutdownAsync();
            var restarting = ResourceManager.InitializeAsync(ResourceMode.Offline);
            True(!shutdown.IsCompleted && !restarting.IsCompleted);
            Equal(1, package.InitializeCalls);
            True(package.InitializeStatus != EOperationStatus.Succeed);
            pending.SetResult(true);
            Complete(shutdown);
            True(!Complete(first));
            True(Complete(restarting));
            True(ResourceManager.PackageInitialized && ResourceManager.IsReady);
            Equal(1, package.DestroyCalls);
            Equal(0, package.ManifestRequests);
            True(!ReferenceEquals(package, YooAssets.Package));
            Equal(1, YooAssets.Package.InitializeCalls);
            Equal(1, YooAssets.Package.ManifestRequests);
        });
        Run("failed native package initialization returns false and a later caller can retry", () =>
        {
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            package.FailNextInitialize = true;
            var loading = ResourceManager.InitializeAsync(ResourceMode.Offline);
            True(!loading.IsCompleted);
            pending.SetResult(true);
            True(!Complete(loading));
            True(!ResourceManager.PackageInitialized && !ResourceManager.Initialized);
            Equal(EOperationStatus.Failed, package.InitializeStatus);
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            Equal(2, package.InitializeCalls);
            True(ResourceManager.PackageInitialized && ResourceManager.IsReady);
            Equal(1, package.ManifestRequests);
            Equal(0, package.DownloaderCalls);
        });
        Run("play reset prevents an old package success from publishing before the new native operation", () =>
        {
            var oldPackage = YooAssets.Package;
            var oldNative = Gate<bool>();
            oldPackage.InitializeGate = oldNative.Task;
            var oldLoading = ResourceManager.InitializeAsync(ResourceMode.Offline);
            ResetResourcePackageForPlay();
            var newPackage = new ResourcePackage();
            var newNative = Gate<bool>();
            newPackage.InitializeGate = newNative.Task;
            YooAssets.Package = newPackage;
            var newLoading = ResourceManager.InitializeAsync(ResourceMode.Offline);
            Equal(1, oldPackage.InitializeCalls);
            Equal(0, newPackage.InitializeCalls);
            True(!newLoading.IsCompleted);
            oldNative.SetResult(true);
            True(!Complete(oldLoading));
            context.Until(() => newPackage.InitializeCalls == 1);
            True(!newLoading.IsCompleted && !ResourceManager.PackageInitialized && !ResourceManager.Initialized);
            newNative.SetResult(true);
            True(Complete(newLoading));
            True(ResourceManager.PackageInitialized && ResourceManager.IsReady);
            Equal(0, oldPackage.ManifestRequests);
            Equal(1, newPackage.ManifestRequests);
            Equal(0, newPackage.DownloaderCalls);
        });
        Run("play reset waits for an old package failure without inheriting it into new content readiness", () =>
        {
            var oldPackage = YooAssets.Package;
            var oldNative = Gate<bool>();
            oldPackage.InitializeGate = oldNative.Task;
            oldPackage.FailNextInitialize = true;
            var oldLoading = ResourceManager.InitializeAsync(ResourceMode.Offline);
            ResetResourcePackageForPlay();
            var newPackage = new ResourcePackage();
            YooAssets.Package = newPackage;
            var newLoading = ResourceManager.InitializeAsync(ResourceMode.Offline);
            True(!newLoading.IsCompleted);
            Equal(0, newPackage.InitializeCalls);
            oldNative.SetResult(true);
            True(!Complete(oldLoading));
            True(Complete(newLoading));
            True(ResourceManager.PackageInitialized && ResourceManager.Initialized);
            Equal("1", ResourceManager.PackageVersion);
            Equal(1, newPackage.InitializeCalls);
            Equal(1, PackageAtlasSubscribers());
            Equal(0, oldPackage.VersionRequests);
            Equal(0, oldPackage.ManifestRequests);
            Equal(0, oldPackage.DownloaderCalls);
        });
    }

    private static void ResourceDownloadScenarios()
    {
        Run("offline mode mounts the built-in manifest without invoking the downloader", () =>
        {
            var package = YooAssets.Package;
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            True(ResourceManager.IsReady && ResourceManager.PackageVersion == "1");
            True(package.LastInitializeParameters is OfflinePlayModeParameters);
            Equal(1, package.ManifestRequests);
            Equal(0, package.DownloaderCalls);
            True(ResourceManager.DownloadProgress == null);
            True(!Complete(ResourceManager.InitializeAsync(ResourceMode.Online)));
            Complete(ResourceManager.ShutdownAsync());
            Equal(1, package.DestroyCalls);
            True(YooAssets.Package == null && !ResourceManager.IsReady);
        });
        Run("online initialization leaves network work to explicit exact-version download", () =>
        {
            var package = YooAssets.Package;
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Online)));
            AssertNoPackageContentWork(package);
            Equal(0, package.InitializeCalls);
            True(!ResourceManager.IsReady && !ResourceManager.PackageInitialized);

            var gate = Gate<bool>();
            package.Downloader.Task = gate.Task;
            package.Downloader.TotalDownloadCount = 2;
            package.Downloader.TotalDownloadBytes = 512;
            var progress = new RecordingDownloadProgress();
            var options = new RemoteDownloadSetting
            {
                MainUrl = "https://example.test/releases/",
                FallbackUrl = "https://backup.example.test/releases",
                PackageVersion = "v2",
                ConcurrentDownloads = 3,
                FailedTryAgain = 1,
                Progress = progress
            };
            var downloading = ResourceManager.DownloadAsync(options);
            True(package.LastInitializeParameters is HostPlayModeParameters);
            Equal(0, package.VersionRequests);
            Equal("v2", package.SelectedVersion);
            Equal(3, package.DownloadConcurrency);
            Equal(1, package.DownloadRetries);
            Equal(2, ResourceManager.DownloadProgress.Value.TotalCount);
            True(!ResourceManager.IsReady && ResourceManager.PackageVersion == null);
            Equal(ResourceDownloadStatus.Busy, Complete(ResourceManager.DownloadAsync(options)).Status);
            gate.SetResult(true);
            var result = Complete(downloading);
            Equal(ResourceDownloadStatus.Downloaded, result.Status);
            True(result.IsReady && ResourceManager.IsReady);
            Equal("v2", result.PackageVersion);
            Equal(512L, progress.Value.CompletedBytes);
            Equal(512L, ResourceManager.DownloadProgress.Value.CompletedBytes);
            Equal(ResourceDownloadStatus.UpToDate, Complete(ResourceManager.DownloadAsync(options)).Status);
            options.PackageVersion = "v3";
            Equal(ResourceDownloadStatus.ResetRequired, Complete(ResourceManager.DownloadAsync(options)).Status);
            Equal(1, package.ManifestRequests);
        });
        Run("online version, manifest and download failures return results and can retry the same source", () =>
        {
            var package = YooAssets.Package;
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Online)));
            var options = new RemoteDownloadSetting { MainUrl = "invalid url" };
            Equal(ResourceDownloadStatus.Failed, Complete(ResourceManager.DownloadAsync(options)).Status);
            Equal(0, package.InitializeCalls);
            var host = new HostPlayModeParameters();
            options.Parameters = host;
            package.FailVersionRequest = true;
            Equal(ResourceDownloadStatus.Failed, Complete(ResourceManager.DownloadAsync(options)).Status);
            Equal(0, package.ManifestRequests);
            Same(host, package.LastInitializeParameters);
            package.FailVersionRequest = false;
            package.FailManifest = true;
            Equal(ResourceDownloadStatus.Failed, Complete(ResourceManager.DownloadAsync(options)).Status);
            Equal(0, package.DownloaderCalls);
            package.FailManifest = false;
            package.Downloader.Status = EOperationStatus.Failed;
            Equal(ResourceDownloadStatus.Failed, Complete(ResourceManager.DownloadAsync(options)).Status);
            True(!ResourceManager.IsReady && ResourceManager.PackageVersion == null);
            package.Downloader.Status = EOperationStatus.Succeed;
            var result = Complete(ResourceManager.DownloadAsync(options));
            True(result.IsReady && ResourceManager.IsReady);
            Equal(ResourceDownloadStatus.UpToDate, result.Status);
            Equal("1", result.PackageVersion);
        });
        Run("cancelled download returns a status and reset waits for native download and package destruction", () =>
        {
            var package = YooAssets.Package;
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Online)));
            var gate = Gate<bool>();
            var destroy = Gate<bool>();
            package.Downloader.Task = gate.Task;
            package.DestroyGate = destroy.Task;
            using var stop = new CancellationTokenSource();
            var options = new RemoteDownloadSetting { MainUrl = "https://example.test", PackageVersion = "remote" };
            var downloading = ResourceManager.DownloadAsync(options, stop.Token);
            stop.Cancel();
            Equal(ResourceDownloadStatus.Cancelled, Complete(downloading).Status);
            Equal(ResourceDownloadStatus.Busy, Complete(ResourceManager.DownloadAsync(options)).Status);
            var reset = ResourceManager.ResetAsync();
            True(!reset.IsCompleted && package.DestroyCalls == 0);
            gate.SetResult(true);
            context.Until(() => package.DestroyCalls == 1);
            True(!reset.IsCompleted && !ResourceManager.IsReady);
            destroy.SetResult(true);
            True(Complete(reset));
            Equal(1, package.Downloader.Cancelled);
            True(ResourceManager.PackageVersion == null && ResourceManager.DownloadProgress == null && YooAssets.Package == null);
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            True(!ReferenceEquals(package, YooAssets.Package));
            True(YooAssets.Package.LastInitializeParameters is OfflinePlayModeParameters);
        });
        Run("failed native reset leaves the package available for another reset attempt", () =>
        {
            var package = YooAssets.Package;
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Offline)));
            package.FailDestroy = true;
            True(!Complete(ResourceManager.ResetAsync()));
            True(!ResourceManager.IsReady);
            Same(package, YooAssets.Package);
            True(!Complete(ResourceManager.InitializeAsync(ResourceMode.Online)));
            package.FailDestroy = false;
            True(Complete(ResourceManager.ResetAsync()));
            Equal(2, package.DestroyCalls);
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Online)));
        });
        Run("reset cancels a download even when its caller did not request cancellation", () =>
        {
            var package = YooAssets.Package;
            True(Complete(ResourceManager.InitializeAsync(ResourceMode.Online)));
            var gate = Gate<bool>();
            package.Downloader.Task = gate.Task;
            var downloading = ResourceManager.DownloadAsync(new RemoteDownloadSetting { MainUrl = "https://example.test" });
            var reset = ResourceManager.ResetAsync();
            Equal(1, package.Downloader.Cancelled);
            True(!reset.IsCompleted && !ResourceManager.IsReady && package.DestroyCalls == 0);
            gate.SetResult(true);
            Equal(ResourceDownloadStatus.Cancelled, Complete(downloading).Status);
            True(Complete(reset));
            Equal(1, package.DestroyCalls);
        });
    }

    private sealed class RecordingDownloadProgress : IProgress<ResourceDownloadProgress>
    {
        public ResourceDownloadProgress Value { get; private set; }
        public void Report(ResourceDownloadProgress value) => Value = value;
    }

    private static void AssertNoPackageContentWork(ResourcePackage package)
    {
        Equal(0, package.VersionRequests);
        Equal(0, package.ManifestRequests);
        Equal(0, package.DownloaderCalls);
        Equal(0, package.Downloader.Begun);
        Equal(0, package.Assets.Count);
        Equal(0, PackageAtlasSubscribers());
    }

    private static int PackageAtlasSubscribers()
    {
        var callback = (Delegate)typeof(SpriteAtlasManager).GetField("atlasRequested", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return callback?.GetInvocationList().Length ?? 0;
    }

    private static void InvokeResourcePlayReset()
    {
        typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }

    private static void ResetResourcePackageForPlay()
    {
        InvokeResourcePlayReset();
        True(!ResourceManager.PackageInitialized && !ResourceManager.Initialized && ResourceManager.PackageVersion == null);
    }
}

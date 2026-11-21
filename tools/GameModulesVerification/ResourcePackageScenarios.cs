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
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var manifest = Gate<bool>();
            package.ManifestGate = manifest.Task;
            var loading = resources.InitializeAsync(ResourceMode.Offline);
            Equal(1, package.InitializeCalls);
            True(package.LastInitializeParameters is OfflinePlayModeParameters);
            Equal(1, package.ManifestRequests);
            True(!loading.IsCompleted && resources.PackageInitialized && !resources.IsReady);
            True(resources.PackageVersion == null);
            Throws<InvalidOperationException>(() => resources.CreateSceneLoader());
            manifest.SetResult(true);
            True(Complete(loading));
            True(resources.PackageInitialized && resources.IsReady);
            Equal("1", resources.PackageVersion);
            Equal(0, package.DownloaderCalls);
        });
        Run("retrying an exact remote version reuses filesystems and waits for all downloads", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var parameters = new HostPlayModeParameters();
            var setting = new RemoteDownloadSetting { Parameters = parameters, PackageVersion = "package-release-2" };
            True(Complete(resources.InitializeAsync(ResourceMode.Online)));
            package.FailManifest = true;
            Equal(ResourceDownloadStatus.Failed, Complete(resources.DownloadAsync(setting)).Status);
            package.FailManifest = false;
            var download = Gate<bool>();
            package.Downloader.Task = download.Task;
            var loading = resources.DownloadAsync(setting);
            Equal(1, package.InitializeCalls);
            Same(parameters, package.LastInitializeParameters);
            Equal(0, package.VersionRequests);
            Equal(2, package.ManifestRequests);
            Equal("package-release-2", package.SelectedVersion);
            Equal(1, package.DownloaderCalls);
            Equal(1, package.Downloader.Begun);
            True(!loading.IsCompleted && resources.PackageInitialized && !resources.IsReady);
            Equal(0, PackageAtlasSubscribers());
            download.SetResult(true);
            True(Complete(loading).IsReady);
            True(resources.PackageInitialized && resources.IsReady);
            Equal("package-release-2", resources.PackageVersion);
            Equal(1, PackageAtlasSubscribers());
            Equal(1, package.InitializeCalls);
        });
        Run("concurrent package initialization shares one unfinished native operation", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            var first = resources.InitializeAsync(ResourceMode.Offline);
            var second = resources.InitializeAsync(ResourceMode.Offline);
            Equal(1, package.InitializeCalls);
            True(!first.IsCompleted && !second.IsCompleted && !resources.PackageInitialized);
            True(package.InitializeStatus != EOperationStatus.Succeed);
            pending.SetResult(true);
            True(Complete(first) && Complete(second));
            True(resources.PackageInitialized && resources.IsReady);
            Equal(1, package.InitializeCalls);
            Equal(1, package.ManifestRequests);
            Equal(0, package.DownloaderCalls);
        });
        Run("cancelling an offline waiter preserves the shared initialization", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            using var stop = new CancellationTokenSource();
            var owner = resources.InitializeAsync(ResourceMode.Offline);
            var waiter = resources.InitializeAsync(ResourceMode.Offline, stop.Token);
            stop.Cancel();
            True(!Complete(waiter));
            True(!owner.IsCompleted && !resources.PackageInitialized);
            Equal(1, package.InitializeCalls);
            pending.SetResult(true);
            True(Complete(owner));
            True(resources.PackageInitialized && resources.IsReady);
            Equal(1, package.InitializeCalls);
            Equal(1, package.ManifestRequests);
            Equal(0, package.DownloaderCalls);
        });
        Run("shutdown retains cancelled package native work until completion without late readiness", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            using var stop = new CancellationTokenSource();
            var loading = resources.InitializeAsync(ResourceMode.Offline, stop.Token);
            stop.Cancel();
            True(!Complete(loading));
            var shutdown = resources.ShutdownAsync();
            True(!shutdown.IsCompleted && !resources.PackageInitialized && !resources.Initialized);
            Equal(1, package.InitializeCalls);
            pending.SetResult(true);
            Complete(shutdown);
            context.Drain();
            True(!resources.PackageInitialized && !resources.Initialized && resources.PackageVersion == null);
            AssertNoPackageContentWork(package);
        });
        Run("package restart waits for shutdown before creating a new package", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            var first = resources.InitializeAsync(ResourceMode.Offline);
            var shutdown = resources.ShutdownAsync();
            var restarting = resources.InitializeAsync(ResourceMode.Offline);
            True(!shutdown.IsCompleted && !restarting.IsCompleted);
            Equal(1, package.InitializeCalls);
            True(package.InitializeStatus != EOperationStatus.Succeed);
            pending.SetResult(true);
            Complete(shutdown);
            True(!Complete(first));
            True(Complete(restarting));
            True(resources.PackageInitialized && resources.IsReady);
            Equal(1, package.DestroyCalls);
            Equal(0, package.ManifestRequests);
            True(!ReferenceEquals(package, YooAssets.Package));
            Equal(1, YooAssets.Package.InitializeCalls);
            Equal(1, YooAssets.Package.ManifestRequests);
        });
        Run("failed native package initialization returns false and a later caller can retry", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            package.FailNextInitialize = true;
            var loading = resources.InitializeAsync(ResourceMode.Offline);
            True(!loading.IsCompleted);
            pending.SetResult(true);
            True(!Complete(loading));
            True(!resources.PackageInitialized && !resources.Initialized);
            Equal(EOperationStatus.Failed, package.InitializeStatus);
            True(Complete(resources.InitializeAsync(ResourceMode.Offline)));
            Equal(2, package.InitializeCalls);
            True(resources.PackageInitialized && resources.IsReady);
            Equal(1, package.ManifestRequests);
            Equal(0, package.DownloaderCalls);
        });
        Run("play reset prevents an old package success from publishing before the new native operation", () =>
        {
            var resources = ResourceManager.Instance;
            var oldPackage = YooAssets.Package;
            var oldNative = Gate<bool>();
            oldPackage.InitializeGate = oldNative.Task;
            var oldLoading = resources.InitializeAsync(ResourceMode.Offline);
            ResetResourcePackageForPlay(resources);
            var newPackage = new ResourcePackage();
            var newNative = Gate<bool>();
            newPackage.InitializeGate = newNative.Task;
            YooAssets.Package = newPackage;
            var newLoading = resources.InitializeAsync(ResourceMode.Offline);
            Equal(1, oldPackage.InitializeCalls);
            Equal(0, newPackage.InitializeCalls);
            True(!newLoading.IsCompleted);
            oldNative.SetResult(true);
            True(!Complete(oldLoading));
            context.Until(() => newPackage.InitializeCalls == 1);
            True(!newLoading.IsCompleted && !resources.PackageInitialized && !resources.Initialized);
            newNative.SetResult(true);
            True(Complete(newLoading));
            True(resources.PackageInitialized && resources.IsReady);
            Equal(0, oldPackage.ManifestRequests);
            Equal(1, newPackage.ManifestRequests);
            Equal(0, newPackage.DownloaderCalls);
        });
        Run("play reset waits for an old package failure without inheriting it into new content readiness", () =>
        {
            var resources = ResourceManager.Instance;
            var oldPackage = YooAssets.Package;
            var oldNative = Gate<bool>();
            oldPackage.InitializeGate = oldNative.Task;
            oldPackage.FailNextInitialize = true;
            var oldLoading = resources.InitializeAsync(ResourceMode.Offline);
            ResetResourcePackageForPlay(resources);
            var newPackage = new ResourcePackage();
            YooAssets.Package = newPackage;
            var newLoading = resources.InitializeAsync(ResourceMode.Offline);
            True(!newLoading.IsCompleted);
            Equal(0, newPackage.InitializeCalls);
            oldNative.SetResult(true);
            True(!Complete(oldLoading));
            True(Complete(newLoading));
            True(resources.PackageInitialized && resources.Initialized);
            Equal("1", resources.PackageVersion);
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
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            True(Complete(resources.InitializeAsync(ResourceMode.Offline)));
            True(resources.IsReady && resources.PackageVersion == "1");
            True(package.LastInitializeParameters is OfflinePlayModeParameters);
            Equal(1, package.ManifestRequests);
            Equal(0, package.DownloaderCalls);
            True(resources.DownloadProgress == null);
            True(!Complete(resources.InitializeAsync(ResourceMode.Online)));
            Complete(resources.ShutdownAsync());
            Equal(1, package.DestroyCalls);
            True(YooAssets.Package == null && !resources.IsReady);
        });
        Run("online initialization leaves network work to explicit exact-version download", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            True(Complete(resources.InitializeAsync(ResourceMode.Online)));
            AssertNoPackageContentWork(package);
            Equal(0, package.InitializeCalls);
            True(!resources.IsReady && !resources.PackageInitialized);

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
            var downloading = resources.DownloadAsync(options);
            True(package.LastInitializeParameters is HostPlayModeParameters);
            Equal(0, package.VersionRequests);
            Equal("v2", package.SelectedVersion);
            Equal(3, package.DownloadConcurrency);
            Equal(1, package.DownloadRetries);
            Equal(2, resources.DownloadProgress.Value.TotalCount);
            True(!resources.IsReady && resources.PackageVersion == null);
            Equal(ResourceDownloadStatus.Busy, Complete(resources.DownloadAsync(options)).Status);
            gate.SetResult(true);
            var result = Complete(downloading);
            Equal(ResourceDownloadStatus.Downloaded, result.Status);
            True(result.IsReady && resources.IsReady);
            Equal("v2", result.PackageVersion);
            Equal(512L, progress.Value.CompletedBytes);
            Equal(512L, resources.DownloadProgress.Value.CompletedBytes);
            Equal(ResourceDownloadStatus.UpToDate, Complete(resources.DownloadAsync(options)).Status);
            options.PackageVersion = "v3";
            Equal(ResourceDownloadStatus.ResetRequired, Complete(resources.DownloadAsync(options)).Status);
            Equal(1, package.ManifestRequests);
        });
        Run("online version, manifest and download failures return results and can retry the same source", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            True(Complete(resources.InitializeAsync(ResourceMode.Online)));
            var options = new RemoteDownloadSetting { MainUrl = "invalid url" };
            Equal(ResourceDownloadStatus.Failed, Complete(resources.DownloadAsync(options)).Status);
            Equal(0, package.InitializeCalls);
            var host = new HostPlayModeParameters();
            options.Parameters = host;
            package.FailVersionRequest = true;
            Equal(ResourceDownloadStatus.Failed, Complete(resources.DownloadAsync(options)).Status);
            Equal(0, package.ManifestRequests);
            Same(host, package.LastInitializeParameters);
            package.FailVersionRequest = false;
            package.FailManifest = true;
            Equal(ResourceDownloadStatus.Failed, Complete(resources.DownloadAsync(options)).Status);
            Equal(0, package.DownloaderCalls);
            package.FailManifest = false;
            package.Downloader.Status = EOperationStatus.Failed;
            Equal(ResourceDownloadStatus.Failed, Complete(resources.DownloadAsync(options)).Status);
            True(!resources.IsReady && resources.PackageVersion == null);
            package.Downloader.Status = EOperationStatus.Succeed;
            var result = Complete(resources.DownloadAsync(options));
            True(result.IsReady && resources.IsReady);
            Equal(ResourceDownloadStatus.UpToDate, result.Status);
            Equal("1", result.PackageVersion);
        });
        Run("cancelled download returns a status and reset waits for native download and package destruction", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            True(Complete(resources.InitializeAsync(ResourceMode.Online)));
            var gate = Gate<bool>();
            var destroy = Gate<bool>();
            package.Downloader.Task = gate.Task;
            package.DestroyGate = destroy.Task;
            using var stop = new CancellationTokenSource();
            var options = new RemoteDownloadSetting { MainUrl = "https://example.test", PackageVersion = "remote" };
            var downloading = resources.DownloadAsync(options, stop.Token);
            stop.Cancel();
            Equal(ResourceDownloadStatus.Cancelled, Complete(downloading).Status);
            Equal(ResourceDownloadStatus.Busy, Complete(resources.DownloadAsync(options)).Status);
            var reset = resources.ResetAsync();
            True(!reset.IsCompleted && package.DestroyCalls == 0);
            gate.SetResult(true);
            context.Until(() => package.DestroyCalls == 1);
            True(!reset.IsCompleted && !resources.IsReady);
            destroy.SetResult(true);
            True(Complete(reset));
            Equal(1, package.Downloader.Cancelled);
            True(resources.PackageVersion == null && resources.DownloadProgress == null && YooAssets.Package == null);
            True(Complete(resources.InitializeAsync(ResourceMode.Offline)));
            True(!ReferenceEquals(package, YooAssets.Package));
            True(YooAssets.Package.LastInitializeParameters is OfflinePlayModeParameters);
        });
        Run("failed native reset leaves the package available for another reset attempt", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            True(Complete(resources.InitializeAsync(ResourceMode.Offline)));
            package.FailDestroy = true;
            True(!Complete(resources.ResetAsync()));
            True(!resources.IsReady);
            Same(package, YooAssets.Package);
            True(!Complete(resources.InitializeAsync(ResourceMode.Online)));
            package.FailDestroy = false;
            True(Complete(resources.ResetAsync()));
            Equal(2, package.DestroyCalls);
            True(Complete(resources.InitializeAsync(ResourceMode.Online)));
        });
        Run("reset cancels a download even when its caller did not request cancellation", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            True(Complete(resources.InitializeAsync(ResourceMode.Online)));
            var gate = Gate<bool>();
            package.Downloader.Task = gate.Task;
            var downloading = resources.DownloadAsync(new RemoteDownloadSetting { MainUrl = "https://example.test" });
            var reset = resources.ResetAsync();
            Equal(1, package.Downloader.Cancelled);
            True(!reset.IsCompleted && !resources.IsReady && package.DestroyCalls == 0);
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

    private static void ResetResourcePackageForPlay(ResourceManager resources)
    {
        typeof(ResourceManager).GetMethod("ResetForPlay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(resources, null);
        True(!resources.PackageInitialized && !resources.Initialized && resources.PackageVersion == null);
    }
}

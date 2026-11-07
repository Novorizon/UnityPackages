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
        Run("package initialization prepares only filesystems and leaves content APIs unavailable", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            var parameters = new OfflinePlayModeParameters();
            package.InitializeGate = pending.Task;
            var loading = resources.InitializePackageAsync(parameters);
            Equal(1, package.InitializeCalls);
            Same(parameters, package.LastInitializeParameters);
            True(!loading.IsCompleted && !resources.PackageInitialized && !resources.Initialized);
            True(package.InitializeStatus != EOperationStatus.Succeed);
            AssertNoPackageContentWork(package);
            pending.SetResult(true);
            True(Complete(loading));
            True(resources.PackageInitialized && !resources.Initialized);
            True(resources.PackageVersion == null);
            Throws<InvalidOperationException>(() => resources.CreateSceneLoader());
            AssertNoPackageContentWork(package);
        });
        Run("exact content initialization reuses package filesystems and publishes readiness after downloads", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var parameters = new OfflinePlayModeParameters();
            True(Complete(resources.InitializePackageAsync(parameters)));
            AssertNoPackageContentWork(package);
            var download = Gate<bool>();
            package.Downloader.Task = download.Task;
            var loading = resources.PrepareContentAsync(new ResourceInitializationOptions
            {
                Parameters = parameters,
                PackageVersion = "package-release-2",
                DownloadAll = true
            });
            Equal(1, package.InitializeCalls);
            Equal(0, package.VersionRequests);
            Equal(1, package.ManifestRequests);
            Equal("package-release-2", package.SelectedVersion);
            Equal(1, package.DownloaderCalls);
            Equal(1, package.Downloader.Begun);
            True(!loading.IsCompleted && resources.PackageInitialized && !resources.Initialized);
            Equal(0, PackageAtlasSubscribers());
            download.SetResult(true);
            True(Complete(loading));
            True(resources.PackageInitialized && resources.Initialized);
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
            var first = resources.InitializePackageAsync(new OfflinePlayModeParameters());
            var second = resources.InitializePackageAsync(new OfflinePlayModeParameters());
            Equal(1, package.InitializeCalls);
            True(!first.IsCompleted && !second.IsCompleted && !resources.PackageInitialized);
            True(package.InitializeStatus != EOperationStatus.Succeed);
            pending.SetResult(true);
            True(Complete(first) && Complete(second));
            True(resources.PackageInitialized && !resources.Initialized);
            Equal(1, package.InitializeCalls);
            AssertNoPackageContentWork(package);
        });
        Run("package caller cancellation stops only its waiter and preserves shared native initialization", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            using var stop = new CancellationTokenSource();
            var first = resources.InitializePackageAsync(new OfflinePlayModeParameters(), stop.Token);
            var second = resources.InitializePackageAsync(new OfflinePlayModeParameters());
            stop.Cancel();
            Fails<OperationCanceledException>(first);
            True(!second.IsCompleted && !resources.PackageInitialized);
            Equal(1, package.InitializeCalls);
            pending.SetResult(true);
            True(Complete(second));
            True(resources.PackageInitialized && !resources.Initialized);
            Equal(1, package.InitializeCalls);
            AssertNoPackageContentWork(package);
        });
        Run("shutdown retains cancelled package native work until completion without late readiness", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            using var stop = new CancellationTokenSource();
            var loading = resources.InitializePackageAsync(new OfflinePlayModeParameters(), stop.Token);
            stop.Cancel();
            Fails<OperationCanceledException>(loading);
            var shutdown = resources.ShutdownAsync();
            True(!shutdown.IsCompleted && !resources.PackageInitialized && !resources.Initialized);
            Equal(1, package.InitializeCalls);
            pending.SetResult(true);
            Complete(shutdown);
            context.Drain();
            True(!resources.PackageInitialized && !resources.Initialized && resources.PackageVersion == null);
            AssertNoPackageContentWork(package);
        });
        Run("package restart waits for shutdown native completion before reusing its initialized filesystems", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            var first = resources.InitializePackageAsync(new OfflinePlayModeParameters());
            var shutdown = resources.ShutdownAsync();
            var restarting = resources.InitializePackageAsync(new OfflinePlayModeParameters());
            True(!shutdown.IsCompleted && !restarting.IsCompleted);
            Equal(1, package.InitializeCalls);
            True(package.InitializeStatus != EOperationStatus.Succeed);
            pending.SetResult(true);
            Complete(shutdown);
            True(!Complete(first));
            True(Complete(restarting));
            True(resources.PackageInitialized && !resources.Initialized);
            Equal(1, package.InitializeCalls);
            AssertNoPackageContentWork(package);
        });
        Run("failed native package initialization returns false and a later caller can retry", () =>
        {
            var resources = ResourceManager.Instance;
            var package = YooAssets.Package;
            var pending = Gate<bool>();
            package.InitializeGate = pending.Task;
            package.FailNextInitialize = true;
            var loading = resources.InitializePackageAsync(new OfflinePlayModeParameters());
            True(!loading.IsCompleted);
            pending.SetResult(true);
            True(!Complete(loading));
            True(!resources.PackageInitialized && !resources.Initialized);
            Equal(EOperationStatus.Failed, package.InitializeStatus);
            True(Complete(resources.InitializePackageAsync(new OfflinePlayModeParameters())));
            Equal(2, package.InitializeCalls);
            True(resources.PackageInitialized && !resources.Initialized);
            AssertNoPackageContentWork(package);
        });
        Run("play reset prevents an old package success from publishing before the new native operation", () =>
        {
            var resources = ResourceManager.Instance;
            var oldPackage = YooAssets.Package;
            var oldNative = Gate<bool>();
            oldPackage.InitializeGate = oldNative.Task;
            var oldLoading = resources.InitializePackageAsync(new OfflinePlayModeParameters());
            ResetResourcePackageForPlay(resources);
            var newPackage = new ResourcePackage();
            var newNative = Gate<bool>();
            newPackage.InitializeGate = newNative.Task;
            YooAssets.Package = newPackage;
            var newLoading = resources.InitializePackageAsync(new OfflinePlayModeParameters());
            Equal(1, oldPackage.InitializeCalls);
            Equal(0, newPackage.InitializeCalls);
            True(!newLoading.IsCompleted);
            oldNative.SetResult(true);
            True(!Complete(oldLoading));
            context.Until(() => newPackage.InitializeCalls == 1);
            True(!newLoading.IsCompleted && !resources.PackageInitialized && !resources.Initialized);
            newNative.SetResult(true);
            True(Complete(newLoading));
            True(resources.PackageInitialized && !resources.Initialized);
            AssertNoPackageContentWork(oldPackage);
            AssertNoPackageContentWork(newPackage);
        });
        Run("play reset waits for an old package failure without inheriting it into new content readiness", () =>
        {
            var resources = ResourceManager.Instance;
            var oldPackage = YooAssets.Package;
            var oldNative = Gate<bool>();
            oldPackage.InitializeGate = oldNative.Task;
            oldPackage.FailNextInitialize = true;
            var oldLoading = resources.InitializePackageAsync(new OfflinePlayModeParameters());
            ResetResourcePackageForPlay(resources);
            var newPackage = new ResourcePackage();
            YooAssets.Package = newPackage;
            var newLoading = resources.InitializeAsync(new ResourceInitializationOptions
            {
                Parameters = new OfflinePlayModeParameters(),
                PackageVersion = "new-play-release"
            });
            True(!newLoading.IsCompleted);
            Equal(0, newPackage.InitializeCalls);
            oldNative.SetResult(true);
            True(!Complete(oldLoading));
            True(Complete(newLoading));
            True(resources.PackageInitialized && resources.Initialized);
            Equal("new-play-release", resources.PackageVersion);
            Equal(1, newPackage.InitializeCalls);
            Equal(1, PackageAtlasSubscribers());
            Equal(0, oldPackage.VersionRequests);
            Equal(0, oldPackage.ManifestRequests);
            Equal(0, oldPackage.DownloaderCalls);
        });
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

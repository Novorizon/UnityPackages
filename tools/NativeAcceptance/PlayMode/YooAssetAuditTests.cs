using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Resource;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;

public sealed class YooAssetAuditTests
{
    [UnityTest]
    public IEnumerator CancellationAtDownloaderCreationStillCompletesItsRealNativeOperation()
    {
        bool ownDispatcher = !UnityThread.IsInitialized;
        bool ownYooAsset = !YooAssets.Initialized;
        if (ownDispatcher)
        {
            UnityThread.Initialize();
        }
        if (ownYooAsset)
        {
            YooAssets.Initialize();
        }
        try
        {
            // A real 2.3.18 empty operation needs no manifest, bundles, network or credentials.
            var create = typeof(ResourceDownloaderOperation).GetMethod("CreateEmptyDownloader", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(create, Is.Not.Null);
            var downloader = (ResourceDownloaderOperation)create.Invoke(null, new object[] { "audit", 1, 0 });
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            var run = typeof(ResourceManager).GetMethod("RunDownloadAsync", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(run, Is.Not.Null);
            Task operation = (Task)run.Invoke(null, new object[] { downloader, stop.Token });
            float deadline = Time.realtimeSinceStartup + 5;
            while (!operation.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "A cancelled downloader must have entered YooAsset's operation pump before retirement.");
                yield return null;
            }
            Assert.Catch<OperationCanceledException>(() => operation.GetAwaiter().GetResult());
            Assert.That(downloader.Task.IsCompleted, Is.True);
            Assert.That(downloader.Status, Is.EqualTo(EOperationStatus.Failed));
        }
        finally
        {
            if (ownYooAsset)
            {
                YooAssets.Destroy();
            }
            if (ownDispatcher)
            {
                UnityThread.Reset();
            }
        }
        yield return null;
    }
}

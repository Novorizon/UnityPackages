// Test-only API contracts checked against official YooAsset tag 2.3.18 (4b6a8ca).
// These types are excluded from every published package and do not emulate Unity asset loading.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace YooAsset
{
    public enum EOperationStatus
    {
        None,
        Succeed,
        Failed
    }

    public class InitializeParameters
    {
    }

    public sealed class EditorSimulateModeParameters : InitializeParameters
    {
        public FileSystemParameters EditorFileSystemParameters;
    }

    public sealed class OfflinePlayModeParameters : InitializeParameters
    {
        public FileSystemParameters BuildinFileSystemParameters;
    }

    public sealed class HostPlayModeParameters : InitializeParameters
    {
        public FileSystemParameters BuildinFileSystemParameters, CacheFileSystemParameters;
    }

    public sealed class CustomPlayModeParameters : InitializeParameters
    {
        public List<FileSystemParameters> FileSystemParameterList = new List<FileSystemParameters>();
    }

    public interface IRemoteServices
    {
        string GetRemoteMainURL(string name);
        string GetRemoteFallbackURL(string name);
    }

    public sealed class FileSystemParameters
    {
        public static FileSystemParameters CreateDefaultEditorFileSystemParameters(string path) => new FileSystemParameters();
        public static FileSystemParameters CreateDefaultBuildinFileSystemParameters() => new FileSystemParameters();
        public static FileSystemParameters CreateDefaultCacheFileSystemParameters(IRemoteServices services, object decryptionServices = null, string packageRoot = null) => new FileSystemParameters();
    }

    public sealed class PackageInvokeBuildResult
    {
        public string PackageRootDirectory = "test-root";
    }

    public static class EditorSimulateModeHelper
    {
        public static PackageInvokeBuildResult SimulateBuild(string name) => new PackageInvokeBuildResult();
    }

    public class Operation
    {
        public Task Task = System.Threading.Tasks.Task.CompletedTask;
        public EOperationStatus Status = EOperationStatus.Succeed;
        public string Error = "";
        public string PackageVersion = "1";
    }

    public sealed class UnloadSceneOperation : Operation
    {
    }

    public sealed class AssetHandle
    {
        public bool IsValid = true;
        public UnityEngine.Object AssetObject;
        public Task Task = System.Threading.Tasks.Task.CompletedTask;
        public EOperationStatus Status = EOperationStatus.Succeed;
        public string LastError = "test asset failure";
        public int ReleaseCount;
        public void Release()
        {
            ReleaseCount++;
            IsValid = false;
        }
    }

    public sealed class SceneHandle
    {
        public float Progress => Task.IsCompleted ? 1 : 0.5f;

        public bool IsValid = true;
        public Task Task = System.Threading.Tasks.Task.CompletedTask;
        public Scene SceneObject;
        public EOperationStatus Status = EOperationStatus.Succeed;
        public string LastError = "test scene failure";
        public int ReleaseCount, UnloadCount;
        public bool FailUnload;
        public Task UnloadGate = System.Threading.Tasks.Task.CompletedTask;
        public void Release()
        {
            ReleaseCount++;
            IsValid = false;
        }

        public UnloadSceneOperation UnloadAsync()
        {
            UnloadCount++;
            return new UnloadSceneOperation
            {
                Task = CompleteUnloadAsync(),
                Status = FailUnload ? EOperationStatus.Failed : EOperationStatus.Succeed
            };
        }
        private async Task CompleteUnloadAsync()
        {
            await UnloadGate;
            if (!FailUnload)
            {
                UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(SceneObject);
            }
        }
    }

    public struct DownloadUpdateData
    {
        public int TotalDownloadCount, CurrentDownloadCount;
        public long TotalDownloadBytes, CurrentDownloadBytes;
    }

    public sealed class ResourceDownloaderOperation : Operation
    {
        public int TotalDownloadCount;
        public long TotalDownloadBytes;
        public int Begun, Cancelled;
        public Action<DownloadUpdateData> DownloadUpdateCallback;
        public void BeginDownload()
        {
            Begun++;
            DownloadUpdateCallback?.Invoke(new DownloadUpdateData
            {
                TotalDownloadCount = TotalDownloadCount,
                TotalDownloadBytes = TotalDownloadBytes
            });
        }

        public void CancelDownload()
        {
            Cancelled++;
        }
    }

    public sealed class ResourcePackage
    {
        public ResourcePackage()
        {
            // Mirrors YooAsset 2.3.18 ResourceManager.OnSceneUnloaded ownership, not native I/O.
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += scene =>
            {
                foreach (var handle in Scenes)
                {
                    if (handle.IsValid && handle.SceneObject.handle == scene.handle)
                    {
                        handle.Release();
                    }
                }
            };
        }

        public readonly List<AssetHandle> Assets = new List<AssetHandle>();
        private readonly Dictionary<string, UnityEngine.Object> values = new Dictionary<string, UnityEngine.Object>();
        public readonly List<SceneHandle> Scenes = new List<SceneHandle>();
        public Task AssetGate = Task.CompletedTask, SceneGate = Task.CompletedTask;
        public Task InitializeGate = Task.CompletedTask;
        public EOperationStatus InitializeStatus;
        public bool FailNextAsset, FailNextScene;
        public int InitializeCalls, VersionRequests;
        public string SelectedVersion;
        public ResourceDownloaderOperation Downloader = new ResourceDownloaderOperation();
        public ResourceDownloaderOperation CreateResourceDownloader(int concurrency, int retries) => Downloader;
        public Operation InitializeAsync(InitializeParameters parameters)
        {
            InitializeCalls++;
            InitializeStatus = EOperationStatus.Succeed;
            return new Operation { Task = InitializeGate };
        }

        public Operation RequestPackageVersionAsync()
        {
            VersionRequests++;
            return new Operation();
        }
        public Operation UpdatePackageManifestAsync(string version)
        {
            SelectedVersion = version;
            return new Operation();
        }
        public AssetHandle LoadAssetAsync<T>(string location)
            where T : UnityEngine.Object
        {
            if (!values.TryGetValue(location, out var value))
            {
                value = typeof(T) == typeof(GameObject) ? new GameObject(location) : (UnityEngine.Object)Activator.CreateInstance(typeof(T));
                values.Add(location, value);
            }

            var handle = new AssetHandle
            {
                Task = AssetGate,
                AssetObject = value,
                Status = FailNextAsset ? EOperationStatus.Failed : EOperationStatus.Succeed
            };
            FailNextAsset = false;
            Assets.Add(handle);
            return handle;
        }

        public AssetHandle LoadAssetSync<T>(string location)
            where T : UnityEngine.Object => LoadAssetAsync<T>(location);
        public SceneHandle LoadSceneAsync(string location, LoadSceneMode mode)
        {
            var handle = new SceneHandle
            {
                Task = SceneGate,
                SceneObject = new Scene
                {
                    handle = Scenes.Count + 1,
                    name = "same-name",
                    isLoaded = true
                },
                Status = FailNextScene ? EOperationStatus.Failed : EOperationStatus.Succeed
            };
            FailNextScene = false;
            Scenes.Add(handle);
            return handle;
        }

        public bool CheckLocationValid(string location) => true;
        public bool IsNeedDownloadFromRemote(string location) => false;
        public void TryUnloadUnusedAsset(string location, int loopCount)
        {
        }

        public Operation UnloadUnusedAssetsAsync(int loopCount) => new Operation();
    }

    public static class YooAssets
    {
        public static ResourcePackage Package = new ResourcePackage();
        public static void Initialize()
        {
        }

        public static ResourcePackage TryGetPackage(string name) => Package;
        public static ResourcePackage CreatePackage(string name) => Package;
        public static void SetDefaultPackage(ResourcePackage package)
        {
        }
    }
}

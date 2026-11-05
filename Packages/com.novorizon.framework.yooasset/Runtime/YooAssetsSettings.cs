///------------------------------------
/// Author：guanjinbiao
/// Mail：novogooglor@gmail.com
/// Date：2025-12-11
/// Description：YooAssets 配置
///------------------------------------

using System;
using System.IO;
using UnityEngine;
using YooAsset;

namespace Game.Resource
{
    public static class YooAssetsSettings
    {
        public static string PadInstallTimePath = "InstallTimeAssets";
        //public static string YooAssetPath = Path.Combine(Application.persistentDataPath, PadInstallTimePath);

        public static string YooCache = "YooCache";
        public static string YooCachePath = Path.Combine(Application.persistentDataPath, YooCache);
        public static string DefaultHostServer = "";
        public static string FallbackHostServer = "";
        public static InitializeParameters CreateParameters()
        {
#if UNITY_EDITOR
            return CreateEditorSimulateModeParameters();

#elif OFFLINE_PLAY_MODE
            return CreateOfflinePlayModeParameters();

#elif HOST_PLAY_MODE
            return CreateHostPlayModeParameters();
#else
            return CreateOfflinePlayModeParameters();
#endif
        }

        public static InitializeParameters CreateEditorSimulateModeParameters()
        {
            PackageInvokeBuildResult buildResult = EditorSimulateModeHelper.SimulateBuild("DefaultPackage");
            string packageRoot = buildResult.PackageRootDirectory;
            FileSystemParameters fileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);

            EditorSimulateModeParameters parameters = new EditorSimulateModeParameters();
            parameters.EditorFileSystemParameters = fileSystemParameters;

            return parameters;
        }

        public static InitializeParameters CreateOfflinePlayModeParameters()
        {
            FileSystemParameters fileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            OfflinePlayModeParameters parameters = new OfflinePlayModeParameters();
            parameters.BuildinFileSystemParameters = fileSystemParameters;


            return parameters;
        }

        public static InitializeParameters CreateHostPlayModeParameters()
        {
            ValidateRemoteUrls();
            IRemoteServices remoteServices = new RemoteServices(DefaultHostServer.TrimEnd('/'), string.IsNullOrWhiteSpace(FallbackHostServer) ? DefaultHostServer.TrimEnd('/') : FallbackHostServer.TrimEnd('/'));
            FileSystemParameters cacheFileSystemParams = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, null, YooCachePath);
            FileSystemParameters buildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();

            HostPlayModeParameters parameters = new HostPlayModeParameters();
            parameters.BuildinFileSystemParameters = buildinFileSystemParams;
            parameters.CacheFileSystemParameters = cacheFileSystemParams;

            return parameters;
        }

        public static InitializeParameters CreateCustomPlayModeParameters()
        {
            ValidateRemoteUrls();
            IRemoteServices remoteServices = new RemoteServices(DefaultHostServer.TrimEnd('/'), string.IsNullOrWhiteSpace(FallbackHostServer) ? DefaultHostServer.TrimEnd('/') : FallbackHostServer.TrimEnd('/'));
            FileSystemParameters buildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            FileSystemParameters cacheSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, null, YooCachePath);
            //FileSystemParameters cacheSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(null, YooCachePath);

            CustomPlayModeParameters parameters = new CustomPlayModeParameters();
            parameters.FileSystemParameterList.Add(buildinFileSystemParams);
            parameters.FileSystemParameterList.Add(cacheSystemParameters);


            return parameters;
        }
        /// <summary>
        /// 远端资源地址查询服务类
        /// </summary>
        private static void ValidateRemoteUrls()
        {
            foreach (string url in new[] { DefaultHostServer, string.IsNullOrWhiteSpace(FallbackHostServer) ? DefaultHostServer : FallbackHostServer })
                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || (parsed.Scheme != "http" && parsed.Scheme != "https"))
                    throw new InvalidOperationException("Configure YooAssetsSettings.DefaultHostServer before enabling remote play mode.");
        }
        private class RemoteServices : IRemoteServices
        {
            private readonly string _defaultHostServer;
            private readonly string _fallbackHostServer;

            public RemoteServices(string defaultHostServer, string fallbackHostServer)
            {
                _defaultHostServer = defaultHostServer;
                _fallbackHostServer = fallbackHostServer;
            }
            string IRemoteServices.GetRemoteMainURL(string fileName)
            {
                return $"{_defaultHostServer}/{fileName}";
            }
            string IRemoteServices.GetRemoteFallbackURL(string fileName)
            {
                return $"{_fallbackHostServer}/{fileName}";
            }
        }
        private static bool NeedDownloadRemoteResources()
        {
            // 可根据条件判断：平台、渠道、大版本号……
            return true; // 启动时下载一部分资源 → 使用 Host 模式
        }
    }

}

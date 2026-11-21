///------------------------------------
/// Author：guanjinbiao
/// Mail：novogooglor@gmail.com
/// Date：2025-12-11
/// Description：YooAssets 配置
///------------------------------------

using System;
using Logger = Game.Framework.Logger;
using System.IO;
using System.Reflection;
using UnityEngine;
using YooAsset;

namespace Game.Resource
{
    /// <summary>Yoo 资源对应的设置。保存本模块使用的配置。</summary>
    public static class YooAssetsSettings
    {
        public static string PadInstallTimePath = "InstallTimeAssets";
        //public static string YooAssetPath = Path.Combine(Application.persistentDataPath, PadInstallTimePath);

        public static string YooCache = "YooCache";
        private static string yooCachePath;
        public static string YooCachePath
        {
            get => yooCachePath ?? (Game.Framework.UnityThread.IsMainThread ? Path.Combine(Application.persistentDataPath, YooCache) : null);
            set => yooCachePath = value;
        }

        public static string DefaultHostServer = "";
        public static string FallbackHostServer = "";
        /// <summary>创建参数。</summary>
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

        /// <summary>创建编辑器 Simulate 模式参数。</summary>
        public static InitializeParameters CreateEditorSimulateModeParameters()
        {
#if UNITY_EDITOR
            if (!ResourceManager.CheckMainThread(nameof(CreateEditorSimulateModeParameters)))
            {
                return null;
            }

            try
            {
                var build = EditorSimulateModeHelper.SimulateBuild("DefaultPackage");
                if (!string.IsNullOrWhiteSpace(build?.PackageRootDirectory))
                {
                    return new EditorSimulateModeParameters
                    {
                        EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(build.PackageRootDirectory)
                    };
                }
            }
            catch (TargetInvocationException error)
            {
                Logger.Warn("YooAsset simulation build failed: " + error.GetBaseException().Message);
                return null;
            }
            catch (Exception error) when (ResourceManager.IsResourceIoFailure(error) || error is TypeLoadException)
            {
                Logger.Warn("YooAsset simulation build failed: " + error.Message);
                return null;
            }
            Logger.Error("YooAsset could not build the simulated DefaultPackage.");
#else
            Logger.Warn("Resource simulation requires the Unity Editor.");
#endif
            return null;
        }

        /// <summary>创建 OfflinePlay 模式参数。</summary>
        public static InitializeParameters CreateOfflinePlayModeParameters()
        {
            FileSystemParameters fileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            OfflinePlayModeParameters parameters = new OfflinePlayModeParameters();
            parameters.BuildinFileSystemParameters = fileSystemParameters;


            return parameters;
        }

        /// <summary>创建宿主 Play Mode 参数。</summary>
        public static InitializeParameters CreateHostPlayModeParameters()
        {
            return CreateHostPlayModeParameters(DefaultHostServer, FallbackHostServer);
        }

        /// <summary>创建宿主 Play Mode 参数。</summary>
        public static InitializeParameters CreateHostPlayModeParameters(string mainUrl, string fallbackUrl = null)
        {
            fallbackUrl = string.IsNullOrWhiteSpace(fallbackUrl) ? mainUrl : fallbackUrl;
            if (!CheckRemoteUrls(mainUrl, fallbackUrl))
            {
                return null;
            }
            IRemoteServices remoteServices = new RemoteServices(mainUrl.TrimEnd('/'), fallbackUrl.TrimEnd('/'));
            return new HostPlayModeParameters
            {
                BuildinFileSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(),
                CacheFileSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, null, YooCachePath)
            };
        }

        /// <summary>创建 CustomPlay 模式参数。</summary>
        public static InitializeParameters CreateCustomPlayModeParameters()
        {
            string fallback = string.IsNullOrWhiteSpace(FallbackHostServer) ? DefaultHostServer : FallbackHostServer;
            if (!CheckRemoteUrls(DefaultHostServer, fallback))
            {
                return null;
            }
            IRemoteServices remoteServices = new RemoteServices(DefaultHostServer.TrimEnd('/'), fallback.TrimEnd('/'));
            FileSystemParameters buildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
            FileSystemParameters cacheSystemParameters = FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices, null, YooCachePath);
            //FileSystemParameters cacheSystemParameters = FileSystemParameters.CreateDefaultBuildinFileSystemParameters(null, YooCachePath);

            CustomPlayModeParameters parameters = new CustomPlayModeParameters();
            parameters.FileSystemParameterList.Add(buildinFileSystemParams);
            parameters.FileSystemParameterList.Add(cacheSystemParameters);


            return parameters;
        }
        private static bool CheckRemoteUrls(string mainUrl, string fallbackUrl)
        {
            if (IsRemoteUrl(mainUrl) && IsRemoteUrl(fallbackUrl))
            {
                return true;
            }
            Logger.Error("Configure valid resource host URLs before enabling remote play mode.");
            return false;
        }

        internal static bool IsRemoteUrl(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var parsed) && !string.IsNullOrEmpty(parsed.Host) &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
        /// <summary>表示Yoo 资源设置中的远程服务。</summary>
        private class RemoteServices : IRemoteServices
        {
            private readonly string _defaultHostServer;
            private readonly string _fallbackHostServer;

            /// <summary>使用调用方提供的defaultHostServer、fallbackHostServer创建 RemoteServices。</summary>
            public RemoteServices(string defaultHostServer, string fallbackHostServer)
            {
                _defaultHostServer = defaultHostServer;
                _fallbackHostServer = fallbackHostServer;
            }
            /// <summary>获取远程主 URL。</summary>
            string IRemoteServices.GetRemoteMainURL(string fileName)
            {
                return $"{_defaultHostServer}/{fileName}";
            }
            /// <summary>获取远程回退项 URL。</summary>
            string IRemoteServices.GetRemoteFallbackURL(string fileName)
            {
                return $"{_fallbackHostServer}/{fileName}";
            }
        }
        /// <summary>返回NeedDownload 远程资源的判断结果。</summary>
        private static bool NeedDownloadRemoteResources()
        {
            // 可根据条件判断：平台、渠道、大版本号……
            return true; // 启动时下载一部分资源 → 使用 Host 模式
        }
    }

}

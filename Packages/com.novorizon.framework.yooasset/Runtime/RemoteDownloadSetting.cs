using System;
using YooAsset;

namespace Game.Resource
{
    /// <summary>选择 DefaultPackage 的资源来源。</summary>
    public enum ResourceMode
    {
        Offline,
        Online,
        EditorSimulation
    }

    /// <summary>指定在线资源的文件系统、版本和下载配置。</summary>
    public sealed class RemoteDownloadSetting
    {
        /// <summary>提供时使用调用方的 YooAsset 文件系统参数。</summary>
        public InitializeParameters Parameters;
        public string MainUrl;
        public string FallbackUrl;
        /// <summary>为空时查询该来源的最新版本，否则直接加载指定版本。</summary>
        public string PackageVersion;
        public int ConcurrentDownloads = 4;
        public int FailedTryAgain = 2;
        public IProgress<ResourceDownloadProgress> Progress;
    }

    public enum ResourceDownloadStatus
    {
        Failed,
        Downloaded,
        UpToDate,
        Cancelled,
        Busy,
        ResetRequired
    }

    /// <summary>资源操作的结果。成功时带有实际可用的版本。</summary>
    public readonly struct ResourceDownloadResult
    {
        public ResourceDownloadStatus Status { get; }
        public string PackageVersion { get; }
        public bool IsReady => Status == ResourceDownloadStatus.Downloaded || Status == ResourceDownloadStatus.UpToDate;

        internal ResourceDownloadResult(ResourceDownloadStatus status, string version = null)
        {
            Status = status;
            PackageVersion = version;
        }
    }
}

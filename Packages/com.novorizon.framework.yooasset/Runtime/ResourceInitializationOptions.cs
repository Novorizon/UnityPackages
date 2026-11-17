using System;
using YooAsset;

namespace Game.Resource
{
    /// <summary>可选的精确资源版本；提供时不查询远端 latest/version 文件。</summary>
    public sealed class ResourceInitializationOptions
    {
        public InitializeParameters Parameters;
        public string PackageVersion;
        public bool DownloadAll;
        public int ConcurrentDownloads = 4;
        public int FailedTryAgain = 2;
        public IProgress<ResourceDownloadProgress> Progress;
    }

    /// <summary>保存资源 Download 进度所需的数据与配套操作。</summary>
    public readonly struct ResourceDownloadProgress
    {
        public readonly int TotalCount;
        public readonly int CompletedCount;
        public readonly long TotalBytes;
        public readonly long CompletedBytes;
        public float Fraction => TotalBytes == 0 ? 1 : (float)CompletedBytes / TotalBytes;

        /// <summary>使用调用方提供的totalCount、completedCount、totalBytes创建 ResourceDownloadProgress。</summary>
        public ResourceDownloadProgress(int totalCount, int completedCount, long totalBytes, long completedBytes)
        {
            TotalCount = totalCount;
            CompletedCount = completedCount;
            TotalBytes = totalBytes;
            CompletedBytes = completedBytes;
        }
    }
}

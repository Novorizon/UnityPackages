namespace Game.Resource
{
    /// <summary>记录缺失资源下载的文件数和字节数。</summary>
    public readonly struct ResourceDownloadProgress
    {
        public readonly int TotalCount;
        public readonly int CompletedCount;
        public readonly long TotalBytes;
        public readonly long CompletedBytes;
        public float Fraction => TotalBytes == 0 ? 1 : (float)CompletedBytes / TotalBytes;

        public ResourceDownloadProgress(int totalCount, int completedCount, long totalBytes, long completedBytes)
        {
            TotalCount = totalCount;
            CompletedCount = completedCount;
            TotalBytes = totalBytes;
            CompletedBytes = completedBytes;
        }
    }
}

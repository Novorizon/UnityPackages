using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;

namespace Game.Framework.HotUpdate
{
    /// <summary>定义更新传输层的调用契约。</summary>
    internal interface IUpdateTransport
    {
        /// <summary>读取字节数据。</summary>
        Task<byte[]> ReadBytes(string url, long limit, CancellationToken token);
        /// <summary>读取文本。</summary>
        Task<string> ReadText(string url, CancellationToken token);
        /// <summary>下载已校验。</summary>
        Task DownloadVerified(Uri url, string destination, ReleaseFile file, CancellationToken token);
    }
}

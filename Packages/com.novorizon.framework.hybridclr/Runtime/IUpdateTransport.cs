using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;

namespace Game.Framework.HotUpdate
{
    internal interface IUpdateTransport
    {
        Task<byte[]> ReadBytes(string url, long limit, CancellationToken token);
        Task<string> ReadText(string url, CancellationToken token);
        Task DownloadVerified(Uri url, string destination, ReleaseFile file, CancellationToken token);
    }
}

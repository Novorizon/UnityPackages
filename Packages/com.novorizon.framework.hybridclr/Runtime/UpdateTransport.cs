using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;
using UnityEngine.Networking;

namespace Game.Framework.HotUpdate
{
    internal sealed class UpdateTransport : IUpdateTransport
    {
        private readonly UpdateSettings settings;
        public UpdateTransport(UpdateSettings settings) { this.settings = settings; }

        private sealed class BoundedHandler : DownloadHandlerScript
        {
            private readonly Stream destination;
            private readonly long limit;
            public long Received { get; private set; }
            public bool Overflow { get; private set; }
            public BoundedHandler(Stream destination, long limit) : base(new byte[64 * 1024])
            { this.destination = destination; this.limit = limit; }
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (data == null || length == 0) return true;
                if (Received + length > limit) { Overflow = true; return false; }
                destination.Write(data, 0, length);
                Received += length;
                return true;
            }
        }

        private async Task Fetch(string url, Stream destination, long limit, CancellationToken token)
        {
            using (var handler = new BoundedHandler(destination, limit))
            using (var request = new UnityWebRequest(url, "GET", handler, null))
            {
                request.disposeDownloadHandlerOnDispose = false;
                request.timeout = settings.requestTimeoutSeconds;
                request.redirectLimit = 0; // Never follow an HTTPS redirect to an unapproved endpoint.
                var operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    if (token.IsCancellationRequested) { request.Abort(); token.ThrowIfCancellationRequested(); }
                    await Task.Yield();
                }
                token.ThrowIfCancellationRequested();
                ReleasePolicy.Require(!handler.Overflow, "Download exceeded declared size limit.");
                if (request.result != UnityWebRequest.Result.Success)
                    throw new IOException("Download failed (HTTP " + request.responseCode + "): " + request.error);
            }
        }

        public async Task<byte[]> ReadBytes(string url, long limit, CancellationToken token)
        {
            using (var stream = new MemoryStream())
            {
                await Fetch(url, stream, limit, token);
                return stream.ToArray();
            }
        }

        public async Task<string> ReadText(string url, CancellationToken token) =>
            Encoding.UTF8.GetString(await ReadBytes(url, ReleasePolicy.MaxManifestBytes, token));

        public async Task DownloadVerified(Uri url, string destination, ReleaseFile file, CancellationToken token)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await Fetch(url.AbsoluteUri, stream, file.size, token);
                        stream.Flush(true);
                    }
                    // Hashing large files must not stall the Unity main thread.
                    await Task.Run(() => ReleasePolicy.VerifyFile(destination, file), token);
                    return;
                }
                catch (OperationCanceledException) { throw; }
                catch when (attempt + 1 < settings.requestAttempts)
                {
                    await Task.Delay(500 * (attempt + 1), token);
                }
            }
        }
    }
}

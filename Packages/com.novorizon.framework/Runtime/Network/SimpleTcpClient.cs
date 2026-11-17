using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Security;
using Google.Protobuf;

namespace Game.Framework.Network
{
    // Transport only: each connection owns its queue, cipher, CTS and loop tasks.
    /// <summary>提供简易 TCP 客户端的实现。</summary>
    public sealed class SimpleTcpClient : INetworkTransport
    {
        private readonly object sync = new object();
        private readonly TcpClientOptions options;
        private readonly SemaphoreSlim connectGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private TcpConnection current;
        private CancellationTokenSource connecting;
        private TcpClient connectingSocket;
        private long generation;
        private Task lastCompletion = Task.CompletedTask;
        private bool disposed;
        private Task disposeTask;
        /// <summary>使用调用方提供的options创建 SimpleTcpClient。</summary>
        public SimpleTcpClient(TcpClientOptions options) { this.options = (options ?? throw new ArgumentNullException(nameof(options))).Snapshot(); }
        public bool IsConnected { get { lock (sync) return !disposed && current != null && current.IsOpen; } }
        public long ConnectionId { get { lock (sync) return current?.Generation ?? generation; } }
        public Task ConnectionCompletion { get { lock (sync) return lastCompletion; } }
        public int PendingSendCount { get { lock (sync) return current?.PendingSendCount ?? 0; } }
        public int PendingSendBytes { get { lock (sync) return current?.PendingSendBytes ?? 0; } }
        // Transport events execute on I/O threads; NetworkManager provides main-thread dispatch.
        public event Action<long, NetworkPacket> PacketReceived;
        public event Action<long, Exception> Closed;

        /// <summary>异步连接简易 TCP 客户端。</summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            lock (sync) { if (disposed) throw new ObjectDisposedException(nameof(SimpleTcpClient)); }
            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, ct))
            {
                operation.CancelAfter(options.ConnectTimeoutMilliseconds);
                await connectGate.WaitAsync(operation.Token).ConfigureAwait(false);
                TcpClient socket = null; PacketCipher cipher = null;
                try
                {
                    lock (sync)
                    {
                        if (disposed) throw new ObjectDisposedException(nameof(SimpleTcpClient));
                        if (current != null && current.IsOpen) return;
                        connecting = operation;
                    }
                    Task previous;
                    lock (sync) previous = lastCompletion;
                    await AsyncOperation.WaitAsync(previous, operation.Token).ConfigureAwait(false);
                    IPAddress[] addresses = await AsyncOperation.WaitAsync(Dns.GetHostAddressesAsync(options.Host), operation.Token).ConfigureAwait(false);
                    if (addresses.Length == 0) throw new IOException("DNS returned no usable addresses.");
                    for (int index = 0; index < addresses.Length; index++)
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        var attempt = new TcpClient(addresses[index].AddressFamily) { NoDelay = true };
                        socket = attempt;
                        lock (sync) connectingSocket = attempt;
                        try
                        {
                            using (var addressDeadline = CancellationTokenSource.CreateLinkedTokenSource(operation.Token))
                            {
                                addressDeadline.CancelAfter(Math.Min(2000, options.ConnectTimeoutMilliseconds));
                                using (addressDeadline.Token.Register(() => attempt.Dispose()))
                                    await AsyncOperation.WaitAsync(attempt.ConnectAsync(addresses[index], options.Port), addressDeadline.Token).ConfigureAwait(false);
                            }
                            break;
                        }
                        catch (Exception error) when (!operation.IsCancellationRequested && index + 1 < addresses.Length &&
                            (error is SocketException || error is OperationCanceledException || error is ObjectDisposedException))
                        { attempt.Dispose(); socket = null; }
                    }
                    NetworkStream stream = socket.GetStream();
                    if (options.Encryption == PacketEncryptionMode.AesGcm)
                    {
                        byte[] hello = new byte[PacketCipher.HelloBytes];
                        var handshakeSocket = socket;
                        using (operation.Token.Register(() => handshakeSocket.Dispose()))
                            await TcpConnection.ReadExactlyAsync(stream, hello, 0, hello.Length, operation.Token).ConfigureAwait(false);
                        byte[] key = Convert.FromBase64String(options.KeyBase64);
                        try { cipher = new PacketCipher(key, hello); }
                        finally { Array.Clear(key, 0, key.Length); }
                    }
                    operation.Token.ThrowIfCancellationRequested();
                    TcpConnection session;
                    lock (sync)
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        if (disposed) throw new ObjectDisposedException(nameof(SimpleTcpClient));
                        session = new TcpConnection(++generation, socket, stream, cipher, options, OnPacket, OnClosed);
                        current = session; lastCompletion = session.Completion;
                    }
                    session.Start(); socket = null; cipher = null;
                }
                finally
                {
                    lock (sync) { if (ReferenceEquals(connecting, operation)) { connecting = null; connectingSocket = null; } }
                    socket?.Dispose(); cipher?.Dispose(); connectGate.Release();
                }
            }
        }

        /// <summary>异步发送简易 TCP 客户端。</summary>
        public Task SendAsync(int messageId, long requestId, IMessage message, CancellationToken ct = default, long expectedConnectionId = 0)
        {
            var session = Session();
            if (expectedConnectionId != 0 && session.Generation != expectedConnectionId) throw new IOException("Connection changed before the message was queued.");
            return session.SendAsync(messageId, requestId, message, null, ct);
        }
        /// <summary>异步发送原始。</summary>
        public Task SendRawAsync(int messageId, long requestId, byte[] payload, CancellationToken ct = default) =>
            Session().SendAsync(messageId, requestId, null, payload, ct);
        /// <summary>取得会话。</summary>
        private TcpConnection Session()
        {
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException(nameof(SimpleTcpClient));
                return current ?? throw new IOException("TCP connection is not open.");
            }
        }
        /// <summary>响应数据包。</summary>
        private void OnPacket(TcpConnection session, NetworkPacket packet)
        {
            lock (sync) { if (!ReferenceEquals(current, session) || disposed) return; }
            PacketReceived?.Invoke(session.Generation, packet);
        }
        /// <summary>响应已关闭。</summary>
        private void OnClosed(TcpConnection session, Exception error)
        {
            lock (sync) { if (!ReferenceEquals(current, session)) return; current = null; }
            Closed?.Invoke(session.Generation, error);
        }
        /// <summary>中止简易 TCP 客户端。</summary>
        public void Abort(Exception error = null)
        {
            TcpConnection session;
            lock (sync)
            {
                try { connecting?.Cancel(); } catch (ObjectDisposedException) { }
                connectingSocket?.Dispose(); session = current;
            }
            session?.Abort(error);
        }
        /// <summary>异步断开简易 TCP 客户端。</summary>
        public async Task DisconnectAsync()
        {
            Abort(new IOException("TCP connection closed by the application."));
            await connectGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Task finished;
                lock (sync) finished = lastCompletion;
                Abort(new IOException("TCP connection closed by the application."));
                await finished.ConfigureAwait(false);
            }
            finally { connectGate.Release(); }
        }
        /// <summary>结束简易 TCP 客户端对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true; lifetime.Cancel();
                disposeTask = DisposeCoreAsync();
            }
        }
        /// <summary>异步释放简易 TCP 客户端。</summary>
        private async Task DisposeCoreAsync()
        { await DisconnectAsync().ConfigureAwait(false); lifetime.Dispose(); }
        /// <summary>等待资源和后台操作完成异步清理。</summary>
        public async ValueTask DisposeAsync()
        { Dispose(); Task task; lock (sync) task = disposeTask; await task.ConfigureAwait(false); }
    }
}

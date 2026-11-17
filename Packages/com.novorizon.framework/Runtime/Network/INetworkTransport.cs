using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace Game.Framework.Network
{
    // Framed transport contract; the module currently ships only a TCP implementation.
    /// <summary>定义网络传输层的调用契约。</summary>
    public interface INetworkTransport : IDisposable, IAsyncDisposable
    {
        bool IsConnected { get; }
        long ConnectionId { get; }
        Task ConnectionCompletion { get; }
        int PendingSendCount { get; }
        int PendingSendBytes { get; }
        event Action<long, NetworkPacket> PacketReceived;
        event Action<long, Exception> Closed;
        /// <summary>异步连接网络传输层。</summary>
        Task ConnectAsync(CancellationToken ct = default);
        /// <summary>异步断开网络传输层。</summary>
        Task DisconnectAsync();
        /// <summary>中止网络传输层。</summary>
        void Abort(Exception error = null);
        /// <summary>异步发送网络传输层。</summary>
        Task SendAsync(int messageId, long requestId, IMessage message, CancellationToken ct = default, long expectedConnectionId = 0);
    }
}

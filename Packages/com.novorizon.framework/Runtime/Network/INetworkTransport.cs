using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace Game.Framework.Network
{
    // Framed transport contract; the module currently ships only a TCP implementation.
    public interface INetworkTransport : IDisposable, IAsyncDisposable
    {
        bool IsConnected { get; }
        long ConnectionId { get; }
        Task ConnectionCompletion { get; }
        int PendingSendCount { get; }
        int PendingSendBytes { get; }
        event Action<long, NetworkPacket> PacketReceived;
        event Action<long, Exception> Closed;
        Task ConnectAsync(CancellationToken ct = default);
        Task DisconnectAsync();
        void Abort(Exception error = null);
        Task SendAsync(int messageId, long requestId, IMessage message, CancellationToken ct = default, long expectedConnectionId = 0);
    }
}

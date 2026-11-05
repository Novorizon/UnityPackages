using System;
using Game.Framework.Network.Protocol;

namespace Game.Framework.Network
{
    public sealed class NetworkPacket
    {
        public NetworkPacket(PacketHeader header, byte[] payload) { Header = header; Payload = payload; }
        public PacketHeader Header { get; }
        // Owned by this packet; not a borrowed view into the receive buffer.
        public byte[] Payload { get; }
    }

    public sealed class NetworkProtocolException : Exception
    { public NetworkProtocolException(string message) : base(message) { } }
    public sealed class SendQueueFullException : Exception
    { public SendQueueFullException() : base("The bounded send queue is full.") { } }
    public sealed class PendingRequestLimitException : Exception
    { public PendingRequestLimitException() : base("The pending request limit was reached.") { } }
    /// <summary>A project authentication callback can reject a connection without automatic retries.</summary>
    public sealed class ConnectionRejectedException : Exception
    {
        public ConnectionRejectedException(string message, Exception innerException = null) : base(message, innerException) { }
    }
    public sealed class RemoteRequestException : Exception
    {
        public RemoteRequestException(PacketHeader header) : base("Server rejected request with code " + header.ResultCode + ".")
        { MessageId = header.MessageId; RequestId = header.RequestId; ResultCode = header.ResultCode; }
        public int MessageId { get; }
        public long RequestId { get; }
        public int ResultCode { get; }
    }
}

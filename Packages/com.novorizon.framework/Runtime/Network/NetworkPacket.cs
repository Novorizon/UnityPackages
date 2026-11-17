using System;
using Game.Framework.Network.Protocol;

namespace Game.Framework.Network
{
    /// <summary>提供网络数据包的实现。</summary>
    public sealed class NetworkPacket
    {
        /// <summary>使用调用方提供的header、payload创建 NetworkPacket。</summary>
        public NetworkPacket(PacketHeader header, byte[] payload) { Header = header; Payload = payload; }
        public PacketHeader Header { get; }
        // Owned by this packet; not a borrowed view into the receive buffer.
        public byte[] Payload { get; }
    }

    /// <summary>网络协议对应的异常。报告此操作特有的异常情况。</summary>
    public sealed class NetworkProtocolException : Exception
    { /* 使用调用方提供的message创建 NetworkProtocolException。 */ public NetworkProtocolException(string message) : base(message) { } }
    /// <summary>Send 队列完整对应的异常。报告此操作特有的异常情况。</summary>
    public sealed class SendQueueFullException : Exception
    { /* 创建 SendQueueFullException 并采用默认配置。 */ public SendQueueFullException() : base("The bounded send queue is full.") { } }
    /// <summary>待处理请求上限对应的异常。报告此操作特有的异常情况。</summary>
    public sealed class PendingRequestLimitException : Exception
    { /* 创建 PendingRequestLimitException 并采用默认配置。 */ public PendingRequestLimitException() : base("The pending request limit was reached.") { } }
    /// <summary>A project authentication callback can reject a connection without automatic retries.</summary>
    /// <remarks>连接已拒绝对应的异常。报告此操作特有的异常情况。</remarks>
    public sealed class ConnectionRejectedException : Exception
    {
        /// <summary>使用调用方提供的message、innerException创建 ConnectionRejectedException。</summary>
        public ConnectionRejectedException(string message, Exception innerException = null) : base(message, innerException) { }
    }
    /// <summary>远程请求对应的异常。报告此操作特有的异常情况。</summary>
    public sealed class RemoteRequestException : Exception
    {
        /// <summary>使用调用方提供的header创建 RemoteRequestException。</summary>
        public RemoteRequestException(PacketHeader header) : base("Server rejected request with code " + header.ResultCode + ".")
        { MessageId = header.MessageId; RequestId = header.RequestId; ResultCode = header.ResultCode; }
        public int MessageId { get; }
        public long RequestId { get; }
        public int ResultCode { get; }
    }
}

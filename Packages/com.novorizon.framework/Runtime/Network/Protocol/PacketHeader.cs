namespace Game.Framework.Network.Protocol
{
    public readonly struct PacketHeader
    {
        public PacketHeader(int messageId, long requestId, int resultCode, int payloadLength)
        { MessageId = messageId; RequestId = requestId; ResultCode = resultCode; PayloadLength = payloadLength; }
        public int MessageId { get; }
        public long RequestId { get; }
        public int ResultCode { get; }
        public int PayloadLength { get; }
    }

}

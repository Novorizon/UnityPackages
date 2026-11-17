namespace Game.Framework.Network.Protocol
{
    /// <summary>保存数据包头部所需的数据与配套操作。</summary>
    public readonly struct PacketHeader
    {
        /// <summary>使用调用方提供的messageId、requestId、resultCode创建 PacketHeader。</summary>
        public PacketHeader(int messageId, long requestId, int resultCode, int payloadLength)
        { MessageId = messageId; RequestId = requestId; ResultCode = resultCode; PayloadLength = payloadLength; }
        public int MessageId { get; }
        public long RequestId { get; }
        public int ResultCode { get; }
        public int PayloadLength { get; }
    }

}

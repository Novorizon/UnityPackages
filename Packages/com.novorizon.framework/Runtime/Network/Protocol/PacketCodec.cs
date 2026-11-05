using System;
using System.Buffers.Binary;

namespace Game.Framework.Network.Protocol
{
    // GWS1/v2 is the existing binary TCP application protocol.
    public static class PacketCodec
    {
        public const int HeaderSize = 28;
        public const uint Magic = 0x31535747;
        public const byte Version = 2;
        public const int NotificationMessageIdStart = 2100000000;
        public static bool IsNotification(PacketHeader header) => header.RequestId == 0 &&
            header.MessageId >= NotificationMessageIdStart && header.ResultCode == 0;

        public static bool TryReadHeader(ReadOnlySpan<byte> bytes, int maximumApplicationBytes, out PacketHeader header)
        {
            header = default;
            if (bytes.Length < HeaderSize || maximumApplicationBytes < HeaderSize ||
                BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic || bytes[4] != Version ||
                bytes[5] != 0 || bytes[6] != 0 || bytes[7] != 0) return false;
            int size = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(24));
            if (size < 0 || size > maximumApplicationBytes - HeaderSize) return false;
            header = new PacketHeader(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(8)),
                BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(12)),
                BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(20)), size);
            return true;
        }

        public static void WriteHeader(Span<byte> bytes, PacketHeader header)
        {
            if (bytes.Length < HeaderSize || header.PayloadLength < 0) throw new ArgumentException("Invalid packet size.");
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, Magic);
            bytes[4] = Version; bytes.Slice(5, 3).Clear();
            BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(8), header.MessageId);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(12), header.RequestId);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(20), header.ResultCode);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.Slice(24), header.PayloadLength);
        }
    }
}

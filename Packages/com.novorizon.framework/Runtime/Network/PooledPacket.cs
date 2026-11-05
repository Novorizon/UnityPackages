using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Protocol;
using Google.Protobuf;

namespace Game.Framework.Network
{
    internal sealed class PooledPacket : IDisposable
    {
        private byte[] buffer;
        internal byte[] Buffer => buffer;
        internal int ApplicationLength { get; }
        internal int WireLength { get; }
        internal int Capacity { get; }
        internal CancellationToken Cancellation { get; }
        internal TaskCompletionSource<bool> Sent { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private PooledPacket(byte[] bytes, int length, int overhead, CancellationToken ct)
        { buffer = bytes; Capacity = bytes.Length; ApplicationLength = length; WireLength = length + overhead; Cancellation = ct; }

        internal static PooledPacket Create(int messageId, long requestId, IMessage message, byte[] rawPayload,
            int overhead, int maximumWireBytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            int length = message != null ? message.CalculateSize() : rawPayload?.Length ?? 0;
            if (length < 0 || length > maximumWireBytes - PacketCodec.HeaderSize - overhead)
                throw new ArgumentOutOfRangeException(nameof(message), "Message exceeds MaxPacketBytes.");
            byte[] bytes = ArrayPool<byte>.Shared.Rent(PacketCodec.HeaderSize + length + overhead);
            try
            {
                PacketCodec.WriteHeader(bytes, new PacketHeader(messageId, requestId, 0, length));
                if (message != null) message.WriteTo(bytes.AsSpan(PacketCodec.HeaderSize, length));
                else if (length != 0) Array.Copy(rawPayload, 0, bytes, PacketCodec.HeaderSize, length);
                return new PooledPacket(bytes, PacketCodec.HeaderSize + length, overhead, ct);
            }
            catch { ArrayPool<byte>.Shared.Return(bytes, clearArray: true); throw; }
        }

        public void Dispose()
        {
            byte[] value = Interlocked.Exchange(ref buffer, null);
            if (value != null) ArrayPool<byte>.Shared.Return(value, clearArray: true);
        }
    }
}

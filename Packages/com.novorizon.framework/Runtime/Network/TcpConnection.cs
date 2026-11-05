using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Protocol;
using Game.Framework.Network.Security;
using Google.Protobuf;

namespace Game.Framework.Network
{
    internal sealed class TcpConnection
    {
        private readonly object sync = new object();
        private readonly TcpClient socket;
        private readonly NetworkStream stream;
        private readonly TcpClientOptions options;
        private readonly PacketCipher cipher;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly SemaphoreSlim signal = new SemaphoreSlim(0);
        private readonly Queue<PooledPacket> sends = new Queue<PooledPacket>();
        private readonly Action<TcpConnection, NetworkPacket> received;
        private readonly Action<TcpConnection, Exception> closed;
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> aborted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private int stopped, pendingCount, pendingBytes;
        internal TcpConnection(long generation, TcpClient socket, NetworkStream stream, PacketCipher cipher,
            TcpClientOptions options, Action<TcpConnection, NetworkPacket> received, Action<TcpConnection, Exception> closed)
        { Generation = generation; this.socket = socket; this.stream = stream; this.cipher = cipher; this.options = options; this.received = received; this.closed = closed; }
        internal long Generation { get; }
        internal bool IsOpen => Volatile.Read(ref stopped) == 0;
        internal Task Completion => completion.Task;
        internal int PendingSendCount { get { lock (sync) return pendingCount; } }
        internal int PendingSendBytes { get { lock (sync) return pendingBytes; } }
        internal void Start() { _ = RunAsync(); }

        internal Task SendAsync(int id, long requestId, IMessage message, byte[] payload, CancellationToken ct)
        {
            lock (sync)
            {
                if (!IsOpen) throw new IOException("TCP connection is closed.");
                if (pendingCount >= options.MaxPendingSends) throw new SendQueueFullException();
                var packet = PooledPacket.Create(id, requestId, message, payload, cipher == null ? 0 : PacketCipher.TagBytes, options.MaxPacketBytes, ct);
                if (packet.Capacity > options.MaxPendingSendBytes - pendingBytes)
                { packet.Dispose(); throw new SendQueueFullException(); }
                pendingCount++; pendingBytes += packet.Capacity; sends.Enqueue(packet); signal.Release();
                return ct.CanBeCanceled ? AsyncOperation.WaitAsync(packet.Sent.Task, ct) : packet.Sent.Task;
            }
        }

        internal void Abort(Exception error)
        {
            if (Interlocked.Exchange(ref stopped, 1) != 0) return;
            try
            {
                stop.Cancel(); socket.Dispose();
                lock (sync)
                {
                    while (sends.Count != 0)
                    {
                        var packet = sends.Dequeue(); pendingCount--; pendingBytes -= packet.Capacity;
                        packet.Sent.TrySetException(error ?? new IOException("TCP connection closed.")); packet.Dispose();
                    }
                }
                try { closed(this, error); } catch { /* Application events cannot break transport cleanup. */ }
            }
            finally { aborted.TrySetResult(true); }
        }

        private async Task RunAsync()
        {
            try
            {
                // Both loops call Abort on exit; that cancels the sibling before WhenAll waits for it.
                await Task.WhenAll(SendLoopAsync(), ReceiveLoopAsync()).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    Abort(null); await aborted.Task.ConfigureAwait(false);
                    cipher?.Dispose(); stream.Dispose(); stop.Dispose(); signal.Dispose();
                }
                finally { completion.TrySetResult(true); }
            }
        }

        private async Task SendLoopAsync()
        {
            Exception failure = null;
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    await signal.WaitAsync(stop.Token).ConfigureAwait(false);
                    PooledPacket packet;
                    lock (sync) { if (sends.Count == 0) continue; packet = sends.Dequeue(); }
                    Exception packetFailure = null;
                    bool canceled = false;
                    try
                    {
                        canceled = packet.Cancellation.IsCancellationRequested;
                        if (!canceled)
                        {
                            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, packet.Cancellation))
                            {
                                operation.CancelAfter(options.SendTimeoutMilliseconds);
                                // Closing the socket also interrupts I/O on older Unity Mono implementations.
                                using (operation.Token.Register(() => socket.Dispose()))
                                {
                                    int count = cipher == null ? packet.WireLength : cipher.Encode(packet.Buffer, packet.ApplicationLength);
                                    await stream.WriteAsync(packet.Buffer, 0, count, operation.Token).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    catch (Exception error) { packetFailure = error; }
                    finally
                    {
                        lock (sync) { pendingCount--; pendingBytes -= packet.Capacity; }
                        packet.Dispose();
                    }
                    // Returning successfully also releases this packet's queue slot and byte reservation.
                    if (packetFailure != null)
                    {
                        failure = packetFailure; Abort(packetFailure); packet.Sent.TrySetException(packetFailure); return;
                    }
                    if (canceled) packet.Sent.TrySetCanceled(); else packet.Sent.TrySetResult(true);
                }
            }
            catch (Exception error) { failure = error; }
            finally { Abort(failure); }
        }

        private async Task ReceiveLoopAsync()
        {
            Exception failure = null;
            byte[] headerBytes = new byte[PacketCodec.HeaderSize];
            byte[] tagBytes = cipher == null ? null : new byte[PacketCipher.TagBytes];
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    int first = await stream.ReadAsync(headerBytes, 0, headerBytes.Length, stop.Token).ConfigureAwait(false);
                    if (first == 0) throw new EndOfStreamException("Peer closed the TCP connection.");
                    using (var operation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                    using (operation.Token.Register(() => socket.Dispose()))
                    {
                        operation.CancelAfter(options.PacketTimeoutMilliseconds);
                        await ReadExactlyAsync(stream, headerBytes, first, headerBytes.Length - first, operation.Token).ConfigureAwait(false);
                        int overhead = cipher == null ? 0 : PacketCipher.TagBytes;
                        if (!PacketCodec.TryReadHeader(headerBytes, options.MaxPacketBytes - overhead, out var header))
                            throw new NetworkProtocolException("Invalid TCP packet header.");
                        // The packet owns its exact payload array. Read into it directly, so no borrowed
                        // buffer can escape into a request continuation or a queued main-thread callback.
                        byte[] payload = header.PayloadLength == 0 ? Array.Empty<byte>() : new byte[header.PayloadLength];
                        await ReadExactlyAsync(stream, payload, 0, payload.Length, operation.Token).ConfigureAwait(false);
                        if (cipher != null)
                        {
                            await ReadExactlyAsync(stream, tagBytes, 0, tagBytes.Length, operation.Token).ConfigureAwait(false);
                            cipher.DecodePayload(headerBytes, payload, tagBytes, options.MaxPacketBytes);
                        }
                        received(this, new NetworkPacket(header, payload));
                    }
                }
            }
            catch (Exception error) { failure = error; }
            finally { Abort(failure); }
        }

        internal static async Task ReadExactlyAsync(Stream stream, byte[] bytes, int offset, int length, CancellationToken ct)
        {
            while (length != 0)
            {
                int count = await stream.ReadAsync(bytes, offset, length, ct).ConfigureAwait(false);
                if (count == 0) throw new EndOfStreamException("Truncated TCP packet.");
                offset += count; length -= count;
            }
        }
    }
}

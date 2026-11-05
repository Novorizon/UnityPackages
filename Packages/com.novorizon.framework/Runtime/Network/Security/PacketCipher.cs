using System;
using System.Buffers.Binary;
using System.Text;
using Game.Framework.Network.Protocol;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;

namespace Game.Framework.Network.Security
{
    // Standard AES-256-GCM/HKDF-SHA256, interoperable with GameServer's .NET implementation.
    // Each connection owns two directions; callers serialize each direction and dispose after I/O stops.
    public sealed class PacketCipher : IDisposable
    {
        public const int HelloBytes = 40;
        public const int TagBytes = 16;
        private sealed class Direction
        {
            internal GcmBlockCipher Cipher = new GcmBlockCipher(new AesEngine());
            private readonly byte[] key;
            private readonly byte[] nonce = new byte[12];
            private readonly byte[] associatedData = new byte[PacketCodec.HeaderSize];
            private readonly AeadParameters reuseParameters;
            private ulong counter;
            private bool initialized, failed;

            internal Direction(byte[] key)
            {
                this.key = key;
                // BC 2.6.2 retains these arrays, but copies the nonce into separate last-nonce state.
                // A null key explicitly reuses the expanded AES key and GCM multiplier after first Init.
                reuseParameters = new AeadParameters(null, TagBytes * 8, nonce, associatedData);
            }
            internal void Begin(bool encrypt, byte[] header)
            {
                if (failed) throw new NetworkProtocolException("Cipher direction failed; establish a new connection.");
                if (counter == ulong.MaxValue) throw new NetworkProtocolException("Message counter exhausted.");
                BinaryPrimitives.WriteUInt64LittleEndian(nonce.AsSpan(4), counter);
                Array.Copy(header, associatedData, associatedData.Length);
                try
                {
                    if (initialized) Cipher.Init(encrypt, reuseParameters);
                    else
                    {
                        try { Cipher.Init(encrypt, new AeadParameters(new KeyParameter(key), TagBytes * 8, nonce, associatedData)); }
                        finally { Array.Clear(key, 0, key.Length); }
                        initialized = true;
                    }
                }
                catch { Fail(); throw; }
            }
            internal void Complete() { counter++; }
            internal void Fail() { failed = true; Array.Clear(key, 0, key.Length); }
            internal void Dispose()
            {
                Array.Clear(key, 0, key.Length); Array.Clear(nonce, 0, nonce.Length);
                Array.Clear(associatedData, 0, associatedData.Length);
                // The vendor does not expose deterministic round-key erasure. Release its state rather
                // than retaining expanded keys through a disposed PacketCipher; all owned arrays are wiped.
                try { if (initialized) Cipher.Reset(); }
                finally { Cipher = null; failed = true; }
            }
        }
        private readonly Direction send, receive;
        private bool disposed;

        public PacketCipher(byte[] key, byte[] hello, bool server = false)
        {
            if (key == null || key.Length != 32 || hello == null || hello.Length != HelloBytes ||
                hello[0] != 'T' || hello[1] != 'C' || hello[2] != 'P' || hello[3] != 'A' ||
                hello[4] != 1 || hello[5] != 0 || hello[6] != 0 || hello[7] != 0)
                throw new NetworkProtocolException("Invalid encryption initialization.");
            byte[] c2s = null, s2c = null;
            try
            {
                c2s = Derive(key, hello, "GameTcp/v1/client-to-server");
                s2c = Derive(key, hello, "GameTcp/v1/server-to-client");
                send = new Direction(server ? s2c : c2s); receive = new Direction(server ? c2s : s2c);
            }
            catch
            {
                if (c2s != null) Array.Clear(c2s, 0, c2s.Length);
                if (s2c != null) Array.Clear(s2c, 0, s2c.Length);
                throw;
            }
        }

        private static byte[] Derive(byte[] key, byte[] hello, string info)
        {
            byte[] salt = new byte[32]; Array.Copy(hello, 8, salt, 0, salt.Length);
            try
            {
                var generator = new HkdfBytesGenerator(new Sha256Digest());
                generator.Init(new HkdfParameters(key, salt, Encoding.UTF8.GetBytes(info)));
                byte[] result = new byte[32]; generator.GenerateBytes(result, 0, result.Length);
                return result;
            }
            finally { Array.Clear(salt, 0, salt.Length); }
        }

        public int Encode(byte[] buffer, int applicationLength)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PacketCipher));
            if (buffer == null || applicationLength < PacketCodec.HeaderSize || applicationLength > buffer.Length - TagBytes ||
                !PacketCodec.TryReadHeader(buffer.AsSpan(0, applicationLength), applicationLength, out var header) ||
                applicationLength != PacketCodec.HeaderSize + header.PayloadLength)
                throw new ArgumentException("Invalid outgoing packet.");
            try
            {
                send.Begin(true, buffer);
                int count = send.Cipher.ProcessBytes(buffer, PacketCodec.HeaderSize, header.PayloadLength, buffer, PacketCodec.HeaderSize);
                send.Cipher.DoFinal(buffer, PacketCodec.HeaderSize + count);
                send.Complete();
                return applicationLength + TagBytes;
            }
            catch { send.Fail(); throw; }
        }

        public void Decode(byte[] buffer, int wireLength, int maximumWireBytes)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PacketCipher));
            if (maximumWireBytes < PacketCodec.HeaderSize + TagBytes || buffer == null || wireLength < PacketCodec.HeaderSize + TagBytes || wireLength > buffer.Length ||
                !PacketCodec.TryReadHeader(buffer.AsSpan(0, wireLength), maximumWireBytes - TagBytes, out var header) ||
                wireLength != PacketCodec.HeaderSize + header.PayloadLength + TagBytes)
                throw new NetworkProtocolException("Invalid encrypted packet size.");
            try
            {
                receive.Begin(false, buffer);
                int count = receive.Cipher.ProcessBytes(buffer, PacketCodec.HeaderSize, header.PayloadLength + TagBytes, buffer, PacketCodec.HeaderSize);
                receive.Cipher.DoFinal(buffer, PacketCodec.HeaderSize + count);
                receive.Complete();
            }
            catch (InvalidCipherTextException)
            {
                receive.Fail(); Array.Clear(buffer, PacketCodec.HeaderSize, header.PayloadLength);
                throw new NetworkProtocolException("Packet authentication failed.");
            }
            catch { receive.Fail(); Array.Clear(buffer, PacketCodec.HeaderSize, header.PayloadLength); throw; }
        }

        // The transport owns payload; header and tag can be reused after this synchronous call.
        internal void DecodePayload(byte[] headerBytes, byte[] payload, byte[] tag, int maximumWireBytes)
        {
            if (disposed) throw new ObjectDisposedException(nameof(PacketCipher));
            if (maximumWireBytes < PacketCodec.HeaderSize + TagBytes || headerBytes == null || headerBytes.Length != PacketCodec.HeaderSize || payload == null || tag == null || tag.Length != TagBytes ||
                !PacketCodec.TryReadHeader(headerBytes, maximumWireBytes - TagBytes, out var header) || header.PayloadLength != payload.Length)
                throw new NetworkProtocolException("Invalid encrypted packet size.");
            try
            {
                receive.Begin(false, headerBytes);
                int count = receive.Cipher.ProcessBytes(payload, 0, payload.Length, payload, 0);
                count += receive.Cipher.ProcessBytes(tag, 0, tag.Length, payload, count);
                receive.Cipher.DoFinal(payload, count);
                receive.Complete();
            }
            catch (InvalidCipherTextException)
            {
                receive.Fail(); Array.Clear(payload, 0, payload.Length);
                throw new NetworkProtocolException("Packet authentication failed.");
            }
            catch { receive.Fail(); Array.Clear(payload, 0, payload.Length); throw; }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { send.Dispose(); } finally { receive.Dispose(); }
        }
    }
}

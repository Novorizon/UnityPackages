using System;

namespace Game.Framework.Network
{
    public enum PacketEncryptionMode { None = 0, AesGcm = 1 }
    public enum NetworkState { Disconnected, Connecting, Authenticating, Connected, Reconnecting, Disposed }

    public sealed class TcpClientOptions
    {
        public string Host { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 1236;
        public PacketEncryptionMode Encryption { get; set; } = PacketEncryptionMode.AesGcm;
        public string KeyBase64 { get; set; } = "";
        public int ConnectTimeoutMilliseconds { get; set; } = 10000;
        public int PacketTimeoutMilliseconds { get; set; } = 10000;
        public int SendTimeoutMilliseconds { get; set; } = 3000;
        public int MaxPacketBytes { get; set; } = 256 * 1024;
        public int MaxPendingSends { get; set; } = 128;
        public int MaxPendingSendBytes { get; set; } = 4 * 1024 * 1024;

        internal TcpClientOptions Snapshot()
        {
            var copy = (TcpClientOptions)MemberwiseClone();
            if (string.IsNullOrWhiteSpace(Host) || Host.IndexOf("://", StringComparison.Ordinal) >= 0 || Port < 1 || Port > 65535 ||
                !Enum.IsDefined(typeof(PacketEncryptionMode), Encryption) || ConnectTimeoutMilliseconds < 1 ||
                PacketTimeoutMilliseconds < 1 || SendTimeoutMilliseconds < 1 || MaxPacketBytes < 1024 ||
                MaxPacketBytes > 4 * 1024 * 1024 || MaxPendingSends < 1 || MaxPendingSends > 10000 ||
                (MaxPacketBytes & (MaxPacketBytes - 1)) != 0 ||
                MaxPendingSendBytes < MaxPacketBytes)
                throw new ArgumentException("Invalid TCP options; Host is a DNS name or IP, without a URL scheme.");
            if (Encryption == PacketEncryptionMode.AesGcm)
            {
                byte[] key;
                try { key = Convert.FromBase64String(KeyBase64); }
                catch (Exception error) when (error is FormatException || error is ArgumentNullException)
                { throw new ArgumentException("AesGcm requires a 32-byte Base64 communication key."); }
                try { if (key.Length != 32) throw new ArgumentException("AesGcm requires a 32-byte Base64 communication key."); }
                finally { Array.Clear(key, 0, key.Length); }
            }
            return copy;
        }
    }

    public sealed class NetworkClientOptions
    {
        public bool AutoReconnect { get; set; } = true;
        public bool AutomaticDispatch { get; set; } = true;
        public bool RetryInitialConnection { get; set; } = false;
        public int RequestTimeoutMilliseconds { get; set; } = 10000;
        public int AuthenticationTimeoutMilliseconds { get; set; } = 10000;
        public int HeartbeatIntervalMilliseconds { get; set; } = 15000;
        public int HeartbeatMessageId { get; set; } = 1;
        public int MaxPendingRequests { get; set; } = 128;
        public int ReconnectMinimumMilliseconds { get; set; } = 500;
        public int ReconnectMaximumMilliseconds { get; set; } = 30000;
        public int MaxReconnectAttempts { get; set; } = 0;
        internal NetworkClientOptions Snapshot()
        {
            if (RequestTimeoutMilliseconds < 1 || AuthenticationTimeoutMilliseconds < 1 || HeartbeatIntervalMilliseconds < 0 || HeartbeatMessageId < 1 ||
                HeartbeatMessageId >= Protocol.PacketCodec.NotificationMessageIdStart || MaxPendingRequests < 1 ||
                MaxPendingRequests > 10000 || ReconnectMinimumMilliseconds < 1 ||
                ReconnectMaximumMilliseconds < ReconnectMinimumMilliseconds || MaxReconnectAttempts < 0)
                throw new ArgumentException("Invalid network session options.");
            return (NetworkClientOptions)MemberwiseClone();
        }
    }
}

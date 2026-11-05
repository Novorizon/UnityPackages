using System;
namespace Game.Framework.Network
{
    public sealed class NetworkConfiguration
    {
        public TcpClientOptions Tcp { get; set; } = new TcpClientOptions();
        public NetworkClientOptions Client { get; set; } = new NetworkClientOptions();
    }
    internal static class NetworkDefaults
    {
        internal static Func<NetworkConfiguration> ConfigurationProvider { get; set; }
        internal static NetworkConfiguration Load() => ConfigurationProvider?.Invoke() ??
            throw new InvalidOperationException("No default network settings host is installed. In Unity create Resources/NetworkSettings; other hosts pass explicit options.");
    }
}

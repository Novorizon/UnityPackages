using System;
namespace Game.Framework.Network
{
    /// <summary>提供网络配置的实现。</summary>
    public sealed class NetworkConfiguration
    {
        public TcpClientOptions Tcp { get; set; } = new TcpClientOptions();
        public NetworkClientOptions Client { get; set; } = new NetworkClientOptions();
    }
    /// <summary>网络对应的Defaults。保存相关配置的默认值。</summary>
    internal static class NetworkDefaults
    {
        internal static Func<NetworkConfiguration> ConfigurationProvider { get; set; }
        /// <summary>加载网络 Defaults。</summary>
        internal static NetworkConfiguration Load() => ConfigurationProvider?.Invoke() ??
            throw new InvalidOperationException("No default network settings host is installed. In Unity create Resources/NetworkSettings; other hosts pass explicit options.");
    }
}

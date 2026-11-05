using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Dispatching;

namespace Game.Framework.Network
{
    public sealed partial class NetworkManager
    {
        // 通用实例同步、工厂创建和重置由 Framework 基类处理；网络配置仍由本类负责。
        public static NetworkManager Instance => GetOrCreateInstance(() => new NetworkManager());

        public static void Configure(TcpClientOptions transportOptions, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
        {
            ConfigureInstance(() => new NetworkManager(transportOptions, options, dispatcher, authenticate));
        }

        public static void Configure(INetworkTransport transport, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
        {
            ConfigureInstance(() => new NetworkManager(transport, options, dispatcher, authenticate));
        }

        internal static void ResetSingleton(bool stopping = false)
        {
            ResetInstance(value => value.Dispose(), stopping);
        }

        private void ReleaseSingleton()
        {
            ReleaseInstance(this);
        }
    }
}
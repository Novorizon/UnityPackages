using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Dispatching;

namespace Game.Framework.Network
{
    /// <summary>网络对应的管理器。管理对应模块的状态和操作入口。</summary>
    public sealed partial class NetworkManager
    {
        private static readonly object instanceGate = new object();
        private static NetworkManager instance;
        private static bool configuring;
        private static bool releasing;

#if UNITY_EDITOR
        /// <summary>为 NetworkManager 准备类型首次使用时所需的静态状态。</summary>
        static NetworkManager()
        {
            EditorStaticReset.Register(ReleaseCurrent);
        }
#endif

        public static NetworkManager Instance
        {
            get
            {
                lock (instanceGate)
                {
                    EnsureInstanceAvailable();
                    return instance ?? (instance = new NetworkManager());
                }
            }
        }

        public static bool IsCreated => TryGetInstance(out _);

        /// <summary>尝试取得实例，返回是否成功。</summary>
        public static bool TryGetInstance(out NetworkManager value)
        {
            lock (instanceGate)
            {
                value = instance;
                return value != null;
            }
        }

        /// <summary>配置网络管理器。</summary>
        public static void Configure(TcpClientOptions transportOptions, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
        {
            Configure(() => new NetworkManager(transportOptions, options, dispatcher, authenticate));
        }

        /// <summary>配置网络管理器。</summary>
        public static void Configure(INetworkTransport transport, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
        {
            Configure(() => new NetworkManager(transport, options, dispatcher, authenticate));
        }

        /// <summary>配置网络管理器。</summary>
        private static void Configure(Func<NetworkManager> create)
        {
            lock (instanceGate)
            {
                EnsureInstanceAvailable();
                if (instance != null)
                {
                    throw new InvalidOperationException("Configure the network before accessing its instance.");
                }

                configuring = true;
                try
                {
                    instance = create();
                }
                finally
                {
                    configuring = false;
                }
            }
        }

        /// <summary>结束网络管理器对相关资源的持有，并执行该类型的清理流程。</summary>
        public static void Release()
        {
            try
            {
                ReleaseCurrent();
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }
        }

        /// <summary>释放当前。</summary>
        private static void ReleaseCurrent()
        {
            NetworkManager previous;
            lock (instanceGate)
            {
                if (configuring || releasing)
                {
                    throw new InvalidOperationException("Cannot release the network during configuration or release.");
                }

                previous = instance;
                if (previous == null)
                {
                    return;
                }

                instance = null;
                releasing = true;
            }

            try
            {
                previous.Dispose();
            }
            finally
            {
                lock (instanceGate)
                {
                    releasing = false;
                }
            }
        }

        /// <summary>检查并确保实例可用可用。</summary>
        private static void EnsureInstanceAvailable()
        {
            if (configuring || releasing)
            {
                throw new InvalidOperationException("Cannot access the network during configuration or release.");
            }
        }
    }
}

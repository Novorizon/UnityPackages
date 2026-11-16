using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Dispatching;

namespace Game.Framework.Network
{
    public sealed partial class NetworkManager
    {
        private static readonly object instanceGate = new object();
        private static NetworkManager instance;
        private static bool configuring;
        private static bool releasing;

#if UNITY_EDITOR
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

        public static bool TryGetInstance(out NetworkManager value)
        {
            lock (instanceGate)
            {
                value = instance;
                return value != null;
            }
        }

        public static void Configure(TcpClientOptions transportOptions, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
        {
            Configure(() => new NetworkManager(transportOptions, options, dispatcher, authenticate));
        }

        public static void Configure(INetworkTransport transport, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
        {
            Configure(() => new NetworkManager(transport, options, dispatcher, authenticate));
        }

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

        private static void EnsureInstanceAvailable()
        {
            if (configuring || releasing)
            {
                throw new InvalidOperationException("Cannot access the network during configuration or release.");
            }
        }
    }
}

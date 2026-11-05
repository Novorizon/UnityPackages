using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Dispatching;

namespace Game.Framework.Network
{
    public sealed partial class NetworkManager
    {
        private static readonly object singletonGate = new object();
        private static NetworkManager instance;
        private static bool applicationStopped;

        // Initialize on the Unity main thread after obtaining the lightweight singleton.
        public static NetworkManager Instance
        {
            get
            {
                lock (singletonGate)
                {
                    ThrowIfApplicationStopped();
                    return instance ?? (instance = new NetworkManager());
                }
            }
        }
        public static bool IsCreated { get { lock (singletonGate) return instance != null; } }

        public static void Configure(TcpClientOptions transportOptions, NetworkClientOptions options = null,
            INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null) =>
            Install(() => new NetworkManager(transportOptions, options, dispatcher, authenticate));

        public static void Configure(INetworkTransport transport, NetworkClientOptions options = null,
            INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null) =>
            Install(() => new NetworkManager(transport, options, dispatcher, authenticate));

        private static void Install(Func<NetworkManager> create)
        {
            lock (singletonGate)
            {
                ThrowIfApplicationStopped();
                if (instance != null) throw new InvalidOperationException("Configure NetworkManager once at application startup, before accessing Instance.");
                instance = create();
            }
        }
        private static void ThrowIfApplicationStopped()
        {
            if (applicationStopped) throw new InvalidOperationException("The application is stopping; NetworkManager cannot be recreated.");
        }
        internal static void ResetSingleton(bool stopping = false)
        {
            NetworkManager previous;
            lock (singletonGate) { previous = instance; instance = null; applicationStopped = stopping; }
            previous?.Dispose();
        }
        private void ReleaseSingleton()
        {
            lock (singletonGate) { if (ReferenceEquals(instance, this)) instance = null; }
        }
    }
}

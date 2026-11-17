using System;
using System.Threading;
using System.Threading.Tasks;
namespace Game.Framework.Network
{
    /// <summary>网络对应的管理器。管理对应模块的状态和操作入口。</summary>
    public sealed partial class NetworkManager
    {
        /// <summary>创建 NetworkManager 并采用默认配置。</summary>
        public NetworkManager()
        {
        }

        public bool IsInitialized => Volatile.Read(ref session) != null && Volatile.Read(ref disposed) == 0;

        // Load/validate settings and install the dispatcher, without connecting or logging in.
        /// <summary>初始化网络管理器。</summary>
        public void Initialize() => Initialize(null);

        /// <summary>初始化网络管理器。</summary>
        public void Initialize(Func<AuthenticationContext, CancellationToken, Task> authenticate)
        {
            if (Volatile.Read(ref disposed) != 0) throw new ObjectDisposedException(nameof(NetworkManager));
            lock (initializationGate)
            {
                if (disposed != 0) throw new ObjectDisposedException(nameof(NetworkManager));
                if (session != null)
                {
                    if (authenticate != null) throw new InvalidOperationException("Configure authentication before initializing NetworkManager.");
                    return;
                }
                var settings = NetworkDefaults.Load();
                try
                {
                    var created = new NetworkSession(settings.Tcp, settings.Client,
                        ResolveDispatcher(null, settings.Client), WrapAuthentication(authenticate));
                    Volatile.Write(ref session, created);
                }
                catch { Interlocked.Exchange(ref automaticDispatcher, null)?.Dispose(); throw; }
            }
        }
        private NetworkSession Session { get { Initialize(); return session; } }

    }
}

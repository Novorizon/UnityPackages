using System;
using System.Threading;
using System.Threading.Tasks;
namespace Game.Framework.Network
{
    public sealed partial class NetworkManager
    {
        private NetworkManager() { }

        public bool IsInitialized => Volatile.Read(ref session) != null && Volatile.Read(ref disposed) == 0;

        // Load/validate settings and install the dispatcher, without connecting or logging in.
        public void Initialize() => Initialize(null);

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

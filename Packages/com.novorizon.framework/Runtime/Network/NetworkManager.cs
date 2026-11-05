using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Dispatching;
using Game.Framework.Network.Protocol;
using Google.Protobuf;

namespace Game.Framework.Network
{
    // Application entry point. Owns a session without application-specific messages.
    public sealed partial class NetworkManager : IDisposable, IAsyncDisposable
    {
        private NetworkSession session;
        private readonly object initializationGate = new object();
        private int disposed;
        private IAutomaticNetworkDispatcher automaticDispatcher;

        // For a custom protocol, supply its authentication callback or omit authentication.
        internal NetworkManager(TcpClientOptions transportOptions, NetworkClientOptions options = null,
            INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
            : this(new SimpleTcpClient(transportOptions), options, dispatcher, authenticate) { }

        internal NetworkManager(INetworkTransport transport, NetworkClientOptions options = null,
            INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
        {
            try { session = new NetworkSession(transport, options, ResolveDispatcher(dispatcher, options), WrapAuthentication(authenticate)); }
            catch { automaticDispatcher?.Dispose(); throw; }
        }

        private INetworkDispatcher ResolveDispatcher(INetworkDispatcher dispatcher, NetworkClientOptions options)
        {
            if (dispatcher != null) return dispatcher;
            if (options != null && !options.AutomaticDispatch) return null;
            automaticDispatcher = NetworkDispatcherProvider.CreateAutomatic?.Invoke();
            return automaticDispatcher;
        }

        public bool UsesAutomaticDispatch => Volatile.Read(ref automaticDispatcher) != null;

        private static Func<NetworkSession.AuthenticationContext, CancellationToken, Task> WrapAuthentication(
            Func<AuthenticationContext, CancellationToken, Task> authenticate)
        {
            if (authenticate == null) return null;
            return (context, ct) => authenticate(new AuthenticationContext(context), ct);
        }

        public NetworkState State => session?.State ?? (Volatile.Read(ref disposed) != 0 ? NetworkState.Disposed : NetworkState.Disconnected);
        public bool IsConnected => session?.IsConnected ?? false;
        public long ConnectionId => session?.ConnectionId ?? 0;
        public Exception LastError => session?.LastError;
        public int PendingRequestCount => session?.PendingRequestCount ?? 0;
        public int PendingSendCount => session?.PendingSendCount ?? 0;
        public int PendingSendBytes => session?.PendingSendBytes ?? 0;

        public event Action Connected
        {
            add { Session.Connected += value; }
            remove { if (session != null) session.Connected -= value; }
        }
        public event Action Reconnected
        {
            add { Session.Reconnected += value; }
            remove { if (session != null) session.Reconnected -= value; }
        }
        public event Action<Exception> Disconnected
        {
            add { Session.Disconnected += value; }
            remove { if (session != null) session.Disconnected -= value; }
        }
        public event Action<Exception> Error
        {
            add { Session.Error += value; }
            remove { if (session != null) session.Error -= value; }
        }

        public Task ConnectAsync(CancellationToken ct = default) => Session.ConnectAsync(ct);
        public Task DisconnectAsync() => session?.DisconnectAsync() ?? Task.CompletedTask;

        // Manual host fallback. Unity integration dispatches automatically without calling Update.
        public int Update(int maximumCallbacks = 64) => session?.Update(maximumCallbacks) ?? 0;

        public Task<T> RequestAsync<T>(int messageId, IMessage message, MessageParser<T> parser,
            CancellationToken ct = default) where T : class, IMessage<T> =>
            Session.RequestAsync(messageId, message, parser, ct);

        // Completes after writing bytes; RequestAsync additionally waits for the business response.
        public Task SendAsync(int messageId, IMessage message, CancellationToken ct = default) =>
            Session.SendAsync(messageId, message, ct);

        public IDisposable Subscribe<T>(int messageId, MessageParser<T> parser, Action<T> handler)
            where T : class, IMessage<T> => Session.Subscribe(messageId, parser, handler);

        public void Dispose()
        {
            NetworkSession current;
            lock (initializationGate) { disposed = 1; current = session; }
            try
            {
                Interlocked.Exchange(ref automaticDispatcher, null)?.Dispose();
                current?.Dispose();
            }
            finally { ReleaseSingleton(); }
        }
        public ValueTask DisposeAsync()
        {
            Dispose();
            return session != null ? session.DisposeAsync() : default;
        }

        public sealed class AuthenticationContext
        {
            private readonly NetworkSession.AuthenticationContext context;

            internal AuthenticationContext(NetworkSession.AuthenticationContext context) { this.context = context; }

            public long ConnectionId => context.ConnectionId;

            public Task<T> RequestAsync<T>(int messageId, IMessage request, CancellationToken ct = default)
                where T : class, IMessage<T>, new() =>
                context.RequestAsync(messageId, request, MessageParserCache<T>.Instance, ct);

            public Task<T> RequestAsync<T>(Enum messageId, IMessage request, CancellationToken ct = default)
                where T : class, IMessage<T>, new() => RequestAsync<T>(GetMessageId(messageId), request, ct);

            public Task<T> RequestAsync<T>(int messageId, IMessage request, MessageParser<T> parser,
                CancellationToken ct) where T : class, IMessage<T> =>
                context.RequestAsync(messageId, request, parser, ct);
        }
    }
}

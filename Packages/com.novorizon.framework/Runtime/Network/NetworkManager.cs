using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Dispatching;
using Game.Framework.Network.Protocol;
using Google.Protobuf;

namespace Game.Framework.Network
{
    // Application entry point. Owns a session without application-specific messages.
    /// <summary>持有网络会话，提供连接、收发、订阅以及回调调度入口。</summary>
    public sealed partial class NetworkManager : IDisposable, IAsyncDisposable
    {
        private NetworkSession session;
        private readonly object initializationGate = new object ();
        private int disposed;
        private IAutomaticNetworkDispatcher automaticDispatcher;
        // For a custom protocol, supply its authentication callback or omit authentication.
        /// <summary>使用调用方提供的transportOptions、options、dispatcher创建 NetworkManager。</summary>
        internal NetworkManager(TcpClientOptions transportOptions, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null) : this(new SimpleTcpClient(transportOptions), options, dispatcher, authenticate)
        {
        }

        /// <summary>使用调用方提供的transport、options、dispatcher创建 NetworkManager。</summary>
        internal NetworkManager(INetworkTransport transport, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
        {
            try
            {
                session = new NetworkSession(transport, options, ResolveDispatcher(dispatcher, options), WrapAuthentication(authenticate));
            }
            catch
            {
                automaticDispatcher?.Dispose();
                throw;
            }
        }

        /// <summary>解析调度器。</summary>
        private INetworkDispatcher ResolveDispatcher(INetworkDispatcher dispatcher, NetworkClientOptions options)
        {
            if (dispatcher != null)
            {
                return dispatcher;
            }

            if (options != null && !options.AutomaticDispatch)
            {
                return null;
            }

            automaticDispatcher = NetworkDispatcherProvider.CreateAutomatic?.Invoke();
            return automaticDispatcher;
        }

        public bool UsesAutomaticDispatch => Volatile.Read(ref automaticDispatcher) != null;

        /// <summary>包装认证。</summary>
        private static Func<NetworkSession.AuthenticationContext, CancellationToken, Task> WrapAuthentication(Func<AuthenticationContext, CancellationToken, Task> authenticate)
        {
            if (authenticate == null)
            {
                return null;
            }

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
            add
            {
                Session.Connected += value;
            }

            remove
            {
                if (session != null)
                {
                    session.Connected -= value;
                }
            }
        }

        /// <summary>按派发队列顺序通知连接状态变化；Disposed 后停止派发，最终状态可读取 State。</summary>
        public event Action<NetworkState> StateChanged
        {
            add
            {
                Session.StateChanged += value;
            }

            remove
            {
                if (session != null)
                {
                    session.StateChanged -= value;
                }
            }
        }

        public event Action Reconnected
        {
            add
            {
                Session.Reconnected += value;
            }

            remove
            {
                if (session != null)
                {
                    session.Reconnected -= value;
                }
            }
        }

        public event Action<Exception> Disconnected
        {
            add
            {
                Session.Disconnected += value;
            }

            remove
            {
                if (session != null)
                {
                    session.Disconnected -= value;
                }
            }
        }

        public event Action<Exception> Error
        {
            add
            {
                Session.Error += value;
            }

            remove
            {
                if (session != null)
                {
                    session.Error -= value;
                }
            }
        }

        /// <summary>异步连接网络管理器。</summary>
        public Task ConnectAsync(CancellationToken ct = default) => Session.ConnectAsync(ct);
        /// <summary>异步断开网络管理器。</summary>
        public Task DisconnectAsync() => session?.DisconnectAsync() ?? Task.CompletedTask;
        // Manual host fallback. Unity integration dispatches automatically without calling Update.
        /// <summary>手动执行一批待处理回调，maximumCallbacks 限制单次处理数量。</summary>
        public int Update(int maximumCallbacks = 64) => session?.Update(maximumCallbacks) ?? 0;
        /// <summary>发送请求并等待对应响应，使用指定的 Protobuf 解析器获得结果。</summary>
        public Task<T> RequestAsync<T>(int messageId, IMessage message, MessageParser<T> parser, CancellationToken ct = default)
            where T : class, IMessage<T> => Session.RequestAsync(messageId, message, parser, ct);
        // Completes after writing bytes; RequestAsync additionally waits for the business response.
        /// <summary>异步发送网络管理器。</summary>
        public Task SendAsync(int messageId, IMessage message, CancellationToken ct = default) => Session.SendAsync(messageId, message, ct);
        /// <summary>订阅网络管理器。</summary>
        public IDisposable Subscribe<T>(int messageId, MessageParser<T> parser, Action<T> handler)
            where T : class, IMessage<T> => Session.Subscribe(messageId, parser, handler);
        /// <summary>结束网络管理器对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            NetworkSession current;
            lock (initializationGate)
            {
                disposed = 1;
                current = session;
            }

            try
            {
                Interlocked.Exchange(ref automaticDispatcher, null)?.Dispose();
                current?.Dispose();
            }
            finally
            {
                lock (instanceGate)
                {
                    if (ReferenceEquals(instance, this))
                    {
                        instance = null;
                    }
                }
            }
        }

        /// <summary>等待资源和后台操作完成异步清理。</summary>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return session != null ? session.DisposeAsync() : default;
        }

        /// <summary>认证对应的上下文。携带一次操作所需的上下文。</summary>
        public sealed class AuthenticationContext
        {
            private readonly NetworkSession.AuthenticationContext context;
            /// <summary>使用调用方提供的context创建 AuthenticationContext。</summary>
            internal AuthenticationContext(NetworkSession.AuthenticationContext context)
            {
                this.context = context;
            }

            public long ConnectionId => context.ConnectionId;

            /// <summary>根据传入参数取得请求 Async。</summary>
            public Task<T> RequestAsync<T>(int messageId, IMessage request, CancellationToken ct = default)
                where T : class, IMessage<T>, new() => context.RequestAsync(messageId, request, MessageParserCache<T>.Instance, ct);
            /// <summary>根据传入参数取得请求 Async。</summary>
            public Task<T> RequestAsync<T>(Enum messageId, IMessage request, CancellationToken ct = default)
                where T : class, IMessage<T>, new() => RequestAsync<T>(GetMessageId(messageId), request, ct);
            /// <summary>根据传入参数取得请求 Async。</summary>
            public Task<T> RequestAsync<T>(int messageId, IMessage request, MessageParser<T> parser, CancellationToken ct)
                where T : class, IMessage<T> => context.RequestAsync(messageId, request, parser, ct);
        }
    }
}

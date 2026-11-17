using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Dispatching;
using Game.Framework.Network.Protocol;
using Google.Protobuf;

namespace Game.Framework.Network
{
    /// <summary>提供网络会话的实现。</summary>
    internal sealed class NetworkSession : IDisposable, IAsyncDisposable
    {
        /// <summary>表示网络会话中的待处理。</summary>
        private sealed class Pending
        {
            internal long Generation;
            internal int MessageId;
            internal readonly TaskCompletionSource<NetworkPacket> Result = new TaskCompletionSource<NetworkPacket>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly object sync = new object ();
        private readonly INetworkTransport transport;
        private readonly NetworkClientOptions options;
        private readonly INetworkDispatcher dispatcher;
        private readonly QueuedNetworkDispatcher ownedDispatcher;
        private readonly NotificationRouter notifications = new NotificationRouter();
        private readonly Dictionary<long, Pending> pending = new Dictionary<long, Pending>();
        private readonly Random jitter = new Random(Guid.NewGuid().GetHashCode());
        private readonly Func<AuthenticationContext, CancellationToken, Task> authenticate;
        private CancellationTokenSource lifetime;
        private Task runTask, disposeTask;
        private TaskCompletionSource<bool> ready;
        private NetworkState state;
        private bool disposed;
        private long requestSeed, generation;
        private Exception closeError, lastError;
        /// <summary>使用调用方提供的transportOptions、options、dispatcher创建 NetworkSession。</summary>
        public NetworkSession(TcpClientOptions transportOptions, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null) : this(new SimpleTcpClient(transportOptions), options, dispatcher, authenticate)
        {
        }

        /// <summary>使用调用方提供的transport、options、dispatcher创建 NetworkSession。</summary>
        public NetworkSession(INetworkTransport transport, NetworkClientOptions options = null, INetworkDispatcher dispatcher = null, Func<AuthenticationContext, CancellationToken, Task> authenticate = null)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
            this.options = (options ?? new NetworkClientOptions()).Snapshot();
            this.dispatcher = dispatcher ?? (ownedDispatcher = new QueuedNetworkDispatcher());
            this.authenticate = authenticate;
            transport.PacketReceived += OnPacket;
            transport.Closed += OnClosed;
        }

        public NetworkState State
        {
            get
            {
                lock (sync)
                {
                    return state;
                }
            }
        }

        public bool IsConnected => State == NetworkState.Connected && transport.IsConnected;

        public long ConnectionId
        {
            get
            {
                lock (sync)
                {
                    return generation;
                }
            }
        }

        public Exception LastError
        {
            get
            {
                lock (sync)
                {
                    return lastError;
                }
            }
        }

        public int PendingRequestCount
        {
            get
            {
                lock (sync)
                {
                    return pending.Count;
                }
            }
        }

        public int PendingSendCount => transport.PendingSendCount;
        public int PendingSendBytes => transport.PendingSendBytes;

        public event Action Connected;
        public event Action<NetworkState> StateChanged;
        public event Action Reconnected;
        public event Action<Exception> Disconnected;
        public event Action<Exception> Error;
        // Only needed when no explicit dispatcher was supplied; call on the constructing thread.
        /// <summary>推进该对象的一次更新。</summary>
        public int Update(int maximumCallbacks = 64) => ownedDispatcher?.Pump(maximumCallbacks) ?? 0;
        // Cancellation belongs to this waiter. DisconnectAsync/Dispose stop the shared connection loop.
        /// <summary>异步连接网络会话。</summary>
        public async Task ConnectAsync(CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Task wait;
            TaskCompletionSource<bool> run = null;
            CancellationToken runningToken = default;
            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(NetworkSession));
                }

                if (IsConnected)
                {
                    return;
                }

                if (runTask == null || runTask.IsCompleted)
                {
                    lifetime?.Dispose();
                    lifetime = new CancellationTokenSource();
                    runningToken = lifetime.Token;
                    ready = NewReady();
                    run = NewReady();
                    // Publish before calling the transport, so concurrent callers join the same attempt.
                    runTask = run.Task;
                }
                else if (ready.Task.Status == TaskStatus.RanToCompletion)
                {
                    // Transport closure can precede the asynchronous disconnect/heartbeat
                    // cleanup that normally rotates readiness. A new waiter must await the
                    // replacement connection instead of the successful, retired generation.
                    ready = NewReady();
                }

                wait = ready.Task;
            }

            if (run != null)
            {
                _ = CompleteRunAsync(runningToken, run);
            }

            await AsyncOperation.WaitAsync(wait, ct).ConfigureAwait(false);
        }

        /// <summary>异步完成 Run。</summary>
        private async Task CompleteRunAsync(CancellationToken token, TaskCompletionSource<bool> completion)
        {
            try
            {
                await RunAsync(token).ConfigureAwait(false);
                completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                // Host dispatch can fail while RunAsync publishes its terminal state.
                // That must also fail current ConnectAsync waiters, even if RunAsync's
                // own finally could not reach its normal ready completion path.
                lock (sync)
                {
                    ready?.TrySetException(error);
                }

                completion.TrySetException(error);
            }
        }

        /// <summary>取得新就绪。</summary>
        private static TaskCompletionSource<bool> NewReady()
        {
            var value = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = value.Task.ContinueWith(task =>
            {
                _ = task.Exception;
            }, TaskContinuationOptions.OnlyOnFaulted);
            return value;
        }

        /// <summary>设置状态。</summary>
        private void SetState(NetworkState value)
        {
            lock (sync)
            {
                if (disposed || state == value)
                {
                    return;
                }

                state = value;
                Post(() => SafeInvoke(StateChanged, value));
            }
        }

        /// <summary>异步运行当前操作。</summary>
        private async Task RunAsync(CancellationToken ct)
        {
            bool wasReady = false;
            int attempts = 0;
            Exception failure = null;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    failure = null;
                    bool thisConnectionReady = false;
                    CancellationTokenSource connection = null;
                    Task heartbeat = null;
                    bool cleanupFailed = false;
                    try
                    {
                        SetState(wasReady || attempts != 0 ? NetworkState.Reconnecting : NetworkState.Connecting);
                        await transport.ConnectAsync(ct).ConfigureAwait(false);
                        lock (sync)
                        {
                            generation = transport.ConnectionId;
                            closeError = null;
                        }

                        connection = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        if (authenticate != null)
                        {
                            SetState(NetworkState.Authenticating);
                            using (var authentication = CancellationTokenSource.CreateLinkedTokenSource(connection.Token))
                            {
                                authentication.CancelAfter(options.AuthenticationTimeoutMilliseconds);
                                try
                                {
                                    await AsyncOperation.WaitAsync(authenticate(new AuthenticationContext(this, ConnectionId), authentication.Token), authentication.Token).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException) when (!ct.IsCancellationRequested && authentication.IsCancellationRequested)
                                {
                                    throw new TimeoutException("Authentication deadline expired.");
                                }
                            }
                        }

                        if (!transport.IsConnected)
                        {
                            throw new IOException("Connection closed during authentication.");
                        }

                        SetState(NetworkState.Connected);
                        bool reconnect = wasReady;
                        wasReady = true;
                        thisConnectionReady = true;
                        attempts = 0;
                        lock (sync)
                        {
                            lastError = null;
                            ready.TrySetResult(true);
                        }

                        long connectedGeneration = ConnectionId;
                        Post(() =>
                        {
                            if (!IsConnected || ConnectionId != connectedGeneration)
                            {
                                return;
                            }

                            SafeInvoke(Connected);
                            if (reconnect)
                            {
                                SafeInvoke(Reconnected);
                            }
                        });
                        heartbeat = HeartbeatAsync(connection.Token);
                        Task finished = await Task.WhenAny(transport.ConnectionCompletion, heartbeat).ConfigureAwait(false);
                        await finished.ConfigureAwait(false);
                        throw closeError ?? new IOException("TCP peer disconnected.");
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception error)
                    {
                        failure = error;
                        lock (sync)
                        {
                            lastError = error;
                        }

                        Report(error);
                    }
                    finally
                    {
                        CancelSafely(connection);
                        try
                        {
                            transport.Abort(failure);
                        }
                        catch (Exception error)
                        {
                            failure = error;
                            cleanupFailed = true;
                            Report(error);
                        }

                        FailPending(ConnectionId, failure ?? new IOException("Connection ended."));
                        try
                        {
                            await transport.DisconnectAsync().ConfigureAwait(false);
                        }
                        catch (Exception error)
                        {
                            failure = error;
                            cleanupFailed = true;
                            Report(error);
                        }

                        if (cleanupFailed)
                        {
                            lock (sync)
                            {
                                lastError = failure;
                            }
                        }

                        if (heartbeat != null)
                        {
                            try
                            {
                                await heartbeat.ConfigureAwait(false);
                            }
                            catch
                            {
                            }
                        }

                        connection?.Dispose();
                        if (thisConnectionReady)
                        {
                            lock (sync)
                            {
                                if (ready.Task.IsCompleted)
                                {
                                    ready = NewReady();
                                }
                            }

                            Exception reason = failure;
                            Post(() => SafeInvoke(Disconnected, reason));
                        }
                    }

                    if (cleanupFailed || ct.IsCancellationRequested || (!wasReady && !options.RetryInitialConnection) || !options.AutoReconnect || failure is ConnectionRejectedException)
                    {
                        break;
                    }

                    attempts++;
                    if (options.MaxReconnectAttempts != 0 && attempts > options.MaxReconnectAttempts)
                    {
                        break;
                    }

                    SetState(NetworkState.Reconnecting);
                    double delay = Math.Min(options.ReconnectMaximumMilliseconds, options.ReconnectMinimumMilliseconds * Math.Pow(2, Math.Min(attempts - 1, 16)));
                    delay = Math.Max(1, Math.Min(options.ReconnectMaximumMilliseconds, delay * (.5 + jitter.NextDouble())));
                    await Task.Delay((int)delay, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                SetState(NetworkState.Disconnected);
                lock (sync)
                {
                    ready?.TrySetException(failure ?? new IOException("Connection stopped."));
                }
            }
        }

        /// <summary>根据传入参数取得HeartbeatAsync。</summary>
        private async Task HeartbeatAsync(CancellationToken ct)
        {
            if (options.HeartbeatIntervalMilliseconds == 0)
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                return;
            }

            while (true)
            {
                await Task.Delay(options.HeartbeatIntervalMilliseconds, ct).ConfigureAwait(false);
                var response = await RequestCoreAsync(options.HeartbeatMessageId, null, ct, false).ConfigureAwait(false);
                CheckResult(response);
                if (response.Payload.Length != 0)
                {
                    throw new NetworkProtocolException("Heartbeat response must have no payload.");
                }
            }
        }

        /// <summary>根据传入参数取得请求 Async。</summary>
        public async Task<T> RequestAsync<T>(int messageId, IMessage message, MessageParser<T> parser, CancellationToken ct = default)
            where T : class, IMessage<T>
        {
            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }

            var response = await RequestCoreAsync(messageId, message, ct, false).ConfigureAwait(false);
            CheckResult(response);
            return parser.ParseFrom(response.Payload);
        }

        // Completion means bytes written. This does not await or retry the business response.
        /// <summary>异步发送网络会话。</summary>
        public async Task SendAsync(int messageId, IMessage message, CancellationToken ct = default)
        {
            long expectedGeneration = EnsureRequestAllowed(messageId, false);
            long id = NextRequestId();
            await transport.SendAsync(messageId, id, message, ct, expectedGeneration).ConfigureAwait(false);
        }

        /// <summary>检查并确保请求允许可用。</summary>
        private long EnsureRequestAllowed(int messageId, bool authenticating)
        {
            if (messageId <= 0 || messageId >= PacketCodec.NotificationMessageIdStart)
            {
                throw new ArgumentOutOfRangeException(nameof(messageId));
            }

            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(NetworkSession));
                }

                if (!transport.IsConnected || (!authenticating && state != NetworkState.Connected))
                {
                    throw new IOException("Await ConnectAsync before sending business requests.");
                }

                return generation;
            }
        }

        /// <summary>取得下一个请求标识。</summary>
        private long NextRequestId()
        {
            long value = Interlocked.Increment(ref requestSeed);
            if (value <= 0)
            {
                throw new InvalidOperationException("Request counter exhausted.");
            }

            return value;
        }

        /// <summary>根据传入参数取得请求核心 Async。</summary>
        private async Task<NetworkPacket> RequestCoreAsync(int messageId, IMessage message, CancellationToken ct, bool authenticating, long authenticationGeneration = 0)
        {
            ct.ThrowIfCancellationRequested();
            long expectedGeneration = EnsureRequestAllowed(messageId, authenticating);
            if (authenticationGeneration != 0 && expectedGeneration != authenticationGeneration)
            {
                throw new IOException("Authentication belongs to an old connection.");
            }

            long id = NextRequestId();
            var request = new Pending
            {
                Generation = expectedGeneration,
                MessageId = messageId
            };
            lock (sync)
            {
                if (pending.Count >= options.MaxPendingRequests)
                {
                    throw new PendingRequestLimitException();
                }

                pending.Add(id, request);
            }

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(options.RequestTimeoutMilliseconds);
                using (timeout.Token.Register(() =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        request.Result.TrySetCanceled(ct);
                    }
                    else
                    {
                        request.Result.TrySetException(new TimeoutException("Request response deadline expired."));
                    }
                }))
                {
                    try
                    {
                        await AsyncOperation.WaitAsync(transport.SendAsync(messageId, id, message, timeout.Token, expectedGeneration), timeout.Token).ConfigureAwait(false);
                        return await request.Result.Task.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeout.IsCancellationRequested)
                    {
                        throw new TimeoutException("Request deadline expired before completion.");
                    }
                    finally
                    {
                        lock (sync)
                        {
                            pending.Remove(id);
                        }

                        if (request.Result.Task.IsFaulted)
                        {
                            _ = request.Result.Task.Exception;
                        }
                    }
                }
            }
        }

        /// <summary>检查结果。</summary>
        private static void CheckResult(NetworkPacket response)
        {
            if (response.Header.ResultCode != 0)
            {
                throw new RemoteRequestException(response.Header);
            }
        }

        /// <summary>响应数据包。</summary>
        private void OnPacket(long packetGeneration, NetworkPacket packet)
        {
            if (packetGeneration != ConnectionId)
            {
                return;
            }

            if (PacketCodec.IsNotification(packet.Header))
            {
                var action = notifications.Capture(packet.Header.MessageId, packet.Payload, error =>
                {
                    Report(error);
                    if (ConnectionId == packetGeneration)
                    {
                        transport.Abort(new NetworkProtocolException("Invalid notification payload."));
                    }
                }, Report);
                if (action != null && !dispatcher.TryPost(() =>
                {
                    if (IsConnected && ConnectionId == packetGeneration)
                    {
                        action();
                    }
                }, packet.Payload.Length))
                {
                    var error = new IOException("Main-thread notification queue is full.");
                    lock (sync)
                    {
                        lastError = error;
                    }

                    transport.Abort(error);
                }

                return;
            }

            if (packet.Header.RequestId <= 0)
            {
                transport.Abort(new NetworkProtocolException("Invalid response request ID."));
                return;
            }

            Pending request;
            lock (sync)
            {
                if (!pending.TryGetValue(packet.Header.RequestId, out request) || request.Generation != packetGeneration)
                {
                    return;
                }

                pending.Remove(packet.Header.RequestId);
            }

            if (request.MessageId != packet.Header.MessageId)
            {
                var error = new NetworkProtocolException("Response message ID does not match its request.");
                request.Result.TrySetException(error);
                transport.Abort(error);
            }
            else
            {
                request.Result.TrySetResult(packet);
            }
        }

        /// <summary>响应已关闭。</summary>
        private void OnClosed(long closedGeneration, Exception error)
        {
            lock (sync)
            {
                if (generation == closedGeneration)
                {
                    closeError = error;
                }
            }

            FailPending(closedGeneration, error ?? new IOException("TCP peer closed."));
        }

        /// <summary>标记失败待处理。</summary>
        private void FailPending(long closedGeneration, Exception error)
        {
            lock (sync)
            {
                var ids = new List<long>();
                foreach (var pair in pending)
                {
                    if (pair.Value.Generation != closedGeneration)
                    {
                        continue;
                    }

                    pair.Value.Result.TrySetException(error);
                    ids.Add(pair.Key);
                }

                foreach (long id in ids)
                {
                    pending.Remove(id);
                }
            }
        }

        /// <summary>订阅网络会话。</summary>
        public IDisposable Subscribe<T>(int messageId, MessageParser<T> parser, Action<T> handler)
            where T : class, IMessage<T>
        {
            if (messageId < PacketCodec.NotificationMessageIdStart)
            {
                throw new ArgumentOutOfRangeException(nameof(messageId), "Subscribe is for server notifications; use RequestAsync for responses.");
            }

            lock (sync)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(NetworkSession));
                }
            }

            return notifications.Subscribe(messageId, parser, handler);
        }

        /// <summary>上报网络会话。</summary>
        private void Report(Exception error)
        {
            Post(() => SafeInvoke(Error, error));
        }

        /// <summary>投递网络会话。</summary>
        private void Post(Action callback)
        {
            dispatcher.TryPost(() =>
            {
                if (State != NetworkState.Disposed)
                {
                    callback();
                }
            }, 0);
        }

        /// <summary>调用 handlers.GetInvocationList 完成安全 Invoke。</summary>
        private void SafeInvoke(Action handlers)
        {
            if (handlers != null)
            {
                foreach (Action handler in handlers.GetInvocationList())
                {
                    try
                    {
                        handler();
                    }
                    catch (Exception error)
                    {
                        SafeInvoke(Error, error);
                    }
                }
            }
        }

        /// <summary>调用 handlers.GetInvocationList 完成安全 Invoke。</summary>
        private void SafeInvoke(Action<Exception> handlers, Exception value)
        {
            if (handlers != null)
            {
                foreach (Action<Exception> handler in handlers.GetInvocationList())
                {
                    try
                    {
                        handler(value);
                    }
                    catch
                    {
                    }
                }
            }
        }

        /// <summary>调用 handlers.GetInvocationList 完成安全 Invoke。</summary>
        private void SafeInvoke(Action<NetworkState> handlers, NetworkState value)
        {
            if (handlers == null)
            {
                return;
            }

            foreach (Action<NetworkState> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch (Exception error)
                {
                    SafeInvoke(Error, error);
                }
            }
        }

        /// <summary>取消安全地。</summary>
        private void CancelSafely(CancellationTokenSource source)
        {
            try
            {
                source?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception error)
            {
                Report(error);
            }
        }

        /// <summary>异步断开网络会话。</summary>
        public async Task DisconnectAsync()
        {
            Task task;
            CancellationTokenSource cancellation;
            lock (sync)
            {
                cancellation = lifetime;
                task = runTask;
                ready?.TrySetException(new IOException("Connection closed by the application."));
            }

            // Authentication callbacks can read session state; do not invoke them under sync.
            CancelSafely(cancellation);
            var errors = new List<Exception>();
            try
            {
                transport.Abort();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }

            if (task != null)
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            try
            {
                await transport.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                errors.Add(error);
            }

            if (errors.Count != 0)
            {
                throw new AggregateException("Network disconnect cleanup failed.", errors);
            }
        }

        /// <summary>结束网络会话对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            TaskCompletionSource<bool> completion;
            lock (sync)
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                state = NetworkState.Disposed;
                ready?.TrySetException(new ObjectDisposedException(nameof(NetworkSession)));
                completion = NewReady();
                disposeTask = completion.Task;
            }

            notifications.Clear();
            ownedDispatcher?.Dispose();
            _ = DisposeCoreAsync(completion);
        }

        /// <summary>异步释放网络会话。</summary>
        private async Task DisposeCoreAsync(TaskCompletionSource<bool> completion)
        {
            var errors = new List<Exception>();
            try
            {
                try
                {
                    await DisconnectAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }

                try
                {
                    await transport.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            finally
            {
                transport.PacketReceived -= OnPacket;
                transport.Closed -= OnClosed;
                lifetime?.Dispose();
            }

            if (errors.Count != 0)
            {
                completion.TrySetException(new AggregateException("Network disposal failed.", errors));
            }
            else
            {
                completion.TrySetResult(true);
            }
        }

        /// <summary>等待资源和后台操作完成异步清理。</summary>
        public async ValueTask DisposeAsync()
        {
            Dispose();
            Task task;
            lock (sync)
            {
                task = disposeTask;
            }

            await task.ConfigureAwait(false);
        }

        /// <summary>认证对应的上下文。携带一次操作所需的上下文。</summary>
        public sealed class AuthenticationContext
        {
            private readonly NetworkSession owner;
            /// <summary>使用调用方提供的owner、connectionId创建 AuthenticationContext。</summary>
            internal AuthenticationContext(NetworkSession owner, long connectionId)
            {
                this.owner = owner;
                ConnectionId = connectionId;
            }

            public long ConnectionId { get; }

            /// <summary>根据传入参数取得请求 Async。</summary>
            public async Task<T> RequestAsync<T>(int messageId, IMessage request, MessageParser<T> parser, CancellationToken ct)
                where T : class, IMessage<T>
            {
                if (parser == null)
                {
                    throw new ArgumentNullException(nameof(parser));
                }

                var response = await owner.RequestCoreAsync(messageId, request, ct, true, ConnectionId).ConfigureAwait(false);
                CheckResult(response);
                return parser.ParseFrom(response.Payload);
            }
        }
    }
}

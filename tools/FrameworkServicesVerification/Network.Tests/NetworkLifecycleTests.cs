using System.Collections.Concurrent;
using Game.Framework.Network.Dispatching;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Game.Framework.Network.Tests;
public sealed class NetworkLifecycleTests
{
    private static NetworkClientOptions Options() => new()
    {
        AutoReconnect = false,
        HeartbeatIntervalMilliseconds = 0,
        RequestTimeoutMilliseconds = 100,
        AuthenticationTimeoutMilliseconds = 2000
    };
    [Fact]
    public async Task CancellingOneConnectWaiterPreservesOtherWaitersAndTheSharedConnection()
    {
        using var peer = new FakePeer();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new NetworkManager(peer.Options, Options(), authenticate: (_, _) =>
        {
            entered.TrySetResult();
            return release.Task;
        });
        using var stop = new CancellationTokenSource();
        var first = client.ConnectAsync(stop.Token);
        using var socket = await peer.AcceptAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        long originalConnection = client.ConnectionId;
        var second = client.ConnectAsync();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(second.IsCompleted);
        Assert.Equal(NetworkState.Authenticating, client.State);
        release.SetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(client.IsConnected);
        Assert.Equal(originalConnection, client.ConnectionId);
    }

    [Fact]
    public async Task StatusEventsReportTransitionsAndObserverErrorsDoNotDisconnect()
    {
        using var peer = new FakePeer();
        var dispatcher = new Dispatcher();
        await using var client = new NetworkManager(peer.Options, Options(), dispatcher);
        var states = new List<NetworkState>();
        int errors = 0;
        client.StateChanged += _ => throw new InvalidOperationException("observer");
        client.StateChanged += states.Add;
        client.Error += _ => errors++;
        await client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        dispatcher.Drain();
        Assert.Equal(new[] { NetworkState.Connecting, NetworkState.Connected }, states);
        Assert.Equal(2, errors);
        Assert.True(client.IsConnected);
        await client.DisconnectAsync();
        dispatcher.Drain();
        Assert.Equal(NetworkState.Disconnected, states[^1]);
    }

    [Fact]
    public async Task RequestTimeoutRemovesPendingAndLateResponseCannotCompleteANewRequest()
    {
        using var peer = new FakePeer();
        await using var client = new NetworkManager(peer.Options, Options());
        await client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        var first = client.RequestAsync<Int32Value>(50, new Int32Value());
        var old = await FakePeer.ReadAsync(socket.GetStream());
        await Assert.ThrowsAsync<TimeoutException>(() => first);
        Assert.Equal(0, client.PendingRequestCount);
        await socket.GetStream().WriteAsync(FakePeer.Encode(50, old.Header.RequestId, new Int32Value { Value = 999 }));
        var second = client.RequestAsync<Int32Value>(50, new Int32Value());
        var current = await FakePeer.ReadAsync(socket.GetStream());
        await socket.GetStream().WriteAsync(FakePeer.Encode(50, current.Header.RequestId, new Int32Value { Value = 2 }));
        Assert.Equal(2, (await second).Value);
        Assert.Equal(0, client.PendingRequestCount);
    }

    [Fact]
    public async Task RequestDeadlineAlsoCoversATransportThatIgnoresSendCancellation()
    {
        var transport = new ControlledTransport
        {
            HoldSends = true
        };
        await using var client = new NetworkManager(transport, Options());
        await client.ConnectAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => client.RequestAsync<Int32Value>(50, new Int32Value()).WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, client.PendingRequestCount);
        transport.SendGate.SetResult();
    }

    [Fact]
    public async Task AuthenticationTimeoutDoesNotWaitForAnUncooperativeProvider()
    {
        using var peer = new FakePeer();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var options = Options();
        options.AuthenticationTimeoutMilliseconds = 100;
        await using var client = new NetworkManager(peer.Options, options, authenticate: (_, _) => release.Task);
        var connecting = client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => connecting.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(client.IsConnected);
            await FakePeer.AssertClosedAsync(socket);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task ExplicitDisconnectStopsRetryBackoffAndDoesNotReconnectLater()
    {
        using var peer = new FakePeer();
        var options = Options();
        options.AutoReconnect = true;
        options.ReconnectMinimumMilliseconds = options.ReconnectMaximumMilliseconds = 3000;
        await using var client = new NetworkManager(peer.Options, options);
        await client.ConnectAsync();
        var socket = await peer.AcceptAsync();
        socket.Dispose();
        await Until(() => client.State == NetworkState.Reconnecting);
        await client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(NetworkState.Disconnected, client.State);
        await Task.Delay(100);
        Assert.Equal(NetworkState.Disconnected, client.State);
    }

    [Fact]
    public async Task ReconnectAttemptsAreBounded()
    {
        using var peer = new FakePeer();
        var options = Options();
        options.AutoReconnect = options.RetryInitialConnection = true;
        options.ReconnectMinimumMilliseconds = options.ReconnectMaximumMilliseconds = 1;
        options.MaxReconnectAttempts = 2;
        int attempts = 0;
        await using var client = new NetworkManager(peer.Options, options, authenticate: (_, _) =>
        {
            Interlocked.Increment(ref attempts);
            return Task.FromException(new IOException("retry"));
        });
        await Assert.ThrowsAsync<IOException>(() => client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(3, attempts);
        Assert.Equal(NetworkState.Disconnected, client.State);
    }

    [Fact]
    public async Task ReconnectionFailsOldRequestsAndNeverAutomaticallyReplaysThem()
    {
        using var peer = new FakePeer();
        var options = Options();
        options.AutoReconnect = true;
        options.ReconnectMinimumMilliseconds = options.ReconnectMaximumMilliseconds = 1;
        await using var client = new NetworkManager(peer.Options, options);
        await client.ConnectAsync();
        var original = await peer.AcceptAsync();
        long connection = client.ConnectionId;
        var request = client.RequestAsync<Int32Value>(50, new Int32Value { Value = 1 });
        await FakePeer.ReadAsync(original.GetStream());
        original.Dispose();
        await Assert.ThrowsAnyAsync<IOException>(() => request);
        using var next = await peer.AcceptAsync();
        await Until(() => client.IsConnected && client.ConnectionId != connection);
        await Task.Delay(30);
        Assert.False(next.GetStream().DataAvailable);
        Assert.Equal(0, client.PendingRequestCount);
        var fresh = client.RequestAsync<Int32Value>(50, new Int32Value { Value = 2 });
        var packet = await FakePeer.ReadAsync(next.GetStream());
        await next.GetStream().WriteAsync(FakePeer.Encode(50, packet.Header.RequestId, new Int32Value { Value = 2 }));
        Assert.Equal(2, (await fresh).Value);
    }

    [Fact]
    public async Task ThrowingCancellationCallbacksCannotPreventDisconnection()
    {
        using var peer = new FakePeer();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = new NetworkManager(peer.Options, Options(), authenticate: (_, token) =>
        {
            token.Register(() => throw new InvalidOperationException("cancel callback"));
            entered.SetResult();
            return release.Task;
        });
        var connecting = client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(3));
        await Assert.ThrowsAnyAsync<Exception>(() => connecting);
        release.TrySetResult();
        await FakePeer.AssertClosedAsync(socket);
        Assert.Equal(NetworkState.Disconnected, client.State);
    }

    [Fact]
    public async Task DisposalStillReleasesTransportWhenDisconnectThrows()
    {
        var transport = new ControlledTransport
        {
            FailDisconnect = true
        };
        var client = new NetworkManager(transport, Options());
        await client.ConnectAsync();
        await Assert.ThrowsAsync<AggregateException>(() => client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(transport.Disposed);
        Assert.Equal(NetworkState.Disposed, client.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(3000);
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }

    private sealed class Dispatcher : INetworkDispatcher
    {
        private readonly ConcurrentQueue<Action> pending = new();
        public bool TryPost(Action callback, int bytes)
        {
            pending.Enqueue(callback);
            return true;
        }

        internal void Drain()
        {
            while (pending.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private sealed class ControlledTransport : INetworkTransport
    {
        private readonly TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool HoldSends, FailDisconnect, Disposed;
        public bool IsConnected { get; private set; }
        public long ConnectionId => 1;
        public Task ConnectionCompletion => completed.Task;
        public int PendingSendCount => 0;
        public int PendingSendBytes => 0;

        public event Action<long, NetworkPacket>? PacketReceived;
        public event Action<long, Exception>? Closed;
        public Task ConnectAsync(CancellationToken token = default)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public void Abort(Exception? error = null)
        {
            bool wasConnected = IsConnected;
            IsConnected = false;
            if (wasConnected)
            {
                Closed?.Invoke(1, error ?? new IOException("closed"));
            }

            completed.TrySetResult();
        }

        public Task DisconnectAsync()
        {
            Abort();
            return FailDisconnect ? Task.FromException(new IOException("disconnect")) : Task.CompletedTask;
        }

        public Task SendAsync(int id, long request, IMessage message, CancellationToken token = default, long expected = 0) => HoldSends ? SendGate.Task : Task.CompletedTask;
        internal void Receive(NetworkPacket packet) => PacketReceived?.Invoke(1, packet);
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}

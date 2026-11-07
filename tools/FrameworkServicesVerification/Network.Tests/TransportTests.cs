using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using Game.Framework.Network.Dispatching;
using Game.Framework.Network.Protocol;
using Xunit;

namespace Game.Framework.Network.Tests;
public sealed class TransportTests
{
    [Fact]
    public async Task AwaitingSendReleasesTheSingleQueueSlotBeforeTheNextSend()
    {
        using var peer = new FakePeer();
        var options = peer.Options;
        options.MaxPendingSends = 1;
        await using var client = new SimpleTcpClient(options);
        await client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        for (int i = 1; i <= 200; i++)
        {
            await client.SendRawAsync(50, i, [1, 2, 3]);
            Assert.Equal(0, client.PendingSendCount);
            Assert.Equal(0, client.PendingSendBytes);
            Assert.Equal(i, (await FakePeer.ReadAsync(socket.GetStream())).Header.RequestId);
        }
    }

    [Fact]
    public async Task Ipv6LoopbackIsSupportedForAppleIpv6OnlyNetworks()
    {
        if (!Socket.OSSupportsIPv6)
        {
            return;
        }

        var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        try
        {
            await using var client = new SimpleTcpClient(new TcpClientOptions { Host = "::1", Port = ((IPEndPoint)listener.LocalEndpoint).Port, Encryption = PacketEncryptionMode.None });
            await client.ConnectAsync();
            using var peer = await listener.AcceptTcpClientAsync();
            await client.SendRawAsync(50, 1, [1, 2, 3]);
            Assert.Equal(new byte[] { 1, 2, 3 }, (await FakePeer.ReadAsync(peer.GetStream())).Payload);
            await client.DisconnectAsync();
            await FakePeer.AssertClosedAsync(peer);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task FragmentedAndCoalescedPacketsRetainIndependentPayloadOwnership()
    {
        using var peer = new FakePeer();
        await using var client = new SimpleTcpClient(peer.Options);
        var packets = new System.Collections.Concurrent.ConcurrentQueue<NetworkPacket>();
        client.PacketReceived += (_, packet) => packets.Enqueue(packet);
        await client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        byte[] first = new byte[28 + 1500], second = new byte[28 + 3];
        PacketCodec.WriteHeader(first, new(10, 1, 0, 1500));
        first.AsSpan(28).Fill(42);
        PacketCodec.WriteHeader(second, new(11, 2, 0, 3));
        second.AsSpan(28).Fill(7);
        byte[] combined = [..first, ..second];
        for (int offset = 0; offset < combined.Length; offset += 3)
        {
            await socket.GetStream().WriteAsync(combined.AsMemory(offset, Math.Min(3, combined.Length - offset)));
        }

        await WaitUntilAsync(() => packets.Count == 2);
        var result = packets.ToArray();
        Assert.Equal(1, result[0].Header.RequestId);
        Assert.All(result[0].Payload, value => Assert.Equal(42, value));
        Assert.Equal(2, result[1].Header.RequestId);
        Assert.Equal(new byte[] { 7, 7, 7 }, result[1].Payload);
    }

    [Fact]
    public async Task RepeatedDisconnectReconnectAndDisposeClosePeersAndQueues()
    {
        using var peer = new FakePeer();
        var client = new SimpleTcpClient(peer.Options);
        int closed = 0;
        client.Closed += (_, _) => Interlocked.Increment(ref closed);
        long previous = 0;
        for (int i = 0; i < 12; i++)
        {
            await client.ConnectAsync();
            using var socket = await peer.AcceptAsync();
            Assert.True(client.ConnectionId > previous);
            previous = client.ConnectionId;
            await client.SendRawAsync(50, i + 1, [(byte)i]);
            var received = await FakePeer.ReadAsync(socket.GetStream());
            Assert.Equal(i, received.Payload[0]);
            await client.DisconnectAsync();
            await FakePeer.AssertClosedAsync(socket);
            Assert.False(client.IsConnected);
            Assert.Equal(0, client.PendingSendCount);
            Assert.Equal(0, client.PendingSendBytes);
        }

        await client.ConnectAsync();
        using var last = await peer.AcceptAsync();
        client.Dispose();
        Assert.False(client.IsConnected);
        await client.DisposeAsync();
        await client.DisposeAsync();
        await FakePeer.AssertClosedAsync(last);
        Assert.Equal(13, closed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync());
    }

    [Fact]
    public async Task ConcurrentConnectAndDisconnectHaveOnePhysicalConnection()
    {
        using var peer = new FakePeer();
        await using var client = new SimpleTcpClient(peer.Options);
        int closed = 0;
        client.Closed += (_, _) => closed++;
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.ConnectAsync()));
        using var socket = await peer.AcceptAsync();
        Assert.Equal(1, client.ConnectionId);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.DisconnectAsync()));
        await FakePeer.AssertClosedAsync(socket);
        Assert.Equal(1, closed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrDisposeDuringHandshakeDoesNotHangOrLeaveASocket(bool dispose)
    {
        using var peer = new FakePeer();
        var options = peer.Options;
        options.Encryption = PacketEncryptionMode.AesGcm;
        options.KeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var client = new SimpleTcpClient(options);
        Task connecting = client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        var watch = Stopwatch.StartNew();
        if (dispose)
        {
            client.Dispose();
            await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
        else
        {
            await client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }

        Assert.NotNull(await Record.ExceptionAsync(() => connecting));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.False(client.IsConnected);
        await FakePeer.AssertClosedAsync(socket);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task MissingHelloHonorsConnectDeadline()
    {
        using var peer = new FakePeer();
        var options = peer.Options;
        options.Encryption = PacketEncryptionMode.AesGcm;
        options.KeyBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        options.ConnectTimeoutMilliseconds = 100;
        await using var client = new SimpleTcpClient(options);
        Task connect = client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        Assert.NotNull(await Record.ExceptionAsync(() => connect.WaitAsync(TimeSpan.FromSeconds(2))));
        Assert.False(client.IsConnected);
        await FakePeer.AssertClosedAsync(socket);
    }

    [Fact]
    public async Task SlowPartialHeaderHitsAssemblyDeadline()
    {
        using var peer = new FakePeer();
        var options = peer.Options;
        options.PacketTimeoutMilliseconds = 100;
        await using var client = new SimpleTcpClient(options);
        await client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        await socket.GetStream().WriteAsync(new byte[] { 0x47 });
        await client.ConnectionCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(client.IsConnected);
        await FakePeer.AssertClosedAsync(socket);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task BadLengthClosesWithoutAllocatingAdvertisedPayload(int length)
    {
        using var peer = new FakePeer();
        await using var client = new SimpleTcpClient(peer.Options);
        Exception? error = null;
        client.Closed += (_, value) => error = value;
        await client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        byte[] header = FakePeer.Encode(50, 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), length);
        await socket.GetStream().WriteAsync(header);
        await client.ConnectionCompletion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<NetworkProtocolException>(error);
        await FakePeer.AssertClosedAsync(socket);
    }

    [Fact]
    public async Task SendQueueRejectsOverflowWithExplicitFailureAndStaysWithinBudget()
    {
        using var peer = new FakePeer();
        var options = peer.Options;
        options.MaxPacketBytes = 65536;
        options.MaxPendingSendBytes = 65536;
        options.MaxPendingSends = 1;
        await using var client = new SimpleTcpClient(options);
        await client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        byte[] payload = new byte[65536 - 28];
        int rejected = 0;
        var sends = new List<Task>();
        for (int i = 0; i < 128; i++)
        {
            try
            {
                sends.Add(client.SendRawAsync(50, i + 1, payload));
            }
            catch (SendQueueFullException)
            {
                rejected++;
            }

            Assert.InRange(client.PendingSendCount, 0, 1);
            Assert.InRange(client.PendingSendBytes, 0, 65536);
        }

        Assert.True(rejected > 0);
        await client.DisconnectAsync();
        foreach (Task send in sends)
        {
            await Record.ExceptionAsync(() => send);
        }

        Assert.Equal(0, client.PendingSendCount);
        Assert.Equal(0, client.PendingSendBytes);
    }

    [Fact]
    public async Task QueuedCancellationReturnsPromptlyBehindBlockedSend()
    {
        using var peer = new FakePeer();
        var options = peer.Options;
        options.MaxPacketBytes = 4 * 1024 * 1024;
        options.MaxPendingSendBytes = 12 * 1024 * 1024;
        options.SendTimeoutMilliseconds = 30_000;
        await using var client = new SimpleTcpClient(options);
        await client.ConnectAsync();
        using var socket = await peer.AcceptAsync();
        socket.ReceiveBufferSize = 1024;
        byte[] payload = new byte[options.MaxPacketBytes - 28];
        Task first = client.SendRawAsync(50, 1, payload);
        Assert.False(first.IsCompleted);
        using var cancellation = new CancellationTokenSource();
        Task second = client.SendRawAsync(50, 2, payload, cancellation.Token);
        cancellation.Cancel();
        // Check independence from the blocked writer, rather than a CI scheduler's
        // ability to run two asynchronous continuations within 500 milliseconds.
        // The guard is shorter than the write deadline, so waiting for that writer
        // still fails; the postcondition also rules out cancellation via its exit.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(first.IsCompleted);
        await client.DisconnectAsync();
        await Record.ExceptionAsync(() => first);
    }

    [Fact]
    public void DispatcherEnforcesOwnerThreadAndBoundedRetainedPayload()
    {
        using var dispatcher = new QueuedNetworkDispatcher(2, 8);
        int called = 0;
        Assert.True(dispatcher.TryPost(() => called++, 8));
        Assert.False(dispatcher.TryPost(() => called++, 1));
        Assert.Equal(8, dispatcher.PendingPayloadBytes);
        Exception? wrongThreadError = null;
        var wrongThread = new Thread(() =>
        {
            try
            {
                dispatcher.Pump();
            }
            catch (Exception error)
            {
                wrongThreadError = error;
            }
        });
        wrongThread.Start();
        wrongThread.Join();
        Assert.IsType<InvalidOperationException>(wrongThreadError);
        Assert.Equal(0, called);
        Assert.Equal(1, dispatcher.Pump());
        Assert.Equal(1, called);
        Assert.Equal(0, dispatcher.PendingPayloadBytes);
        dispatcher.Dispose();
        Assert.False(dispatcher.TryPost(() => called++, 0));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(5000);
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}

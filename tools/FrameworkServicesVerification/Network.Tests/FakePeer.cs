using System.Net;
using System.Net.Sockets;
using Game.Framework.Network.Protocol;
using Google.Protobuf;
using ProtoMessage = Google.Protobuf.IMessage;

namespace Game.Framework.Network.Tests;
internal sealed class FakePeer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly List<TcpClient> clients = [];
    internal FakePeer()
    {
        listener.Start();
    }

    internal int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    internal TcpClientOptions Options => new()
    {
        Port = Port,
        Encryption = PacketEncryptionMode.None
    };

    internal async Task<TcpClient> AcceptAsync()
    {
        using var timeout = new CancellationTokenSource(5000);
        var client = await listener.AcceptTcpClientAsync(timeout.Token);
        client.NoDelay = true;
        clients.Add(client);
        return client;
    }

    internal static async Task<NetworkPacket> ReadAsync(Stream stream)
    {
        using var timeout = new CancellationTokenSource(5000);
        byte[] bytes = new byte[28];
        await stream.ReadExactlyAsync(bytes, timeout.Token);
        if (!PacketCodec.TryReadHeader(bytes, 4 * 1024 * 1024, out var header))
        {
            throw new IOException("Invalid test packet");
        }

        byte[] body = new byte[header.PayloadLength];
        await stream.ReadExactlyAsync(body, timeout.Token);
        return new(header, body);
    }

    internal static byte[] Encode(int messageId, long requestId, ProtoMessage? message = null, int result = 0)
    {
        byte[] bytes = new byte[28 + (message?.CalculateSize() ?? 0)];
        PacketCodec.WriteHeader(bytes, new(messageId, requestId, result, bytes.Length - 28));
        message?.WriteTo(bytes.AsSpan(28));
        return bytes;
    }

    internal static async Task AssertClosedAsync(TcpClient client)
    {
        using var timeout = new CancellationTokenSource(2000);
        try
        {
            Xunit.Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1], timeout.Token));
        }
        catch (IOException)
        {
        }
    }

    public void Dispose()
    {
        foreach (var client in clients)
        {
            client.Dispose();
        }

        listener.Stop();
    }
}

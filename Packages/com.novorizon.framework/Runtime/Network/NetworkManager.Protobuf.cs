using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Protocol;
using Google.Protobuf;

namespace Game.Framework.Network
{
    // Normal application calls select a response type; parser ownership stays inside the library.
    public sealed partial class NetworkManager
    {
        public Task<T> RequestAsync<T>(int messageId, IMessage message, CancellationToken ct = default)
            where T : class, IMessage<T>, new() =>
            RequestAsync(messageId, message, MessageParserCache<T>.Instance, ct);

        private static int GetMessageId(Enum messageId) =>
            messageId != null ? Convert.ToInt32(messageId) : throw new ArgumentNullException(nameof(messageId));

        public Task<T> RequestAsync<T>(Enum messageId, IMessage message, CancellationToken ct = default)
            where T : class, IMessage<T>, new() => RequestAsync<T>(GetMessageId(messageId), message, ct);

        public IDisposable Subscribe<T>(int messageId, Action<T> handler)
            where T : class, IMessage<T>, new() => Subscribe(messageId, MessageParserCache<T>.Instance, handler);

        public IDisposable Subscribe<T>(Enum messageId, Action<T> handler)
            where T : class, IMessage<T>, new() => Subscribe<T>(GetMessageId(messageId), handler);

        public Task SendAsync(Enum messageId, IMessage message, CancellationToken ct = default) =>
            SendAsync(GetMessageId(messageId), message, ct);
    }
}

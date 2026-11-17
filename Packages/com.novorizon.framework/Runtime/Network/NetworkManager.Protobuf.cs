using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Network.Protocol;
using Google.Protobuf;

namespace Game.Framework.Network
{
    // Normal application calls select a response type; parser ownership stays inside the library.
    /// <summary>网络对应的管理器。管理对应模块的状态和操作入口。</summary>
    public sealed partial class NetworkManager
    {
        /// <summary>根据传入参数取得请求 Async。</summary>
        public Task<T> RequestAsync<T>(int messageId, IMessage message, CancellationToken ct = default)
            where T : class, IMessage<T>, new() =>
            RequestAsync(messageId, message, MessageParserCache<T>.Instance, ct);

        /// <summary>获取消息标识。</summary>
        private static int GetMessageId(Enum messageId) =>
            messageId != null ? Convert.ToInt32(messageId) : throw new ArgumentNullException(nameof(messageId));

        /// <summary>根据传入参数取得请求 Async。</summary>
        public Task<T> RequestAsync<T>(Enum messageId, IMessage message, CancellationToken ct = default)
            where T : class, IMessage<T>, new() => RequestAsync<T>(GetMessageId(messageId), message, ct);

        /// <summary>订阅网络管理器。</summary>
        public IDisposable Subscribe<T>(int messageId, Action<T> handler)
            where T : class, IMessage<T>, new() => Subscribe(messageId, MessageParserCache<T>.Instance, handler);

        /// <summary>订阅网络管理器。</summary>
        public IDisposable Subscribe<T>(Enum messageId, Action<T> handler)
            where T : class, IMessage<T>, new() => Subscribe<T>(GetMessageId(messageId), handler);

        /// <summary>异步发送网络管理器。</summary>
        public Task SendAsync(Enum messageId, IMessage message, CancellationToken ct = default) =>
            SendAsync(GetMessageId(messageId), message, ct);
    }
}

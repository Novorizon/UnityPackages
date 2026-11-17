using Google.Protobuf;

namespace Game.Framework.Network.Protocol
{
    // One parser per response type. Generated messages expose public parameterless constructors.
    // A direct generic factory avoids reflecting over the generated static Parser property.
    /// <summary>提供消息解析器缓存的实现。</summary>
    internal static class MessageParserCache<T> where T : class, IMessage<T>, new()
    {
        internal static readonly MessageParser<T> Instance = new MessageParser<T>(Create);

        /// <summary>创建消息解析器缓存。</summary>
        private static T Create() => new T();
    }
}

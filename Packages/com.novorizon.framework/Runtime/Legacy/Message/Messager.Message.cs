using System;
namespace Game.Framework
{
    /// <summary>提供消息入口的实现。</summary>
    public partial class Messager
    {
        /// <summary>订阅消息入口。</summary>
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage =>
            new Subscription(Bus.Subscribe(topic, handler));
        /// <summary>订阅消息入口。</summary>
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Func<TMessage, bool> filter, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage =>
            new Subscription(Bus.Subscribe(topic, filter, handler));
        /// <summary>通知消息入口。</summary>
        public void Notify<TTopic, TMessage>(TTopic topic, TMessage message) where TTopic : Enum where TMessage : IMessage => Bus.Notify(topic, message);
        /// <summary>取消订阅消息入口。</summary>
        public void Unsubscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage => Bus.Unsubscribe(topic, handler);
    }
}

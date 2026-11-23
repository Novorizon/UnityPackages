using System;
namespace Game.Framework
{
    public partial class Messager
    {
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage =>
            new Subscription(Event.EventManager.Subscribe(topic, handler));
        /// <summary>订阅消息入口。</summary>
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Func<TMessage, bool> filter, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage =>
            new Subscription(Event.EventManager.Subscribe(topic, filter, handler));
        /// <summary>通知消息入口。</summary>
        public void Notify<TTopic, TMessage>(TTopic topic, TMessage message) where TTopic : Enum where TMessage : IMessage => Event.EventManager.Notify(topic, message);
        /// <summary>取消订阅消息入口。</summary>
        public void Unsubscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage => Event.EventManager.Unsubscribe(topic, handler);
    }
}

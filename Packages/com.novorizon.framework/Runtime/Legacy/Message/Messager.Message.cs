using System;
namespace Game.Framework
{
    public partial class Messager
    {
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage =>
            new Subscription(Bus.Subscribe(topic, handler));
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Func<TMessage, bool> filter, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage =>
            new Subscription(Bus.Subscribe(topic, filter, handler));
        public void Notify<TTopic, TMessage>(TTopic topic, TMessage message) where TTopic : Enum where TMessage : IMessage => Bus.Notify(topic, message);
        public void Unsubscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage => Bus.Unsubscribe(topic, handler);
    }
}

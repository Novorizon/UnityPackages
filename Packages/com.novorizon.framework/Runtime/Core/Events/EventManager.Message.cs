using System;
namespace Game.Framework.Event
{
    public partial class EventManager
    {
        public ISubscription Subscribe<TMessage>(Enum topic, Action<TMessage> handler) where TMessage : IMessage =>
            Register(topic, typeof(TMessage), handler, value => handler((TMessage)value));
        public void Notify<TMessage>(Enum topic, TMessage message) where TMessage : IMessage
        { if (message != null) Publish(topic, typeof(TMessage), message); }
        public void Unsubscribe<TMessage>(Enum topic, Action<TMessage> handler) where TMessage : IMessage =>
            Remove(topic, typeof(TMessage), handler);
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler)
            where TTopic : Enum where TMessage : IMessage => Register(topic, typeof(TMessage), handler, value => handler((TMessage)value));
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Func<TMessage, bool> filter, Action<TMessage> handler)
            where TTopic : Enum where TMessage : IMessage
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            return Register(topic, typeof(TMessage), handler, value => { var message = (TMessage)value; if (filter(message)) handler(message); });
        }
        public void Notify<TTopic, TMessage>(TTopic topic, TMessage message) where TTopic : Enum where TMessage : IMessage
        { if (message != null) Publish(topic, typeof(TMessage), message); }
        public void Unsubscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage =>
            Remove(topic, typeof(TMessage), handler);
    }
}

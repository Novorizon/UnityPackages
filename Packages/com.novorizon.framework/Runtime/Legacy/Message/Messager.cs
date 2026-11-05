using System;
namespace Game.Framework
{
    [Obsolete("Use Game.Framework.Event.EventManager. This facade shares the same event backend.")]
    public partial class Messager : Singleton<Messager>
    {
        private Event.EventManager Bus => Event.EventManager.Instance;
        public ISubscription Subscribe<TTopic>(TTopic topic, Action handler) where TTopic : Enum => new Subscription(Bus.Subscribe(topic, handler));
        public void Unsubscribe<TTopic>(TTopic topic, Action handler) where TTopic : Enum => Bus.Unsubscribe(topic, handler);
        public void Unsubscribe<TTopic>(TTopic topic) where TTopic : Enum => Bus.Unsubscribe(topic);
        public void Notify<TTopic>(TTopic topic) where TTopic : Enum => Bus.Notify(topic);
        public void Clear() => Bus.Clear();
    }
}

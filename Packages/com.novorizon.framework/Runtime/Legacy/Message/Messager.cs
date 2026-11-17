using System;
namespace Game.Framework
{
    /// <summary>提供消息入口的实现。</summary>
    [Obsolete("Use Game.Framework.Event.EventManager. This facade shares the same event backend.")]
    public partial class Messager : Singleton<Messager>
    {
        private Event.EventManager Bus => Event.EventManager.Instance;
        /// <summary>订阅消息入口。</summary>
        public ISubscription Subscribe<TTopic>(TTopic topic, Action handler) where TTopic : Enum => new Subscription(Bus.Subscribe(topic, handler));
        /// <summary>取消订阅消息入口。</summary>
        public void Unsubscribe<TTopic>(TTopic topic, Action handler) where TTopic : Enum => Bus.Unsubscribe(topic, handler);
        /// <summary>取消订阅消息入口。</summary>
        public void Unsubscribe<TTopic>(TTopic topic) where TTopic : Enum => Bus.Unsubscribe(topic);
        /// <summary>通知消息入口。</summary>
        public void Notify<TTopic>(TTopic topic) where TTopic : Enum => Bus.Notify(topic);
        /// <summary>清空消息入口。</summary>
        public void Clear() => Bus.Clear();
    }
}

using System;
namespace Game.Framework
{
    [Obsolete("Use Game.Framework.Event.EventManager. This facade shares the same event backend.")]
    public partial class Messager : Singleton<Messager>
    {
        /// <summary>订阅消息入口。</summary>
        public ISubscription Subscribe<TTopic>(TTopic topic, Action handler) where TTopic : Enum => new Subscription(Event.EventManager.Subscribe(topic, handler));
        /// <summary>取消订阅消息入口。</summary>
        public void Unsubscribe<TTopic>(TTopic topic, Action handler) where TTopic : Enum => Event.EventManager.Unsubscribe(topic, handler);
        /// <summary>取消订阅消息入口。</summary>
        public void Unsubscribe<TTopic>(TTopic topic) where TTopic : Enum => Event.EventManager.Unsubscribe(topic);
        /// <summary>通知消息入口。</summary>
        public void Notify<TTopic>(TTopic topic) where TTopic : Enum => Event.EventManager.Notify(topic);
        /// <summary>清空消息入口。</summary>
        public void Clear() => Event.EventManager.Clear();
    }
}

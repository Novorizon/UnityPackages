#nullable enable
using System;
using System.Collections.Generic;

namespace Game.Framework.Event
{
    /// <summary>提供消息总线的实现。</summary>
    public class MessageBus
    {
        /// <summary>表示消息总线中的绑定。</summary>
        sealed class Binding<T>
        {
            internal Action<T>[] Snapshot = Array.Empty<Action<T>>();
        }

        // A bus is owned and synchronously driven by its caller. Publishing retains a stable snapshot even
        // when a callback subscribes, unsubscribes, clears, or publishes recursively.
        readonly Dictionary<Type, object> handlers = new();

        /// <summary>表示消息总线中的订阅。</summary>
        sealed class Subscription<T> : IDisposable
        {
            MessageBus? owner;
            Action<T>? handler;
            readonly Action<T> callback;

            /// <summary>使用调用方提供的owner、handler创建 Subscription。</summary>
            internal Subscription(MessageBus owner, Action<T> handler)
            {
                this.owner = owner;
                this.handler = handler;
                callback = Invoke;
                owner.Subscribe(callback);
            }

            /// <summary>调用已经登记的处理逻辑。</summary>
            void Invoke(T message) => handler?.Invoke(message);

            /// <summary>结束订阅对相关资源的持有，并执行该类型的清理流程。</summary>
            public void Dispose()
            {
                MessageBus? bus = owner;
                if (bus == null) return;
                owner = null;
                handler = null;
                bus.Unsubscribe(callback);
            }
        }

        /// <summary>An independently owned subscription suitable for UIView.Track. Disposal also suppresses an in-flight snapshot callback.</summary>
        /// <remarks>订阅 Scoped。</remarks>
        public IDisposable SubscribeScoped<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return new Subscription<T>(this, handler);
        }

        /// <summary>订阅消息总线。</summary>
        public void Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Type type = typeof(T);
            if (!handlers.TryGetValue(type, out object? value))
            {
                value = new Binding<T>();
                handlers.Add(type, value);
            }

            var binding = (Binding<T>)value;
            Action<T>[] previous = binding.Snapshot;
            if (Array.IndexOf(previous, handler) >= 0) return;
            var snapshot = new Action<T>[previous.Length + 1];
            Array.Copy(previous, snapshot, previous.Length);
            snapshot[previous.Length] = handler;
            binding.Snapshot = snapshot;
        }

        /// <summary>取消订阅消息总线。</summary>
        public void Unsubscribe<T>(Action<T> handler)
        {
            Type type = typeof(T);
            if (!handlers.TryGetValue(type, out object? value))
            {
                return;
            }

            var binding = (Binding<T>)value;
            Action<T>[] previous = binding.Snapshot;
            int index = Array.IndexOf(previous, handler);
            if (index < 0) return;
            if (previous.Length == 1)
            {
                handlers.Remove(type);
                return;
            }

            var snapshot = new Action<T>[previous.Length - 1];
            if (index > 0) Array.Copy(previous, 0, snapshot, 0, index);
            if (index < snapshot.Length)
                Array.Copy(previous, index + 1, snapshot, index, snapshot.Length - index);
            binding.Snapshot = snapshot;
        }

        /// <summary>发布消息总线。</summary>
        public void Publish<T>(T message)
        {
            Type type = typeof(T);
            if (!handlers.TryGetValue(type, out object? value))
            {
                return;
            }

            Action<T>[] snapshot = ((Binding<T>)value).Snapshot;
            for (int i = 0; i < snapshot.Length; i++)
            {
                snapshot[i](message);
            }
        }

        /// <summary>清空消息总线。</summary>
        public void Clear()
        {
            handlers.Clear();
        }
    }
}

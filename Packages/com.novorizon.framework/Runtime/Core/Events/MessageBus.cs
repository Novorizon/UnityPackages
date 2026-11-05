#nullable enable
using System;
using System.Collections.Generic;

namespace Game.Framework.Event
{
    public class MessageBus
    {
        sealed class Binding<T>
        {
            internal Action<T>[] Snapshot = Array.Empty<Action<T>>();
        }

        // A bus is owned and synchronously driven by its caller. Publishing retains a stable snapshot even
        // when a callback subscribes, unsubscribes, clears, or publishes recursively.
        readonly Dictionary<Type, object> handlers = new();

        sealed class Subscription<T> : IDisposable
        {
            MessageBus? owner;
            Action<T>? handler;
            readonly Action<T> callback;

            internal Subscription(MessageBus owner, Action<T> handler)
            {
                this.owner = owner;
                this.handler = handler;
                callback = Invoke;
                owner.Subscribe(callback);
            }

            void Invoke(T message) => handler?.Invoke(message);

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
        public IDisposable SubscribeScoped<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return new Subscription<T>(this, handler);
        }

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

        public void Clear()
        {
            handlers.Clear();
        }
    }
}

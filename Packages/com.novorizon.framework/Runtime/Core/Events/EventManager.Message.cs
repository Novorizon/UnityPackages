using System;
namespace Game.Framework.Event
{
    public partial class EventManager
    {
        public ISubscription Subscribe<TMessage>(Enum topic, Action<TMessage> handler) where TMessage : IMessage =>
            Register(topic, typeof(TMessage), handler, null, handler);
        public void Notify<TMessage>(Enum topic, TMessage message) where TMessage : IMessage => PublishMessage(topic, message);
        public void Unsubscribe<TMessage>(Enum topic, Action<TMessage> handler) where TMessage : IMessage =>
            Remove(topic, typeof(TMessage), handler);
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler)
            where TTopic : Enum where TMessage : IMessage => Register(topic, typeof(TMessage), handler, null, handler);
        public ISubscription Subscribe<TTopic, TMessage>(TTopic topic, Func<TMessage, bool> filter, Action<TMessage> handler)
            where TTopic : Enum where TMessage : IMessage
        {
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

            return Register(topic, typeof(TMessage), handler, null, (Action<TMessage>)(message =>
            {
                if (filter(message))
                {
                    handler(message);
                }
            }));
        }
        public void Notify<TTopic, TMessage>(TTopic topic, TMessage message) where TTopic : Enum where TMessage : IMessage => PublishMessage(topic, message);
        public void Unsubscribe<TTopic, TMessage>(TTopic topic, Action<TMessage> handler) where TTopic : Enum where TMessage : IMessage =>
            Remove(topic, typeof(TMessage), handler);

        private void PublishMessage<TTopic, TMessage>(TTopic value, TMessage message)
            where TTopic : Enum
            where TMessage : IMessage
        {
            // The value-type branch must not box merely to test whether the payload is null.
            if (!typeof(TMessage).IsValueType && ReferenceEquals(message, null))
            {
                return;
            }

            Entry[] snapshot;
            lock (gate)
            {
                var topic = Find(value, false, out _);
                if (topic == null || !topic.Payloads.TryGetValue(typeof(TMessage), out var binding))
                {
                    return;
                }

                snapshot = binding.Snapshot;
            }

            if (UnityThread.IsMainThread)
            {
                DispatchMessage(snapshot, message);
            }
            else
            {
                PostMessageSnapshot(snapshot, message);
            }
        }

        private static void PostMessageSnapshot<TMessage>(Entry[] snapshot, TMessage message)
            where TMessage : IMessage
        {
            UnityThread.Post(() => DispatchMessage(snapshot, message));
        }

        private static void DispatchMessage<TMessage>(Entry[] snapshot, TMessage message)
            where TMessage : IMessage
        {
            foreach (var entry in snapshot)
            {
                var invoke = System.Threading.Volatile.Read(ref entry.TypedInvoke) as Action<TMessage>;
                if (System.Threading.Volatile.Read(ref entry.Active) == 0 || invoke == null)
                {
                    continue;
                }

                try
                {
                    invoke(message);
                }
                catch (Exception error)
                {
                    Logger.Exception(error);
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.Framework.Event
{
    public partial class EventManager : Singleton<EventManager>
    {
        private sealed class Entry : ISubscription
        {
            internal Delegate Handler;
            internal Action<object> Invoke;
            internal Action Remove;
            internal int Active = 1;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref Active, 0) == 0) return;
                try { Interlocked.Exchange(ref Remove, null)?.Invoke(); }
                finally { Handler = null; Volatile.Write(ref Invoke, null); }
            }
            internal void Deactivate()
            {
                Interlocked.Exchange(ref Active, 0);
                Handler = null; Volatile.Write(ref Invoke, null); Interlocked.Exchange(ref Remove, null);
            }
        }
        private sealed class Binding { internal Entry[] Snapshot = Array.Empty<Entry>(); }
        private sealed class Topic
        {
            internal Enum Value;
            internal readonly Dictionary<Type, Binding> Payloads = new Dictionary<Type, Binding>();
        }
        private interface ITopicIndex
        {
            void Add(Enum value, Topic topic);
            void Remove(Enum value);
        }
        // Concrete enum keys use their generic equality comparer: no numeric conversion or boxing on publish.
        private sealed class TopicIndex<TTopic> : ITopicIndex where TTopic : Enum
        {
            private readonly Dictionary<TTopic, Topic> values = new Dictionary<TTopic, Topic>();
            internal TopicIndex(Dictionary<Enum, Topic> topics)
            { foreach (var pair in topics) values.Add((TTopic)(object)pair.Key, pair.Value); }
            internal bool TryGet(TTopic value, out Topic topic) => values.TryGetValue(value, out topic);
            public void Add(Enum value, Topic topic) { values.Add((TTopic)(object)value, topic); }
            public void Remove(Enum value) { values.Remove((TTopic)(object)value); }
        }
        private sealed class TopicGroup
        {
            internal Type EnumType;
            internal readonly Dictionary<Enum, Topic> Topics = new Dictionary<Enum, Topic>();
            internal ITopicIndex Index;
        }
        private readonly object gate = new object();
        private readonly Dictionary<Type, TopicGroup> groups = new Dictionary<Type, TopicGroup>();

        // Only registered topics are indexed. The last unsubscribe removes the group and its typed index.
        private Topic Find<TTopic>(TTopic value, bool create, out TopicGroup group) where TTopic : Enum
        {
            if (typeof(TTopic) == typeof(Enum)) return FindErased((Enum)(object)value, create, out group);
            var type = typeof(TTopic);
            if (!groups.TryGetValue(type, out group))
            {
                if (!create) return null;
                group = new TopicGroup { EnumType = type }; groups.Add(type, group);
            }
            if (group.Index == null) group.Index = new TopicIndex<TTopic>(group.Topics);
            if (((TopicIndex<TTopic>)group.Index).TryGet(value, out var topic)) return topic;
            return create ? AddTopic(group, (Enum)(object)value) : null;
        }
        private Topic FindErased(Enum value, bool create, out TopicGroup group)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var type = value.GetType();
            if (!groups.TryGetValue(type, out group))
            {
                if (!create) return null;
                group = new TopicGroup { EnumType = type }; groups.Add(type, group);
            }
            if (group.Topics.TryGetValue(value, out var topic)) return topic;
            return create ? AddTopic(group, value) : null;
        }
        private static Topic AddTopic(TopicGroup group, Enum value)
        {
            var topic = new Topic { Value = value };
            group.Topics.Add(value, topic); group.Index?.Add(value, topic); return topic;
        }
        private ISubscription Register<TTopic>(TTopic value, Type payload, Delegate handler, Action<object> invoke) where TTopic : Enum
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            lock (gate)
            {
                var topic = Find(value, true, out var group);
                if (!topic.Payloads.TryGetValue(payload, out var binding))
                { binding = new Binding(); topic.Payloads.Add(payload, binding); }
                var entry = new Entry { Handler = handler, Invoke = invoke };
                entry.Remove = () => RemoveEntry(group, topic, payload, binding, entry);
                var previous = binding.Snapshot;
                var snapshot = new Entry[previous.Length + 1]; Array.Copy(previous, snapshot, previous.Length);
                snapshot[previous.Length] = entry; binding.Snapshot = snapshot; return entry;
            }
        }
        private void RemoveEntry(TopicGroup group, Topic topic, Type payload, Binding binding, Entry entry)
        {
            lock (gate)
            {
                var previous = binding.Snapshot; int index = Array.IndexOf(previous, entry);
                if (index < 0) return;
                var snapshot = previous.Length == 1 ? Array.Empty<Entry>() : new Entry[previous.Length - 1];
                if (index > 0) Array.Copy(previous, 0, snapshot, 0, index);
                if (index < snapshot.Length) Array.Copy(previous, index + 1, snapshot, index, snapshot.Length - index);
                binding.Snapshot = snapshot;
                if (snapshot.Length == 0 && topic.Payloads.TryGetValue(payload, out var current) && ReferenceEquals(current, binding))
                    topic.Payloads.Remove(payload);
                if (topic.Payloads.Count == 0) RemoveTopic(group, topic);
            }
        }
        private void RemoveTopic(TopicGroup group, Topic topic)
        {
            if (!group.Topics.TryGetValue(topic.Value, out var current) || !ReferenceEquals(current, topic)) return;
            group.Topics.Remove(topic.Value); group.Index?.Remove(topic.Value);
            if (group.Topics.Count == 0 && groups.TryGetValue(group.EnumType, out var registered) && ReferenceEquals(registered, group))
                groups.Remove(group.EnumType);
        }
        public ISubscription Subscribe<TTopic>(TTopic topic, Action handler) where TTopic : Enum =>
            Register(topic, typeof(void), handler, _ => handler());
        public ISubscription Subscribe<TTopic>(TTopic topic, Func<bool> filter, Action handler) where TTopic : Enum
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            return Register(topic, typeof(void), handler, _ => { if (filter()) handler(); });
        }
        public void Notify<TTopic>(TTopic topic) where TTopic : Enum => Publish(topic, typeof(void), null);
        private void Publish<TTopic>(TTopic value, Type payload, object message) where TTopic : Enum
        {
            Entry[] snapshot;
            lock (gate)
            {
                var topic = Find(value, false, out _);
                if (topic == null || !topic.Payloads.TryGetValue(payload, out var binding)) return;
                snapshot = binding.Snapshot;
            }
            if (UnityThread.IsMainThread) Dispatch(snapshot, message);
            else PostSnapshot(snapshot, message);
        }
        // Keep closure creation out of the main-thread and no-listener publish paths.
        private static void PostSnapshot(Entry[] snapshot, object message) { UnityThread.Post(() => Dispatch(snapshot, message)); }
        private static void Dispatch(Entry[] snapshot, object message)
        {
            foreach (var entry in snapshot)
            {
                var invoke = Volatile.Read(ref entry.Invoke);
                if (Volatile.Read(ref entry.Active) == 0 || invoke == null) continue;
                try { invoke(message); } catch (Exception error) { Logger.Exception(error); }
            }
        }
        public void Unsubscribe<TTopic>(TTopic topic, Action handler) where TTopic : Enum => Remove(topic, typeof(void), handler);
        private void Remove<TTopic>(TTopic value, Type payload, Delegate handler) where TTopic : Enum
        {
            if (handler == null) return;
            lock (gate)
            {
                var topic = Find(value, false, out _);
                if (topic == null || !topic.Payloads.TryGetValue(payload, out var binding)) return;
                foreach (var entry in binding.Snapshot)
                    if (Volatile.Read(ref entry.Active) != 0 && Equals(entry.Handler, handler)) { entry.Dispose(); break; }
            }
        }
        public void Unsubscribe<TTopic>(TTopic value) where TTopic : Enum
        {
            lock (gate)
            {
                var topic = Find(value, false, out var group);
                if (topic == null) return;
                foreach (var binding in topic.Payloads.Values) foreach (var entry in binding.Snapshot) entry.Deactivate();
                topic.Payloads.Clear(); RemoveTopic(group, topic);
            }
        }
        public void Clear()
        {
            lock (gate)
            {
                foreach (var group in groups.Values)
                foreach (var topic in group.Topics.Values)
                foreach (var binding in topic.Payloads.Values)
                foreach (var entry in binding.Snapshot) entry.Deactivate();
                groups.Clear(); groups.TrimExcess();
            }
        }
    }
}

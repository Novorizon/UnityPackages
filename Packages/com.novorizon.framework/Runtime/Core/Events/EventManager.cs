using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.Framework.Event
{
    /// <summary>事件对应的管理器。管理对应模块的状态和操作入口。</summary>
    public partial class EventManager : Singleton<EventManager>, IDisposable
    {
        /// <summary>结束事件管理器对相关资源的持有，并执行该类型的清理流程。</summary>
        public override void Dispose()
        {
            base.Release();
            Clear();
        }
        /// <summary>表示事件管理器中的条目。</summary>
        private sealed class Entry : ISubscription
        {
            internal Delegate Handler;
            internal Action<object> Invoke;
            internal object TypedInvoke;
            internal Action Remove;
            internal int Active = 1;
            /// <summary>结束条目对相关资源的持有，并执行该类型的清理流程。</summary>
            public void Dispose()
            {
                if (Interlocked.Exchange(ref Active, 0) == 0)
                {
                    return;
                }

                try
                {
                    Interlocked.Exchange(ref Remove, null)?.Invoke();
                }
                finally
                {
                    Handler = null;
                    Volatile.Write(ref Invoke, null);
                    Volatile.Write(ref TypedInvoke, null);
                }
            }

            /// <summary>调用 Interlocked.Exchange 完成Deactivate。</summary>
            internal void Deactivate()
            {
                Interlocked.Exchange(ref Active, 0);
                Handler = null;
                Volatile.Write(ref Invoke, null);
                Volatile.Write(ref TypedInvoke, null);
                Interlocked.Exchange(ref Remove, null);
            }
        }

        /// <summary>表示事件管理器中的绑定。</summary>
        private sealed class Binding
        {
            internal Entry[] Snapshot = Array.Empty<Entry>();
        }

        /// <summary>表示事件管理器中的主题。</summary>
        private sealed class Topic
        {
            internal Enum Value;
            internal readonly Dictionary<Type, Binding> Payloads = new Dictionary<Type, Binding>();
        }

        /// <summary>定义主题索引的调用契约。</summary>
        private interface ITopicIndex
        {
            /// <summary>添加主题索引。</summary>
            void Add(Enum value, Topic topic);
            /// <summary>移除主题索引。</summary>
            void Remove(Enum value);
        }

        // Concrete enum keys use their generic equality comparer: no numeric conversion or boxing on publish.
        /// <summary>表示事件管理器中的主题索引。</summary>
        private sealed class TopicIndex<TTopic> : ITopicIndex where TTopic : Enum
        {
            private readonly Dictionary<TTopic, Topic> values = new Dictionary<TTopic, Topic>();
            /// <summary>使用调用方提供的topics创建 TopicIndex。</summary>
            internal TopicIndex(Dictionary<Enum, Topic> topics)
            {
                foreach (var pair in topics)
                {
                    values.Add((TTopic)(object)pair.Key, pair.Value);
                }
            }

            /// <summary>尝试取得主题索引，返回是否成功。</summary>
            internal bool TryGet(TTopic value, out Topic topic) => values.TryGetValue(value, out topic);
            /// <summary>添加主题索引。</summary>
            public void Add(Enum value, Topic topic)
            {
                values.Add((TTopic)(object)value, topic);
            }

            /// <summary>移除主题索引。</summary>
            public void Remove(Enum value)
            {
                values.Remove((TTopic)(object)value);
            }
        }

        /// <summary>表示事件管理器中的主题分组。</summary>
        private sealed class TopicGroup
        {
            internal Type EnumType;
            internal readonly Dictionary<Enum, Topic> Topics = new Dictionary<Enum, Topic>();
            internal ITopicIndex Index;
        }

        private readonly object gate = new object ();
        private readonly Dictionary<Type, TopicGroup> groups = new Dictionary<Type, TopicGroup>();
        // Only registered topics are indexed. The last unsubscribe removes the group and its typed index.
        /// <summary>查找事件管理器。</summary>
        private Topic Find<TTopic>(TTopic value, bool create, out TopicGroup group)
            where TTopic : Enum
        {
            if (typeof(TTopic) == typeof(Enum))
            {
                return FindErased((Enum)(object)value, create, out group);
            }

            var type = typeof(TTopic);
            if (!groups.TryGetValue(type, out group))
            {
                if (!create)
                {
                    return null;
                }

                group = new TopicGroup
                {
                    EnumType = type
                };
                groups.Add(type, group);
            }

            if (group.Index == null)
            {
                group.Index = new TopicIndex<TTopic>(group.Topics);
            }

            if (((TopicIndex<TTopic>)group.Index).TryGet(value, out var topic))
            {
                return topic;
            }

            return create ? AddTopic(group, (Enum)(object)value) : null;
        }

        /// <summary>查找 Erased。</summary>
        private Topic FindErased(Enum value, bool create, out TopicGroup group)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            var type = value.GetType();
            if (!groups.TryGetValue(type, out group))
            {
                if (!create)
                {
                    return null;
                }

                group = new TopicGroup
                {
                    EnumType = type
                };
                groups.Add(type, group);
            }

            if (group.Topics.TryGetValue(value, out var topic))
            {
                return topic;
            }

            return create ? AddTopic(group, value) : null;
        }

        /// <summary>添加主题。</summary>
        private static Topic AddTopic(TopicGroup group, Enum value)
        {
            var topic = new Topic
            {
                Value = value
            };
            group.Topics.Add(value, topic);
            group.Index?.Add(value, topic);
            return topic;
        }

        /// <summary>登记事件管理器。</summary>
        private ISubscription Register<TTopic>(TTopic value, Type payload, Delegate handler, Action<object> invoke, object typedInvoke = null)
            where TTopic : Enum
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (gate)
            {
                var topic = Find(value, true, out var group);
                if (!topic.Payloads.TryGetValue(payload, out var binding))
                {
                    binding = new Binding();
                    topic.Payloads.Add(payload, binding);
                }

                var entry = new Entry
                {
                    Handler = handler,
                    Invoke = invoke,
                    TypedInvoke = typedInvoke
                };
                entry.Remove = () => RemoveEntry(group, topic, payload, binding, entry);
                var previous = binding.Snapshot;
                var snapshot = new Entry[previous.Length + 1];
                Array.Copy(previous, snapshot, previous.Length);
                snapshot[previous.Length] = entry;
                binding.Snapshot = snapshot;
                return entry;
            }
        }

        /// <summary>移除条目。</summary>
        private void RemoveEntry(TopicGroup group, Topic topic, Type payload, Binding binding, Entry entry)
        {
            lock (gate)
            {
                var previous = binding.Snapshot;
                int index = Array.IndexOf(previous, entry);
                if (index < 0)
                {
                    return;
                }

                var snapshot = previous.Length == 1 ? Array.Empty<Entry>() : new Entry[previous.Length - 1];
                if (index > 0)
                {
                    Array.Copy(previous, 0, snapshot, 0, index);
                }

                if (index < snapshot.Length)
                {
                    Array.Copy(previous, index + 1, snapshot, index, snapshot.Length - index);
                }

                binding.Snapshot = snapshot;
                if (snapshot.Length == 0 && topic.Payloads.TryGetValue(payload, out var current) && ReferenceEquals(current, binding))
                {
                    topic.Payloads.Remove(payload);
                }

                if (topic.Payloads.Count == 0)
                {
                    RemoveTopic(group, topic);
                }
            }
        }

        /// <summary>移除主题。</summary>
        private void RemoveTopic(TopicGroup group, Topic topic)
        {
            if (!group.Topics.TryGetValue(topic.Value, out var current) || !ReferenceEquals(current, topic))
            {
                return;
            }

            group.Topics.Remove(topic.Value);
            group.Index?.Remove(topic.Value);
            if (group.Topics.Count == 0 && groups.TryGetValue(group.EnumType, out var registered) && ReferenceEquals(registered, group))
            {
                groups.Remove(group.EnumType);
            }
        }

        /// <summary>订阅事件管理器。</summary>
        public ISubscription Subscribe<TTopic>(TTopic topic, Action handler)
            where TTopic : Enum => Register(topic, typeof(void), handler, _ => handler());
        /// <summary>订阅事件管理器。</summary>
        public ISubscription Subscribe<TTopic>(TTopic topic, Func<bool> filter, Action handler)
            where TTopic : Enum
        {
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

            return Register(topic, typeof(void), handler, _ =>
            {
                if (filter())
                {
                    handler();
                }
            });
        }

        /// <summary>通知事件管理器。</summary>
        public void Notify<TTopic>(TTopic topic)
            where TTopic : Enum => Publish(topic, typeof(void), null);
        /// <summary>发布事件管理器。</summary>
        private void Publish<TTopic>(TTopic value, Type payload, object message)
            where TTopic : Enum
        {
            Entry[] snapshot;
            lock (gate)
            {
                var topic = Find(value, false, out _);
                if (topic == null || !topic.Payloads.TryGetValue(payload, out var binding))
                {
                    return;
                }

                snapshot = binding.Snapshot;
            }

            if (UnityThread.IsMainThread)
            {
                Dispatch(snapshot, message);
            }
            else
            {
                PostSnapshot(snapshot, message);
            }
        }

        // Keep closure creation out of the main-thread and no-listener publish paths.
        /// <summary>投递快照。</summary>
        private static void PostSnapshot(Entry[] snapshot, object message)
        {
            UnityThread.Post(() => Dispatch(snapshot, message));
        }

        // 一次发布使用开始时取得的订阅快照，避免订阅回调修改列表导致遍历出错。
        private static void Dispatch(Entry[] snapshot, object message)
        {
            foreach (var entry in snapshot)
            {
                var invoke = Volatile.Read(ref entry.Invoke);
                if (Volatile.Read(ref entry.Active) == 0 || invoke == null)
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

        /// <summary>取消订阅事件管理器。</summary>
        public void Unsubscribe<TTopic>(TTopic topic, Action handler)
            where TTopic : Enum => Remove(topic, typeof(void), handler);
        /// <summary>移除事件管理器。</summary>
        private void Remove<TTopic>(TTopic value, Type payload, Delegate handler)
            where TTopic : Enum
        {
            if (handler == null)
            {
                return;
            }

            lock (gate)
            {
                var topic = Find(value, false, out _);
                if (topic == null || !topic.Payloads.TryGetValue(payload, out var binding))
                {
                    return;
                }

                foreach (var entry in binding.Snapshot)
                {
                    if (Volatile.Read(ref entry.Active) != 0 && Equals(entry.Handler, handler))
                    {
                        entry.Dispose();
                        break;
                    }
                }
            }
        }

        /// <summary>取消订阅事件管理器。</summary>
        public void Unsubscribe<TTopic>(TTopic value)
            where TTopic : Enum
        {
            lock (gate)
            {
                var topic = Find(value, false, out var group);
                if (topic == null)
                {
                    return;
                }

                foreach (var binding in topic.Payloads.Values)
                {
                    foreach (var entry in binding.Snapshot)
                    {
                        entry.Deactivate();
                    }
                }

                topic.Payloads.Clear();
                RemoveTopic(group, topic);
            }
        }

        /// <summary>清空事件管理器。</summary>
        public void Clear()
        {
            lock (gate)
            {
                foreach (var group in groups.Values)
                {
                    foreach (var topic in group.Topics.Values)
                    {
                        foreach (var binding in topic.Payloads.Values)
                        {
                            foreach (var entry in binding.Snapshot)
                            {
                                entry.Deactivate();
                            }
                        }
                    }
                }

                groups.Clear();
                groups.TrimExcess();
            }
        }
    }
}

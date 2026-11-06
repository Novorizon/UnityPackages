using System;
using System.Collections.Generic;

namespace Game.Framework.Data
{
    /// <summary>按类型持有运行时数据；不隐式加载配置或保存存档。应在游戏主线程使用。</summary>
    public sealed class DataManager : ConfigurableSingleton<DataManager>, IDisposable
    {
        private sealed class Entry
        {
            internal object Value;
            internal bool Owned;
        }

        private readonly Dictionary<Type, Entry> entries = new Dictionary<Type, Entry>();
        private readonly List<Type> order = new List<Type>();
        private bool clearing;
        private bool disposed;
        public static DataManager Instance => GetOrCreateInstance(() => new DataManager());
        public int Count => entries.Count;

        public static void Reset(bool stopping = false)
        {
            ResetInstance(value => value.Dispose(), stopping);
        }

        /// <summary>同一类型只允许注册一次。owned 为 true 时，移除或清理会 Dispose 数据。</summary>
        public void Register<T>(T value, bool owned = true)
            where T : class
        {
            EnsureAvailable();
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            if (entries.ContainsKey(typeof(T)))
            {
                throw new InvalidOperationException("Data is already registered: " + typeof(T).FullName);
            }

            // 防止一个对象按两个类型注册后被释放两次。
            foreach (var entry in entries.Values)
            {
                if (ReferenceEquals(entry.Value, value))
                {
                    throw new InvalidOperationException("This data object is already registered.");
                }
            }

            entries.Add(typeof(T), new Entry { Value = value, Owned = owned });
            order.Add(typeof(T));
        }

        public T Get<T>()
            where T : class
        {
            if (!TryGet<T>(out var value))
            {
                throw new KeyNotFoundException("Data is not registered: " + typeof(T).FullName);
            }

            return value;
        }

        public bool TryGet<T>(out T value)
            where T : class
        {
            EnsureAvailable();
            value = entries.TryGetValue(typeof(T), out var entry) ? (T)entry.Value : null;
            return value != null;
        }

        public bool Remove<T>()
            where T : class
        {
            EnsureAvailable();
            if (!entries.TryGetValue(typeof(T), out var entry))
            {
                return false;
            }

            entries.Remove(typeof(T));
            order.Remove(typeof(T));
            clearing = true;
            try
            {
                Release(entry);
            }
            finally
            {
                clearing = false;
            }

            return true;
        }

        /// <summary>逆注册顺序释放；某个数据释放失败时仍继续清理其他数据。</summary>
        public void Clear()
        {
            EnsureAvailable();
            ClearCore();
        }

        private void ClearCore()
        {
            clearing = true;
            var previous = new List<Entry>();
            for (int i = order.Count - 1; i >= 0; i--)
            {
                previous.Add(entries[order[i]]);
            }

            entries.Clear();
            order.Clear();
            var errors = new List<Exception>();
            try
            {
                foreach (var entry in previous)
                {
                    try
                    {
                        Release(entry);
                    }
                    catch (Exception error)
                    {
                        errors.Add(error);
                    }
                }
            }
            finally
            {
                clearing = false;
            }

            if (errors.Count != 0)
            {
                throw new AggregateException("Data cleanup failed.", errors);
            }
        }

        private static void Release(Entry entry)
        {
            if (entry.Owned && entry.Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private void EnsureAvailable()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DataManager));
            }

            if (clearing)
            {
                throw new InvalidOperationException("Data cleanup is in progress.");
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            if (clearing)
            {
                throw new InvalidOperationException("Data cleanup is in progress.");
            }

            disposed = true;
            try
            {
                ClearCore();
            }
            finally
            {
                ReleaseInstance(this);
            }
        }
    }
}

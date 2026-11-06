using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Framework.Config
{
    /// <summary>只读的表索引快照。记录自身应当作为只读配置使用。</summary>
    public sealed class ConfigTable<TKey, T>
        where T : class
    {
        private readonly Dictionary<TKey, T> index;
        public ReadOnlyCollection<T> Items { get; }
        public int Count => index.Count;

        public T this[TKey key] => index[key];
        public ConfigTable(IEnumerable<T> items, Func<T, TKey> keySelector)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }

            index = new Dictionary<TKey, T>();
            var rows = new List<T>();
            foreach (var item in items)
            {
                if (item == null)
                {
                    throw new ArgumentException("A configuration row cannot be null.", nameof(items));
                }

                var key = keySelector(item);
                if (key == null || index.ContainsKey(key))
                {
                    throw new ArgumentException("Null or duplicate configuration key: " + key, nameof(items));
                }

                index.Add(key, item);
                rows.Add(item);
            }

            Items = rows.AsReadOnly();
        }

        public bool TryGet(TKey key, out T value) => index.TryGetValue(key, out value);
    }
}

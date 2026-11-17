using System;
using System.Collections.Generic;

namespace ECS
{
    /// <summary>Process-local component identities. IDs must not be serialized or sent over the network.</summary>
    /// <remarks>提供组件类型的实现。</remarks>
    public static class ComponentType
    {
        private static readonly Dictionary<Type, int> ids = new Dictionary<Type, int>();
        private static readonly object gate = new object();

        /// <summary>表示组件类型中的缓存。</summary>
        private static class Cache<T> where T : class, IComponent
        {
            internal static readonly int Id = GetId(typeof(T));
        }

        /// <summary>获取标识。</summary>
        public static int GetId<T>() where T : class, IComponent
        {
            return Cache<T>.Id;
        }

        /// <summary>获取标识。</summary>
        public static int GetId(Type type)
        {
            Validate(type);
            lock (gate)
            {
                if (!ids.TryGetValue(type, out var id))
                {
                    id = ids.Count + 1;
                    ids.Add(type, id);
                }
                return id;
            }
        }

        /// <summary>验证传入的 type。</summary>
        public static void Validate(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!type.IsClass || !typeof(IComponent).IsAssignableFrom(type) || type.ContainsGenericParameters)
                throw new ArgumentException("Data components must be closed reference types implementing IComponent.", nameof(type));
        }

        /// <summary>解析组件类型。</summary>
        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) throw new ArgumentException("A component type name is required.", nameof(typeName));
            var pool = ReferencePool.Instance;
            if (pool == null) throw new InvalidOperationException("Component lookup is unavailable while ECS is quitting.");
            Type resolved = pool.ResolveType(typeName);
            Validate(resolved);
            return resolved;
        }
    }
}

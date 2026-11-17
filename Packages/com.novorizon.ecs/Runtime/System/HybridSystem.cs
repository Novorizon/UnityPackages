using System.Collections.Generic;
using UnityEngine;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace ECS
{
    /// <summary>提供混合系统的实现。</summary>
    public abstract class HybridSystem<T1, T2> : SystemBase, ISystemFilter
        where T1 : Component where T2 : class, IComponent
    {
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T1> Components1 = new List<T1>();
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T2> Components2 = new List<T2>();

        private static readonly int[] RequiredTypes = { ComponentType.GetId<T2>() };
        private static readonly IReadOnlyList<int> ReadOnlyRequiredTypes = System.Array.AsReadOnly(RequiredTypes);

        public IReadOnlyList<int> ComponentTypeIds { get { return ReadOnlyRequiredTypes; } }
        public bool RequiresGameObject { get { return true; } }

        /// <summary>注入实体。</summary>
        public sealed override void InjectEntity(Entity entity)
        {
            if (entity == null)
                throw new System.ArgumentNullException(nameof(entity));

            var c1 = entity.gameObject != null ? entity.gameObject.GetComponent<T1>() : null;
            IComponent c2 = null;
            bool matches = entity.IsAlive &&
                c1 &&
                entity.components.TryGetValue(RequiredTypes[0], out c2);

            if (EntityIndices.TryGetValue(entity, out var index))
            {
                if (matches)
                {
                    Components1[index] = c1;
                    Components2[index] = (T2)c2;
                }
                else
                {
                    var removedComponent = Components1[index];
                    int last = Entities.Count - 1;
                    if (index != last)
                    {
                        Components1[index] = Components1[last];
                        Components2[index] = Components2[last];
                    }
                    Components1.RemoveAt(last);
                    Components2.RemoveAt(last);
                    RemoveEntityAt(index);
                    OnEject(entity, removedComponent);
                }
            }
            else if (matches)
            {
                AddEntity(entity);
                Components1.Add(c1);
                Components2.Add((T2)c2);
            }
        }

        /// <summary>清空组件。</summary>
        protected sealed override void ClearComponents()
        {
            Components1.Clear();
            Components2.Clear();
        }

        /// <summary>推进该对象的一次更新。</summary>
        public sealed override void Update()
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                OnUpdate(i, Entities[i], Components1[i], Components2[i]);
            }
        }

        /// <summary>响应更新。</summary>
        protected abstract void OnUpdate(int index, Entity entity, T1 component1, T2 component2);

        /// <summary>响应 Eject。</summary>
        protected virtual void OnEject(Entity entity, T1 component) { }
    }

    /// <summary>提供混合系统的实现。</summary>
    public abstract class HybridSystem<T1, T2, T3> : SystemBase, ISystemFilter
        where T1 : Component where T2 : class, IComponent where T3 : class, IComponent
    {
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T1> Components1 = new List<T1>();
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T2> Components2 = new List<T2>();
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T3> Components3 = new List<T3>();

        private static readonly int[] RequiredTypes = { ComponentType.GetId<T2>(), ComponentType.GetId<T3>() };
        private static readonly IReadOnlyList<int> ReadOnlyRequiredTypes = System.Array.AsReadOnly(RequiredTypes);

        public IReadOnlyList<int> ComponentTypeIds { get { return ReadOnlyRequiredTypes; } }
        public bool RequiresGameObject { get { return true; } }

        /// <summary>注入实体。</summary>
        public sealed override void InjectEntity(Entity entity)
        {
            if (entity == null)
                throw new System.ArgumentNullException(nameof(entity));

            var c1 = entity.gameObject != null ? entity.gameObject.GetComponent<T1>() : null;
            IComponent c2 = null;
            IComponent c3 = null;
            bool matches = entity.IsAlive &&
                c1 &&
                entity.components.TryGetValue(RequiredTypes[0], out c2) &&
                entity.components.TryGetValue(RequiredTypes[1], out c3);

            if (EntityIndices.TryGetValue(entity, out var index))
            {
                if (matches)
                {
                    Components1[index] = c1;
                    Components2[index] = (T2)c2;
                    Components3[index] = (T3)c3;
                }
                else
                {
                    var removedComponent = Components1[index];
                    int last = Entities.Count - 1;
                    if (index != last)
                    {
                        Components1[index] = Components1[last];
                        Components2[index] = Components2[last];
                        Components3[index] = Components3[last];
                    }
                    Components1.RemoveAt(last);
                    Components2.RemoveAt(last);
                    Components3.RemoveAt(last);
                    RemoveEntityAt(index);
                    OnEject(entity, removedComponent);
                }
            }
            else if (matches)
            {
                AddEntity(entity);
                Components1.Add(c1);
                Components2.Add((T2)c2);
                Components3.Add((T3)c3);
            }
        }

        /// <summary>清空组件。</summary>
        protected sealed override void ClearComponents()
        {
            Components1.Clear();
            Components2.Clear();
            Components3.Clear();
        }

        /// <summary>推进该对象的一次更新。</summary>
        public sealed override void Update()
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                OnUpdate(i, Entities[i], Components1[i], Components2[i], Components3[i]);
            }
        }

        /// <summary>响应更新。</summary>
        protected abstract void OnUpdate(int index, Entity entity, T1 component1, T2 component2, T3 component3);

        /// <summary>响应 Eject。</summary>
        protected virtual void OnEject(Entity entity, T1 component) { }
    }

}

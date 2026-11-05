using System.Collections.Generic;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace ECS
{
    [System.Serializable]
    public abstract class ComponentSystem<T1> : SystemBase, ISystemFilter
        where T1 : class, IComponent
    {
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T1> Components = new List<T1>();

        private static readonly int[] RequiredTypes = { ComponentType.GetId<T1>() };
        private static readonly IReadOnlyList<int> ReadOnlyRequiredTypes = System.Array.AsReadOnly(RequiredTypes);

        public IReadOnlyList<int> ComponentTypeIds { get { return ReadOnlyRequiredTypes; } }
        public bool RequiresGameObject { get { return false; } }

        public sealed override void InjectEntity(Entity entity)
        {
            if (entity == null)
                throw new System.ArgumentNullException(nameof(entity));

            IComponent c1 = null;
            bool matches = entity.IsAlive &&
                entity.components.TryGetValue(RequiredTypes[0], out c1);

            if (EntityIndices.TryGetValue(entity, out var index))
            {
                if (matches)
                {
                    Components[index] = (T1)c1;
                }
                else
                {
                    int last = Entities.Count - 1;
                    if (index != last)
                    {
                        Components[index] = Components[last];
                    }
                    Components.RemoveAt(last);
                    RemoveEntityAt(index);
                }
            }
            else if (matches)
            {
                AddEntity(entity);
                Components.Add((T1)c1);
            }
        }

        protected sealed override void ClearComponents()
        {
            Components.Clear();
        }

        public sealed override void Update()
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                OnUpdate(i, Entities[i], Components[i]);
            }
        }

        protected abstract void OnUpdate(int index, Entity entity, T1 component1);
    }

    public abstract class ComponentSystem<T1, T2> : SystemBase, ISystemFilter
        where T1 : class, IComponent where T2 : class, IComponent
    {
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T1> Components1 = new List<T1>();
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T2> Components2 = new List<T2>();

        private static readonly int[] RequiredTypes = { ComponentType.GetId<T1>(), ComponentType.GetId<T2>() };
        private static readonly IReadOnlyList<int> ReadOnlyRequiredTypes = System.Array.AsReadOnly(RequiredTypes);

        public IReadOnlyList<int> ComponentTypeIds { get { return ReadOnlyRequiredTypes; } }
        public bool RequiresGameObject { get { return false; } }

        public sealed override void InjectEntity(Entity entity)
        {
            if (entity == null)
                throw new System.ArgumentNullException(nameof(entity));

            IComponent c1 = null;
            IComponent c2 = null;
            bool matches = entity.IsAlive &&
                entity.components.TryGetValue(RequiredTypes[0], out c1) &&
                entity.components.TryGetValue(RequiredTypes[1], out c2);

            if (EntityIndices.TryGetValue(entity, out var index))
            {
                if (matches)
                {
                    Components1[index] = (T1)c1;
                    Components2[index] = (T2)c2;
                }
                else
                {
                    int last = Entities.Count - 1;
                    if (index != last)
                    {
                        Components1[index] = Components1[last];
                        Components2[index] = Components2[last];
                    }
                    Components1.RemoveAt(last);
                    Components2.RemoveAt(last);
                    RemoveEntityAt(index);
                }
            }
            else if (matches)
            {
                AddEntity(entity);
                Components1.Add((T1)c1);
                Components2.Add((T2)c2);
            }
        }

        protected sealed override void ClearComponents()
        {
            Components1.Clear();
            Components2.Clear();
        }

        public sealed override void Update()
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                OnUpdate(i, Entities[i], Components1[i], Components2[i]);
            }
        }

        protected abstract void OnUpdate(int index, Entity entity, T1 component1, T2 component2);
    }

    public abstract class ComponentSystem<T1, T2, T3> : SystemBase, ISystemFilter
        where T1 : class, IComponent where T2 : class, IComponent where T3 : class, IComponent
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

        private static readonly int[] RequiredTypes = { ComponentType.GetId<T1>(), ComponentType.GetId<T2>(), ComponentType.GetId<T3>() };
        private static readonly IReadOnlyList<int> ReadOnlyRequiredTypes = System.Array.AsReadOnly(RequiredTypes);

        public IReadOnlyList<int> ComponentTypeIds { get { return ReadOnlyRequiredTypes; } }
        public bool RequiresGameObject { get { return false; } }

        public sealed override void InjectEntity(Entity entity)
        {
            if (entity == null)
                throw new System.ArgumentNullException(nameof(entity));

            IComponent c1 = null;
            IComponent c2 = null;
            IComponent c3 = null;
            bool matches = entity.IsAlive &&
                entity.components.TryGetValue(RequiredTypes[0], out c1) &&
                entity.components.TryGetValue(RequiredTypes[1], out c2) &&
                entity.components.TryGetValue(RequiredTypes[2], out c3);

            if (EntityIndices.TryGetValue(entity, out var index))
            {
                if (matches)
                {
                    Components1[index] = (T1)c1;
                    Components2[index] = (T2)c2;
                    Components3[index] = (T3)c3;
                }
                else
                {
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
                }
            }
            else if (matches)
            {
                AddEntity(entity);
                Components1.Add((T1)c1);
                Components2.Add((T2)c2);
                Components3.Add((T3)c3);
            }
        }

        protected sealed override void ClearComponents()
        {
            Components1.Clear();
            Components2.Clear();
            Components3.Clear();
        }

        public sealed override void Update()
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                OnUpdate(i, Entities[i], Components1[i], Components2[i], Components3[i]);
            }
        }

        protected abstract void OnUpdate(int index, Entity entity, T1 component1, T2 component2, T3 component3);
    }

    public abstract class ComponentSystem<T1, T2, T3, T4> : SystemBase, ISystemFilter
        where T1 : class, IComponent where T2 : class, IComponent where T3 : class, IComponent where T4 : class, IComponent
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
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T4> Components4 = new List<T4>();

        private static readonly int[] RequiredTypes = { ComponentType.GetId<T1>(), ComponentType.GetId<T2>(), ComponentType.GetId<T3>(), ComponentType.GetId<T4>() };
        private static readonly IReadOnlyList<int> ReadOnlyRequiredTypes = System.Array.AsReadOnly(RequiredTypes);

        public IReadOnlyList<int> ComponentTypeIds { get { return ReadOnlyRequiredTypes; } }
        public bool RequiresGameObject { get { return false; } }

        public sealed override void InjectEntity(Entity entity)
        {
            if (entity == null)
                throw new System.ArgumentNullException(nameof(entity));

            IComponent c1 = null;
            IComponent c2 = null;
            IComponent c3 = null;
            IComponent c4 = null;
            bool matches = entity.IsAlive &&
                entity.components.TryGetValue(RequiredTypes[0], out c1) &&
                entity.components.TryGetValue(RequiredTypes[1], out c2) &&
                entity.components.TryGetValue(RequiredTypes[2], out c3) &&
                entity.components.TryGetValue(RequiredTypes[3], out c4);

            if (EntityIndices.TryGetValue(entity, out var index))
            {
                if (matches)
                {
                    Components1[index] = (T1)c1;
                    Components2[index] = (T2)c2;
                    Components3[index] = (T3)c3;
                    Components4[index] = (T4)c4;
                }
                else
                {
                    int last = Entities.Count - 1;
                    if (index != last)
                    {
                        Components1[index] = Components1[last];
                        Components2[index] = Components2[last];
                        Components3[index] = Components3[last];
                        Components4[index] = Components4[last];
                    }
                    Components1.RemoveAt(last);
                    Components2.RemoveAt(last);
                    Components3.RemoveAt(last);
                    Components4.RemoveAt(last);
                    RemoveEntityAt(index);
                }
            }
            else if (matches)
            {
                AddEntity(entity);
                Components1.Add((T1)c1);
                Components2.Add((T2)c2);
                Components3.Add((T3)c3);
                Components4.Add((T4)c4);
            }
        }

        protected sealed override void ClearComponents()
        {
            Components1.Clear();
            Components2.Clear();
            Components3.Clear();
            Components4.Clear();
        }

        public sealed override void Update()
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                OnUpdate(i, Entities[i], Components1[i], Components2[i], Components3[i], Components4[i]);
            }
        }

        protected abstract void OnUpdate(int index, Entity entity, T1 component1, T2 component2, T3 component3, T4 component4);
    }

    public abstract class ComponentSystem<T1, T2, T3, T4, T5> : SystemBase, ISystemFilter
        where T1 : class, IComponent where T2 : class, IComponent where T3 : class, IComponent where T4 : class, IComponent where T5 : class, IComponent
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
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T4> Components4 = new List<T4>();
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<T5> Components5 = new List<T5>();

        private static readonly int[] RequiredTypes = { ComponentType.GetId<T1>(), ComponentType.GetId<T2>(), ComponentType.GetId<T3>(), ComponentType.GetId<T4>(), ComponentType.GetId<T5>() };
        private static readonly IReadOnlyList<int> ReadOnlyRequiredTypes = System.Array.AsReadOnly(RequiredTypes);

        public IReadOnlyList<int> ComponentTypeIds { get { return ReadOnlyRequiredTypes; } }
        public bool RequiresGameObject { get { return false; } }

        public sealed override void InjectEntity(Entity entity)
        {
            if (entity == null)
                throw new System.ArgumentNullException(nameof(entity));

            IComponent c1 = null;
            IComponent c2 = null;
            IComponent c3 = null;
            IComponent c4 = null;
            IComponent c5 = null;
            bool matches = entity.IsAlive &&
                entity.components.TryGetValue(RequiredTypes[0], out c1) &&
                entity.components.TryGetValue(RequiredTypes[1], out c2) &&
                entity.components.TryGetValue(RequiredTypes[2], out c3) &&
                entity.components.TryGetValue(RequiredTypes[3], out c4) &&
                entity.components.TryGetValue(RequiredTypes[4], out c5);

            if (EntityIndices.TryGetValue(entity, out var index))
            {
                if (matches)
                {
                    Components1[index] = (T1)c1;
                    Components2[index] = (T2)c2;
                    Components3[index] = (T3)c3;
                    Components4[index] = (T4)c4;
                    Components5[index] = (T5)c5;
                }
                else
                {
                    int last = Entities.Count - 1;
                    if (index != last)
                    {
                        Components1[index] = Components1[last];
                        Components2[index] = Components2[last];
                        Components3[index] = Components3[last];
                        Components4[index] = Components4[last];
                        Components5[index] = Components5[last];
                    }
                    Components1.RemoveAt(last);
                    Components2.RemoveAt(last);
                    Components3.RemoveAt(last);
                    Components4.RemoveAt(last);
                    Components5.RemoveAt(last);
                    RemoveEntityAt(index);
                }
            }
            else if (matches)
            {
                AddEntity(entity);
                Components1.Add((T1)c1);
                Components2.Add((T2)c2);
                Components3.Add((T3)c3);
                Components4.Add((T4)c4);
                Components5.Add((T5)c5);
            }
        }

        protected sealed override void ClearComponents()
        {
            Components1.Clear();
            Components2.Clear();
            Components3.Clear();
            Components4.Clear();
            Components5.Clear();
        }

        public sealed override void Update()
        {
            for (int i = 0; i < Entities.Count; ++i)
            {
                OnUpdate(i, Entities[i], Components1[i], Components2[i], Components3[i], Components4[i], Components5[i]);
            }
        }

        protected abstract void OnUpdate(int index, Entity entity, T1 component1, T2 component2, T3 component3, T4 component4, T5 component5);
    }

}

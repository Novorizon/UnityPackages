using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace ECS
{
    /// <summary>A main-thread system execution group. Entities remain globally owned.</summary>
    /// <remarks>管理同一个 ECS 世界中的系统，以及实体变化到系统的分发。</remarks>
    [Serializable]
    public class World : IWorld
    {
#if ODIN_INSPECTOR
        [ShowInInspector, ListDrawerSettings(IsReadOnly = true)]
#endif
        protected readonly List<ISystemBase> systems = new List<ISystemBase>();
        private readonly Dictionary<int, List<ISystemBase>> componentSystems = new Dictionary<int, List<ISystemBase>>();
        private readonly List<ISystemBase> gameObjectSystems = new List<ISystemBase>();
        private readonly List<ISystemBase> unfilteredSystems = new List<ISystemBase>();
        private readonly HashSet<ISystemBase> routedSystems = new HashSet<ISystemBase>();
        private readonly Dictionary<ISystemBase, ulong> systemGenerations = new Dictionary<ISystemBase, ulong>();
        private ulong nextSystemGeneration;
        private bool destroyed;
        private WorldManager owner;

        public float TimePerFrame { get; set; }
        public static World Self { get; private set; }
        /// <summary>创建 World 并采用默认配置。</summary>
        internal World() { }

        /// <summary>初始化世界。</summary>
        public virtual void Initialize()
        {
            if (!ReferenceEquals(owner, null) && owner.IsStopping)
                throw new InvalidOperationException("Cannot reinitialize a World whose manager is shutting down.");
            var manager = WorldManager.Instance;
            if (manager == null) throw new InvalidOperationException("Cannot initialize a World while ECS is shutting down.");
            destroyed = false;
            owner = manager;
            manager.RegisterWorld(this);
            Self = this;
        }

        /// <summary>检查管理器是否满足前置条件。</summary>
        private WorldManager RequireManager()
        {
            if (destroyed) throw new ObjectDisposedException(nameof(World));
            if (ReferenceEquals(owner, null)) owner = WorldManager.Instance;
            if (ReferenceEquals(owner, null) || owner.IsStopping)
                throw new InvalidOperationException("The World manager is shutting down.");
            return owner;
        }

        /// <summary>回收 Or 发布版本系统。</summary>
        private void RecycleOrReleaseSystem(ISystemBase system)
        {
            ReferencePool.TryGetInstance(out var pool);
            if (!Ecs.IsQuitting && pool != null) pool.RecycleInstance(system);
            else system.OnRecycle();
        }

        /// <summary>销毁世界。</summary>
        public virtual void Destroy()
        {
            var manager = owner;
            if (!ReferenceEquals(manager, null) && manager.IsUpdating)
            {
                manager.Defer(Destroy);
                return;
            }
            if (destroyed) return;
            destroyed = true;
            if (!ReferenceEquals(manager, null)) manager.RemoveWorld(this);
            var ownedSystems = new List<ISystemBase>(systemGenerations.Keys);
            systemGenerations.Clear();
            systems.Clear();
            componentSystems.Clear();
            gameObjectSystems.Clear();
            unfilteredSystems.Clear();
            routedSystems.Clear();
            if (ReferenceEquals(Self, this)) Self = null;
            Exception firstError = null;
            for (int i = 0; i < ownedSystems.Count; ++i)
            {
                try
                {
                    RecycleOrReleaseSystem(ownedSystems[i]);
                }
                catch (Exception error) { if (firstError == null) firstError = error; }
            }
            Rethrow(firstError);
        }

        /// <summary>Rechecks all systems, including user systems without a component filter.</summary>
        /// <remarks>注入实体。</remarks>
        public void InjectEntity(Entity entity)
        {
            if (destroyed) return;
            var manager = owner;
            if (!ReferenceEquals(manager, null)) manager.BeginDispatch();
            Exception firstError = null;
            try
            {
                for (int i = 0; i < systems.Count; ++i)
                    InjectSafely(systems[i], entity, ref firstError);
            }
            finally
            {
                try { if (!ReferenceEquals(manager, null)) manager.EndDispatch(); }
                catch (Exception error) { if (firstError == null) firstError = error; }
            }
            Rethrow(firstError);
        }

        // 根据发生变化的组件和 GameObject 状态选择需要接收更新的系统。
        internal void InjectChangedEntity(Entity entity, IList<int> componentTypeIds, bool gameObjectChanged)
        {
            if (destroyed) return;
            // A system can depend on several changed types; inject it only once per transaction.
            routedSystems.Clear();
            Exception firstError = null;
            for (int i = 0; i < unfilteredSystems.Count; ++i)
                Route(unfilteredSystems[i], entity, ref firstError);
            if (componentTypeIds != null)
            {
                for (int i = 0; i < componentTypeIds.Count; ++i)
                {
                    if (!componentSystems.TryGetValue(componentTypeIds[i], out var matches)) continue;
                    for (int j = 0; j < matches.Count; ++j) Route(matches[j], entity, ref firstError);
                }
            }
            if (gameObjectChanged)
                for (int i = 0; i < gameObjectSystems.Count; ++i) Route(gameObjectSystems[i], entity, ref firstError);
            routedSystems.Clear();
            Rethrow(firstError);
        }

        /// <summary>注入组件变化。</summary>
        internal void InjectComponentChanged(Entity entity, int componentTypeId)
        {
            if (destroyed) return;
            Exception firstError = null;
            for (int i = 0; i < unfilteredSystems.Count; ++i) InjectSafely(unfilteredSystems[i], entity, ref firstError);
            if (componentSystems.TryGetValue(componentTypeId, out var matches))
                for (int i = 0; i < matches.Count; ++i) InjectSafely(matches[i], entity, ref firstError);
            Rethrow(firstError);
        }

        /// <summary>注入 GameObject 变化。</summary>
        internal void InjectGameObjectChanged(Entity entity)
        {
            if (destroyed) return;
            Exception firstError = null;
            for (int i = 0; i < unfilteredSystems.Count; ++i) InjectSafely(unfilteredSystems[i], entity, ref firstError);
            for (int i = 0; i < gameObjectSystems.Count; ++i) InjectSafely(gameObjectSystems[i], entity, ref firstError);
            Rethrow(firstError);
        }

        /// <summary>调用 routedSystems.Add 完成Route。</summary>
        private void Route(ISystemBase system, Entity entity, ref Exception firstError)
        {
            if (routedSystems.Add(system)) InjectSafely(system, entity, ref firstError);
        }

        /// <summary>注入安全地。</summary>
        private static void InjectSafely(ISystemBase system, Entity entity, ref Exception firstError)
        {
            try { system.InjectEntity(entity); }
            catch (Exception error) { if (firstError == null) firstError = error; }
        }

        /// <summary>重新抛出世界。</summary>
        private static void Rethrow(Exception error)
        {
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        /// <summary>登记系统。</summary>
        public T RegisterSystem<T>() where T : ISystemBase, new()
        {
            RequireManager();
            return (T)Register(ReferencePool.Instance.SpawnInstanceUninitialized<T>());
        }

        /// <summary>登记系统。</summary>
        public ISystemBase RegisterSystem(Type type)
        {
            if (type == null || !typeof(ISystemBase).IsAssignableFrom(type))
                throw new ArgumentException("The type must implement ISystemBase.", nameof(type));
            RequireManager();
            return Register((ISystemBase)ReferencePool.Instance.SpawnInstanceUninitialized(type));
        }

        /// <summary>登记系统。</summary>
        public ISystemBase RegisterSystem(string typeName)
        {
            RequireManager();
            return RegisterSystem(ReferencePool.Instance.ResolveType(typeName));
        }

        /// <summary>登记世界。</summary>
        private ISystemBase Register(ISystemBase system)
        {
            if (system == null) throw new ArgumentException("The system type could not be created.");
            if (destroyed)
            {
                RecycleOrReleaseSystem(system);
                throw new InvalidOperationException("Cannot register a system in a destroyed World.");
            }
            WorldManager manager;
            try { manager = RequireManager(); }
            catch
            {
                RecycleOrReleaseSystem(system);
                throw;
            }
            ulong generation = ++nextSystemGeneration;
            systemGenerations.Add(system, generation);
            if (manager.IsUpdating) manager.Defer(() => RegisterNow(system, generation));
            else RegisterNow(system, generation);
            return system;
        }

        /// <summary>登记立即。</summary>
        private void RegisterNow(ISystemBase system, ulong generation)
        {
            // A pending registration can have been canceled by Destroy or an earlier removal.
            if (!systemGenerations.TryGetValue(system, out var current) || current != generation) return;
            var manager = owner;
            bool beganUpdate = false;
            Exception firstError = null;
            try
            {
                manager = RequireManager();
                manager.BeginUpdate();
                beganUpdate = true;
                if (destroyed) throw new InvalidOperationException("Cannot register a system in a destroyed World.");
                system.World = this;
                ReferencePool.Instance.InitializeInstance(system);
                systems.Add(system);
                Index(system);
                // Registration is infrequent; snapshot protects enumeration from user injection hooks.
                var entities = new List<Entity>(EntityManager.Instance.Entities.Values);
                for (int i = 0; i < entities.Count; ++i)
                    if (entities[i].IsAlive) system.InjectEntity(entities[i]);
            }
            catch (Exception error)
            {
                firstError = error;
                systems.Remove(system);
                Unindex(system);
                systemGenerations.Remove(system);
                try { RecycleOrReleaseSystem(system); }
                catch { /* Preserve the registration failure. A failed recycle is not pooled. */ }
            }
            finally
            {
                try { if (beganUpdate) manager.EndUpdate(); }
                catch (Exception error) { if (firstError == null) firstError = error; }
            }
            Rethrow(firstError);
        }

        // 为系统建立按组件类型和对象依赖的索引，减少每次实体变更的分发范围。
        private void Index(ISystemBase system)
        {
            if (!(system is ISystemFilter filter))
            {
                unfilteredSystems.Add(system);
                return;
            }
            var ids = filter.ComponentTypeIds;
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; ++i)
                {
                    if (!componentSystems.TryGetValue(ids[i], out var matches))
                    {
                        matches = new List<ISystemBase>();
                        componentSystems.Add(ids[i], matches);
                    }
                    if (!matches.Contains(system)) matches.Add(system);
                }
            }
            if (filter.RequiresGameObject) gameObjectSystems.Add(system);
        }

        /// <summary>移除索引世界。</summary>
        private void Unindex(ISystemBase system)
        {
            unfilteredSystems.Remove(system);
            gameObjectSystems.Remove(system);
            foreach (var pair in componentSystems) pair.Value.Remove(system);
        }

        /// <summary>移除系统。</summary>
        public void RemoveSystem(ISystemBase system)
        {
            if (system == null || !systemGenerations.TryGetValue(system, out var generation)) return;
            var manager = owner;
            if (!ReferenceEquals(manager, null) && manager.IsUpdating)
            {
                manager.Defer(() => RemoveSystemNow(system, generation));
                return;
            }
            RemoveSystemNow(system, generation);
        }

        /// <summary>移除系统立即。</summary>
        private void RemoveSystemNow(ISystemBase system, ulong generation)
        {
            if (!systemGenerations.TryGetValue(system, out var current) || current != generation) return;
            systemGenerations.Remove(system);
            systems.Remove(system);
            Unindex(system);
            RecycleOrReleaseSystem(system);
        }

        /// <summary>Structural commands become visible after all three phases complete.</summary>
        /// <remarks>推进已登记系统；更新中发生的系统增删按世界的延迟处理规则提交。</remarks>
        public void OnUpdate(float delta)
        {
            if (destroyed) return;
            var manager = owner;
            if (ReferenceEquals(manager, null)) manager = RequireManager();
            if (manager.IsStopping) return;
            if (manager.HasOpenBatch)
                throw new InvalidOperationException("EndBatch must complete before updating a World.");
            manager.BeginUpdate();
            Exception firstError = null;
            try
            {
                TimePerFrame = delta;
                int count = systems.Count;
                for (int i = 0; i < count; ++i) systems[i].PreUpdate();
                for (int i = 0; i < count; ++i) systems[i].Update();
                for (int i = 0; i < count; ++i) systems[i].PostUpdate();
            }
            catch (Exception error) { firstError = error; }
            finally
            {
                try { manager.EndUpdate(); }
                catch (Exception error) { if (firstError == null) firstError = error; }
            }
            Rethrow(firstError);
        }
    }
}

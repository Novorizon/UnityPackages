using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using UnityEngine;
using UnityEngine.Assertions;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;

#endif
namespace ECS
{
    /// <summary>创建和管理 ECS 实体，维护组件数据和 Unity GameObject 的关联。</summary>
    public partial class EntityManager : SingletonBehaviour<EntityManager>
    {
        private static ulong GUID_COUNT = 1000000;

        /// <summary>创建实体管理器。</summary>
        public static Entity Create()
        {
            var owner = Instance;
            if (owner == null)
            {
                throw new InvalidOperationException("Cannot create entities while the application is quitting.");
            }

            owner.RequireMainThread();
            if (owner.deleted || owner.IsDestroying)
            {
                throw new ObjectDisposedException(nameof(EntityManager));
            }

            var entity = ReferencePool.Instance.SpawnInstance<Entity>();
            entity.GUID = GUID_COUNT++;
            entity.LoadRequestId = 0;
            entity.IsAlive = true;
            owner.Entities.Add(entity.GUID, entity);
            return entity;
        }

        /// <summary>创建实体管理器。</summary>
        public static Entity Create(GameObject gameObject)
        {
            return Create(gameObject, GameObjectOwnership.Owned);
        }

        /// <summary>Owned objects are destroyed with the entity. Borrowed objects remain with their caller.</summary>
        /// <remarks>创建实体管理器。</remarks>
        public static Entity Create(GameObject gameObject, GameObjectOwnership ownership)
        {
            Assert.IsNotNull(gameObject);
            if (ownership != GameObjectOwnership.Owned && ownership != GameObjectOwnership.Borrowed)
            {
                throw new ArgumentOutOfRangeException(nameof(ownership));
            }

            var manager = Instance;
            if (manager == null)
            {
                throw new InvalidOperationException("Cannot create entities while the application is quitting.");
            }

            manager.RequireMainThread();
            if (manager.deleted || manager.IsDestroying)
            {
                throw new ObjectDisposedException(nameof(EntityManager));
            }

            if (manager.GameObjectEntities.TryGetValue(gameObject, out var entity))
            {
                return entity;
            }

            return Create(ownership == GameObjectOwnership.Borrowed ? GameObjectLease.Borrowed(gameObject) : GameObjectLease.Owned(gameObject));
        }

        /// <summary>使用带租约的 GameObject 创建实体，由实体生命周期管理这份租约。</summary>
        public static Entity Create(GameObjectLease lease)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            if (lease.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(lease));
            }

            var owner = Instance;
            if (owner == null)
            {
                throw new InvalidOperationException("Cannot create entities while the application is quitting.");
            }

            owner.RequireMainThread();
            if (owner.deleted || owner.IsDestroying)
            {
                throw new ObjectDisposedException(nameof(EntityManager));
            }

            if (lease.GameObject == null)
            {
                throw new ArgumentException("The leased GameObject has been destroyed.", nameof(lease));
            }

            if (owner.GameObjectEntities.TryGetValue(lease.GameObject, out var existing))
            {
                if (owner.bindings.TryGetValue(existing, out var current) && ReferenceEquals(current, lease))
                {
                    return existing;
                }

                throw new InvalidOperationException("The GameObject already has a binding. Reuse its existing lease.");
            }

            owner.ValidateLeaseBinding(null, lease);
            var entity = Create();
            owner.BindGameObject(entity, lease);
            return entity;
        }

#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), DictionaryDrawerSettings(IsReadOnly = true, DisplayMode = DictionaryDisplayOptions.Foldout)]
#endif
        internal readonly Dictionary<GameObject, Entity> GameObjectEntities = new Dictionary<GameObject, Entity>();
#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        internal readonly Dictionary<ulong, Entity> Entities = new Dictionary<ulong, Entity>();
        public bool IsDataMode { get; private set; }
        /// <summary>Explicit resource integration. Pure data entities and existing GameObjects need no provider.</summary>
        public IGameObjectProvider GameObjectProvider { get; set; }

        private readonly Dictionary<Entity, GameObjectLease> bindings = new Dictionary<Entity, GameObjectLease>();
        // Includes leases waiting for a batched HybridSystem refresh, even after their binding was replaced.
        private readonly HashSet<GameObjectLease> heldLeases = new HashSet<GameObjectLease>();
        private readonly Dictionary<GameObject, GameObjectLease> heldObjects = new Dictionary<GameObject, GameObjectLease>();
        private readonly HashSet<LoadOperation> pendingLoads = new HashSet<LoadOperation>();
        private readonly List<DelayedRelease> delayedReleases = new List<DelayedRelease>();
        private int mainThreadId;
        private SynchronizationContext mainContext;
        private bool deleted;
        private bool advancingTime;
        /// <summary>表示实体管理器中的Load 操作。</summary>
        private sealed class LoadOperation
        {
            internal Entity Entity;
            internal ulong Guid;
            internal ulong Request;
            internal Action<Entity, object> Callback;
            internal object UserData;
            internal bool Received;
            internal bool Finished;
            internal bool Cancelled;
            internal GameObjectLease Lease;
        }

        /// <summary>保存Delayed 发布版本所需的数据与配套操作。</summary>
        private struct DelayedRelease
        {
            internal float Remaining;
            internal GameObjectLease Lease;
        }

        /// <summary>响应已初始化。</summary>
        protected override void OnInitialized()
        {
            deleted = false;
            advancingTime = false;
            IsDataMode = false;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            mainContext = SynchronizationContext.Current;
            base.OnInitialized();
        }

        /// <summary>检查主线程是否满足前置条件。</summary>
        private void RequireMainThread()
        {
            // Also supports hosts which construct their singleton without a Unity Awake callback.
            if (mainThreadId == 0)
            {
                mainThreadId = Thread.CurrentThread.ManagedThreadId;
                mainContext = SynchronizationContext.Current;
            }

            if (mainThreadId != Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException("EntityManager and GameObject leases must be used on Unity's main thread.");
            }
        }

        /// <summary>响应更新。</summary>
        protected override void OnUpdate(float delta)
        {
            AdvanceTime(delta);
        }

        /// <summary>Advance delayed native-object releases using scaled time. Called automatically by the singleton update.</summary>
        /// <remarks>推进时间。</remarks>
        public void AdvanceTime(float deltaTime)
        {
            RequireMainThread();
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (deleted || IsDestroying || advancingTime)
            {
                return;
            }

            advancingTime = true;
            Exception firstError = null;
            try
            {
                for (int i = delayedReleases.Count - 1; i >= 0; --i)
                {
                    var item = delayedReleases[i];
                    item.Remaining -= deltaTime;
                    if (item.Remaining > 0)
                    {
                        delayedReleases[i] = item;
                        continue;
                    }

                    delayedReleases.RemoveAt(i);
                    try
                    {
                        ReleaseLease(item.Lease);
                    }
                    catch (Exception error)
                    {
                        if (firstError == null)
                        {
                            firstError = error;
                        }
                    }

                    if (deleted)
                    {
                        break;
                    }
                }
            }
            finally
            {
                advancingTime = false;
            }

            if (firstError != null)
            {
                ExceptionDispatchInfo.Capture(firstError).Throw();
            }
        }

        /// <summary>响应 Delete。</summary>
        protected override void OnDelete()
        {
            deleted = true;
            Exception firstError = null;
            foreach (var operation in pendingLoads.ToArray())
            {
                try
                {
                    CancelLoad(operation);
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }

            var retiringEntities = Entities.Values.ToArray();
            var retiringComponents = new List<IComponent>();
            foreach (var entity in retiringEntities)
            {
                entity.IsAlive = false;
                ++entity.LoadRequestId;
                retiringComponents.AddRange(entity.components.Values);
                entity.components.Clear();
            }

            // Cleanup only uses existing services; it must never create a singleton during shutdown.
            if (WorldManager.TryGetInstance(out var worlds))
            {
                foreach (var entity in retiringEntities)
                {
                    try
                    {
                        worlds.NotifyEntityDestroyed(entity);
                    }
                    catch (Exception error)
                    {
                        if (firstError == null)
                        {
                            firstError = error;
                        }
                    }
                }
            }

            if (ReferencePool.TryGetInstance(out var pool))
            {
                foreach (var component in retiringComponents)
                {
                    try
                    {
                        pool.RecycleInstance(component);
                    }
                    catch (Exception error)
                    {
                        if (firstError == null)
                        {
                            firstError = error;
                        }
                    }
                }
            }
            else
            {
                // Unity may destroy the pool first. Run component cleanup without constructing another pool.
                foreach (var component in retiringComponents)
                {
                    try
                    {
                        if (component is IRecycleable recycleable)
                        {
                            recycleable.OnRecycle();
                        }
                    }
                    catch (Exception error)
                    {
                        if (firstError == null)
                        {
                            firstError = error;
                        }
                    }
                }
            }

            foreach (var lease in heldLeases.ToArray())
            {
                try
                {
                    ReleaseLease(lease);
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }

            foreach (var item in delayedReleases.ToArray())
            {
                try
                {
                    item.Lease.Dispose();
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }

            foreach (var entity in retiringEntities)
            {
                entity.gameObject = null;
                entity.GUID = 0;
            }

            pendingLoads.Clear();
            bindings.Clear();
            heldLeases.Clear();
            heldObjects.Clear();
            delayedReleases.Clear();
            GameObjectEntities.Clear();
            Entities.Clear();
            GameObjectProvider = null;
            base.OnDelete();
            if (firstError != null)
            {
                ExceptionDispatchInfo.Capture(firstError).Throw();
            }
        }

        /// <summary>释放租约。</summary>
        private void ReleaseLease(GameObjectLease lease)
        {
            try
            {
                lease.Dispose();
            }
            finally
            {
                heldLeases.Remove(lease);
                if (heldObjects.TryGetValue(lease.GameObject, out var current) && ReferenceEquals(current, lease))
                {
                    heldObjects.Remove(lease.GameObject);
                }
            }
        }

        /// <summary>调用 heldLeases.Add 完成Hold 租约。</summary>
        private void HoldLease(GameObjectLease lease)
        {
            heldLeases.Add(lease);
            heldObjects[lease.GameObject] = lease;
        }

        /// <summary>验证租约绑定。</summary>
        private void ValidateLeaseBinding(Entity entity, GameObjectLease lease)
        {
            if (heldObjects.TryGetValue(lease.GameObject, out var held) && !ReferenceEquals(held, lease))
            {
                throw new InvalidOperationException("The GameObject already has a retained lease; wait for its release before binding it again.");
            }

            if (heldLeases.Contains(lease) && (entity == null || !bindings.TryGetValue(entity, out var current) || !ReferenceEquals(current, lease)))
            {
                throw new InvalidOperationException("A lease awaiting release cannot be bound again.");
            }
        }

        /// <summary>启用数据模式。</summary>
        public void EnableDataMode(bool tf)
        {
            IsDataMode = tf;
        }

        /// <summary>尝试取得实体，返回是否成功。</summary>
        public bool TryGetEntity(GameObject obj, out Entity entity)
        {
            return GameObjectEntities.TryGetValue(obj, out entity);
        }

        /// <summary>尝试取得实体，返回是否成功。</summary>
        public bool TryGetEntity(ulong key, out Entity entity)
        {
            return Entities.TryGetValue(key, out entity);
        }

        /// <summary>按实体查询匹配结果。</summary>
        public List<Entity> QueryEntities(Func<Entity, bool> query)
        {
            return Entities.Values.Where(query).ToList();
        }

        /// <summary>按实体查询匹配结果。</summary>
        public List<T> QueryEntities<T>(Func<Entity, bool> query, Func<Entity, T> select)
        {
            return Entities.Values.Where(query).Select(select).ToList();
        }

        /// <summary>按实体游戏对象查询匹配结果。</summary>
        public List<GameObject> QueryEntityGameObjects(Func<Entity, bool> query)
        {
            return QueryEntities(query, entity => entity.gameObject);
        }

        /// <summary>检查输入是否仍是当前有效值。</summary>
        private bool IsCurrent(Entity entity, ulong guid)
        {
            return !deleted && !IsDestroying && entity != null && entity.IsAlive && entity.GUID == guid && Entities.TryGetValue(guid, out var current) && ReferenceEquals(current, entity);
        }

        /// <summary>检查 Alive 是否满足前置条件。</summary>
        private void RequireAlive(Entity entity)
        {
            RequireMainThread();
            if (deleted || IsDestroying)
            {
                throw new ObjectDisposedException(nameof(EntityManager));
            }

            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (!IsCurrent(entity, entity.GUID))
            {
                throw new InvalidOperationException("Entity is not alive in this manager.");
            }
        }

        /// <summary>调用 entity.components.ContainsKey 完成内部 Add 组件数据。</summary>
        internal void InternalAddComponentData(Entity entity, int typeId, IComponent comp)
        {
            if (entity.components.ContainsKey(typeId))
            {
                throw new InvalidOperationException("The entity already contains component " + comp.GetType().FullName);
            }

            entity.components.Add(typeId, comp);
        }

        /// <summary>返回内部 Remove 组件数据的判断结果。</summary>
        internal bool InternalRemoveComponentData(Entity entity, int typeId, out IComponent comp)
        {
            if (!entity.components.TryGetValue(typeId, out comp))
            {
                return false;
            }

            entity.components.Remove(typeId);
            return true;
        }

        /// <summary>附加 Spawned 组件。</summary>
        private void AttachSpawnedComponent(Entity entity, ulong guid, int typeId, IComponent component)
        {
            try
            {
                // User initialization hooks can destroy/reuse the entity or add the same component.
                if (!IsCurrent(entity, guid))
                {
                    throw new InvalidOperationException("The entity changed lifetime during component initialization.");
                }

                InternalAddComponentData(entity, typeId, component);
            }
            catch (Exception attachError)
            {
                try
                {
                    ReferencePool.Instance.RecycleInstance(component);
                }
                catch (Exception recycleError)
                {
                    throw new AggregateException("Component attachment and cleanup both failed.", attachError, recycleError);
                }

                throw;
            }
        }

        /// <summary>添加组件数据。</summary>
        public T AddComponentData<T>(Entity entity)
            where T : class, IComponent, new()
        {
            RequireAlive(entity);
            int typeId = ComponentType.GetId<T>();
            if (entity.components.ContainsKey(typeId))
            {
                throw new InvalidOperationException("The entity already contains " + typeof(T).FullName);
            }

            ulong guid = entity.GUID;
            var comp = ReferencePool.Instance.SpawnInstance<T>();
            AttachSpawnedComponent(entity, guid, typeId, comp);
            WorldManager.Instance.NotifyComponentChanged(entity, typeId);
            return comp;
        }

        /// <summary>添加组件数据。</summary>
        public IComponent AddComponentData(Entity entity, Type type)
        {
            RequireAlive(entity);
            int typeId = ComponentType.GetId(type);
            if (entity.components.ContainsKey(typeId))
            {
                throw new InvalidOperationException("The entity already contains " + type.FullName);
            }

            ulong guid = entity.GUID;
            var comp = (IComponent)ReferencePool.Instance.SpawnInstance(type);
            AttachSpawnedComponent(entity, guid, typeId, comp);
            WorldManager.Instance.NotifyComponentChanged(entity, typeId);
            return comp;
        }

        /// <summary>添加组件数据。</summary>
        public IComponent AddComponentData(Entity entity, string typeName)
        {
            return AddComponentData(entity, ComponentType.Resolve(typeName));
        }

        /// <summary>添加组件数据 By 原型。</summary>
        public void AddComponentDataByArchetype(Entity entity, EntityArchetype archetype, out IComponent[] components)
        {
            RequireAlive(entity);
            ulong guid = entity.GUID;
            if (archetype.typeNames == null)
            {
                throw new ArgumentException("The archetype is not initialized.", nameof(archetype));
            }

            int count = archetype.typeNames.Length;
            var types = new Type[count];
            var ids = new int[count];
            var unique = new HashSet<int>();
            // Validate the complete operation before touching the entity or allocating pooled components.
            for (int i = 0; i < count; i++)
            {
                types[i] = ComponentType.Resolve(archetype.typeNames[i]);
                ids[i] = ComponentType.GetId(types[i]);
                if (!unique.Add(ids[i]) || entity.components.ContainsKey(ids[i]))
                {
                    throw new InvalidOperationException("Duplicate archetype component: " + types[i].FullName);
                }
            }

            components = new IComponent[count];
            int spawned = 0;
            try
            {
                for (; spawned < count; spawned++)
                {
                    components[spawned] = (IComponent)ReferencePool.Instance.SpawnInstance(types[spawned]);
                }

                if (!IsCurrent(entity, guid))
                {
                    throw new InvalidOperationException("The entity changed lifetime during archetype initialization.");
                }

                for (int i = 0; i < count; ++i)
                {
                    if (entity.components.ContainsKey(ids[i]))
                    {
                        throw new InvalidOperationException("Component initialization added an archetype component: " + types[i].FullName);
                    }
                }
            }
            catch (Exception spawnError)
            {
                List<Exception> cleanupErrors = null;
                for (int i = 0; i < spawned; i++)
                {
                    try
                    {
                        ReferencePool.Instance.RecycleInstance(components[i]);
                    }
                    catch (Exception cleanupError)
                    {
                        if (cleanupErrors == null)
                        {
                            cleanupErrors = new List<Exception>
                            {
                                spawnError
                            };
                        }

                        cleanupErrors.Add(cleanupError);
                    }
                }

                if (cleanupErrors != null)
                {
                    throw new AggregateException("Archetype initialization and cleanup failed.", cleanupErrors);
                }

                throw;
            }

            var manager = WorldManager.Instance;
            manager.BeginBatch();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    entity.components.Add(ids[i], components[i]);
                }

                for (int i = 0; i < count; i++)
                {
                    manager.NotifyComponentChanged(entity, ids[i]);
                }
            }
            finally
            {
                manager.EndBatch();
            }
        }

        /// <summary>获取组件数据。</summary>
        public T GetComponentData<T>(Entity entity)
            where T : class, IComponent
        {
            return (T)GetComponentData(entity, ComponentType.GetId<T>());
        }

        /// <summary>获取组件数据。</summary>
        public IComponent GetComponentData(Entity entity, Type type)
        {
            return GetComponentData(entity, ComponentType.GetId(type));
        }

        /// <summary>获取组件数据。</summary>
        public IComponent GetComponentData(Entity entity, string typeName)
        {
            return GetComponentData(entity, ComponentType.GetId(ComponentType.Resolve(typeName)));
        }

        /// <summary>The integer is a ComponentType ID, not an Entity.StringToHash result.</summary>
        /// <remarks>获取组件数据。</remarks>
        public IComponent GetComponentData(Entity entity, int typeId)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            return entity.components.TryGetValue(typeId, out var component) ? component : null;
        }

        /// <summary>移除组件数据。</summary>
        public void RemoveComponentData<T>(Entity entity)
            where T : class, IComponent
        {
            RemoveComponentData(entity, ComponentType.GetId<T>());
        }

        /// <summary>移除组件数据。</summary>
        public void RemoveComponentData(Entity entity, Type type)
        {
            RemoveComponentData(entity, ComponentType.GetId(type));
        }

        /// <summary>移除组件数据。</summary>
        public void RemoveComponentData(Entity entity, string typeName)
        {
            RemoveComponentData(entity, ComponentType.GetId(ComponentType.Resolve(typeName)));
        }

        /// <summary>移除组件数据。</summary>
        public void RemoveComponentData(Entity entity, int typeId)
        {
            RequireAlive(entity);
            var manager = WorldManager.Instance;
            if (manager.IsUpdating)
            {
                ulong guid = entity.GUID;
                manager.Defer(() =>
                {
                    if (IsCurrent(entity, guid))
                    {
                        RemoveComponentData(entity, typeId);
                    }
                });
                return;
            }

            var pool = ReferencePool.Instance;
            if (!InternalRemoveComponentData(entity, typeId, out var comp))
            {
                return;
            }

            try
            {
                manager.NotifyComponentChanged(entity, typeId);
            }
            finally
            {
                // Batched matching may still hold the old reference until the final notification.
                if (manager.IsBatching)
                {
                    manager.AfterChangesApplied(() => pool.RecycleInstance(comp));
                }
                else
                {
                    pool.RecycleInstance(comp);
                }
            }
        }

        /// <summary>销毁实体。</summary>
        public void DestroyEntity(ulong key)
        {
            if (Entities.TryGetValue(key, out var entity))
            {
                DestroyEntity(entity);
            }
        }

        /// <summary>销毁全部。</summary>
        public void DestroyAll()
        {
            foreach (var entity in Entities.Values.ToArray())
            {
                DestroyEntity(entity);
            }
        }

        /// <summary>销毁实体。</summary>
        public void DestroyEntity(Entity entity, float delay = 0)
        {
            RequireMainThread();
            if (float.IsNaN(delay) || float.IsInfinity(delay))
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }

            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (deleted || IsDestroying)
            {
                return;
            }

            if (!IsCurrent(entity, entity.GUID))
            {
                return;
            }

            var manager = WorldManager.Instance;
            if (manager.IsUpdating)
            {
                ulong guid = entity.GUID;
                manager.Defer(() =>
                {
                    if (IsCurrent(entity, guid))
                    {
                        DestroyEntity(entity, delay);
                    }
                });
                return;
            }

            entity.IsAlive = false;
            entity.LoadRequestId++;
            var retired = new IComponent[entity.components.Count];
            entity.components.Values.CopyTo(retired, 0);
            entity.components.Clear();
            // Always eject before either components or this Entity reference can be pooled and reused.
            Exception firstError = null;
            try
            {
                CancelLoads(entity);
            }
            catch (Exception error)
            {
                firstError = error;
            }

            try
            {
                manager.NotifyEntityDestroyed(entity);
            }
            catch (Exception error)
            {
                if (firstError == null)
                {
                    firstError = error;
                }
            }

            foreach (var comp in retired)
            {
                try
                {
                    ReferencePool.Instance.RecycleInstance(comp);
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }

            var go = entity.gameObject;
            bindings.TryGetValue(entity, out var lease);
            bindings.Remove(entity);
            if (!ReferenceEquals(go, null))
            {
                GameObjectEntities.Remove(go);
            }

            if (lease == null && go != null)
            {
                lease = GameObjectLease.Owned(go);
                HoldLease(lease);
            }

            if (lease != null)
            {
                try
                {
                    if (delay <= 0)
                    {
                        ReleaseLease(lease);
                    }
                    else
                    {
                        delayedReleases.Add(new DelayedRelease { Remaining = delay, Lease = lease });
                    }
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }

            Entities.Remove(entity.GUID);
            entity.GUID = 0;
            entity.gameObject = null;
            if (!ReferencePool.applicationIsQuitting)
            {
                try
                {
                    ReferencePool.Instance.RecycleInstance(entity);
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }

            if (firstError != null)
            {
                ExceptionDispatchInfo.Capture(firstError).Throw();
            }
        }

        /// <summary>加载 GameObject。</summary>
        public void LoadGameObject(Entity entity, string assetPath, Transform parent = null, Action<Entity, object> callback = null, object userdata = null)
        {
            RequireAlive(entity);
            if (IsDataMode)
            {
                return;
            }

            BeginLoad(entity, new GameObjectRequest(assetPath, parent), callback, userdata);
        }

        /// <summary>加载 GameObject。</summary>
        public void LoadGameObject(Entity entity, string assetPath, Vector3 position, Quaternion rotation, Action<Entity, object> callback = null, object userdata = null)
        {
            RequireAlive(entity);
            if (IsDataMode)
            {
                return;
            }

            BeginLoad(entity, new GameObjectRequest(assetPath, position, rotation), callback, userdata);
        }

        /// <summary>开始加载实体管理器。</summary>
        private void BeginLoad(Entity entity, GameObjectRequest request, Action<Entity, object> callback, object userdata)
        {
            var provider = GameObjectProvider;
            if (provider == null)
            {
                throw new InvalidOperationException("Configure EntityManager.GameObjectProvider before loading a GameObject.");
            }

            ulong guid = entity.GUID;
            ulong requestId = ++entity.LoadRequestId;
            CancelLoads(entity);
            // A lease's release hook can destroy/reuse this entity or start a newer load.
            if (!IsCurrent(entity, guid) || entity.LoadRequestId != requestId)
            {
                throw new InvalidOperationException("The entity lifetime or load request changed during cancellation.");
            }

            var operation = new LoadOperation
            {
                Entity = entity,
                Guid = guid,
                Request = requestId,
                Callback = callback,
                UserData = userdata
            };
            pendingLoads.Add(operation);
            try
            {
                provider.LoadAsync(request, lease => ReceiveLoad(operation, lease));
            }
            catch (Exception loadError)
            {
                // A synchronous user callback can throw after the binding has already transferred.
                if (!operation.Finished)
                {
                    try
                    {
                        CancelLoad(operation);
                    }
                    catch (Exception cleanupError)
                    {
                        throw new AggregateException("GameObject loading and cleanup both failed.", loadError, cleanupError);
                    }
                }

                throw;
            }
        }

        /// <summary>取消 Load。</summary>
        private void CancelLoad(LoadOperation operation)
        {
            operation.Cancelled = true;
            pendingLoads.Remove(operation);
            if (operation.Received && !operation.Finished)
            {
                operation.Finished = true;
                operation.Lease?.Dispose();
            }
        }

        /// <summary>取消 Loads。</summary>
        private void CancelLoads(Entity entity)
        {
            Exception firstError = null;
            foreach (var operation in pendingLoads.ToArray())
            {
                if (!ReferenceEquals(operation.Entity, entity))
                {
                    continue;
                }

                try
                {
                    CancelLoad(operation);
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }

            if (firstError != null)
            {
                ExceptionDispatchInfo.Capture(firstError).Throw();
            }
        }

        /// <summary>接收 Load。</summary>
        private void ReceiveLoad(LoadOperation operation, GameObjectLease lease)
        {
            if (mainThreadId != Thread.CurrentThread.ManagedThreadId)
            {
                if (mainContext == null)
                {
                    throw new InvalidOperationException("IGameObjectProvider must complete on Unity's main thread; no Unity synchronization context is available.");
                }

                mainContext.Post(_ =>
                {
                    RequireMainThread();
                    ReceiveLoad(operation, lease);
                }, null);
                return;
            }

            if (operation.Received)
            {
                // A repeated callback with the same lease must not release the current binding.
                if (!ReferenceEquals(operation.Lease, lease))
                {
                    if (lease != null && operation.Lease != null && ReferenceEquals(lease.GameObject, operation.Lease.GameObject))
                    {
                        throw new InvalidOperationException("The provider completed twice with different leases for the same GameObject.");
                    }

                    lease?.Dispose();
                }

                return;
            }

            operation.Received = true;
            operation.Lease = lease;
            FinishLoad(operation);
        }

        /// <summary>结束 Load。</summary>
        private void FinishLoad(LoadOperation operation)
        {
            if (operation.Finished)
            {
                return;
            }

            var lease = operation.Lease;
            var entity = operation.Entity;
            if (deleted || IsDestroying || operation.Cancelled || lease == null || lease.IsDisposed || !IsCurrent(entity, operation.Guid) || entity.LoadRequestId != operation.Request || lease.GameObject == null)
            {
                operation.Finished = true;
                pendingLoads.Remove(operation);
                lease?.Dispose();
                return;
            }

            var manager = WorldManager.Instance;
            if (manager.IsUpdating)
            {
                manager.Defer(() => FinishLoad(operation));
                return;
            }

            // Retire the operation before notification/user callbacks can start another load.
            operation.Finished = true;
            pendingLoads.Remove(operation);
            try
            {
                BindGameObject(entity, lease);
            }
            catch (Exception bindError)
            {
                if (!bindings.TryGetValue(entity, out var current) || !ReferenceEquals(current, lease))
                {
                    try
                    {
                        lease.Dispose();
                    }
                    catch (Exception cleanupError)
                    {
                        throw new AggregateException("GameObject binding and cleanup both failed.", bindError, cleanupError);
                    }
                }

                throw;
            }

            if (IsCurrent(entity, operation.Guid) && entity.LoadRequestId == operation.Request)
            {
                operation.Callback?.Invoke(entity, operation.UserData);
            }
        }

        /// <summary>Transfer an existing native object's lease to this entity.</summary>
        /// <remarks>绑定 GameObject。</remarks>
        public void BindGameObject(Entity entity, GameObjectLease lease)
        {
            RequireAlive(entity);
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            if (lease.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(lease));
            }

            var go = lease.GameObject;
            if (go == null)
            {
                throw new ArgumentException("The leased GameObject has been destroyed.", nameof(lease));
            }

            ValidateLeaseBinding(entity, lease);
            if (GameObjectEntities.TryGetValue(go, out var owner) && !ReferenceEquals(owner, entity))
            {
                throw new InvalidOperationException("The GameObject already belongs to another entity.");
            }

            var previous = entity.gameObject;
            bindings.TryGetValue(entity, out var previousLease);
            if (ReferenceEquals(previous, go) && previousLease != null && !ReferenceEquals(previousLease, lease))
            {
                throw new InvalidOperationException("The GameObject is already bound through a different lease.");
            }

            if (!ReferenceEquals(previous, null) && !ReferenceEquals(previous, go))
            {
                GameObjectEntities.Remove(previous);
            }

            if (previousLease == null && previous != null && !ReferenceEquals(previous, go))
            {
                previousLease = GameObjectLease.Owned(previous);
                HoldLease(previousLease);
            }

            entity.gameObject = go;
            bindings[entity] = lease;
            HoldLease(lease);
#if UNITY_EDITOR
            entity.name = go.name.Replace("(Spawn)", "");
#endif
            GameObjectEntities[go] = entity;
            var manager = WorldManager.Instance;
            Exception notificationError = null;
            try
            {
                manager.NotifyGameObjectChanged(entity);
            }
            catch (Exception error)
            {
                notificationError = error;
            }

            if (previousLease != null && !ReferenceEquals(previousLease, lease))
            {
                try
                {
                    if (manager.IsBatching)
                    {
                        manager.AfterChangesApplied(() => ReleaseLease(previousLease));
                    }
                    else
                    {
                        ReleaseLease(previousLease);
                    }
                }
                catch (Exception cleanupError)
                {
                    if (notificationError != null)
                    {
                        throw new AggregateException("GameObject notification and previous binding cleanup both failed.", notificationError, cleanupError);
                    }

                    throw;
                }
            }

            if (notificationError != null)
            {
                ExceptionDispatchInfo.Capture(notificationError).Throw();
            }
        }

        /// <summary>添加组件对象。</summary>
        public T AddComponentObject<T>(Entity entity)
            where T : Component
        {
            RequireAlive(entity);
            if (entity.gameObject == null)
            {
                return null;
            }

            var comp = entity.gameObject.AddComponent<T>();
            WorldManager.Instance.NotifyGameObjectChanged(entity);
            return comp;
        }

        /// <summary>获取组件对象。</summary>
        public T GetComponentObject<T>(Entity entity)
            where T : Component
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            return entity.gameObject != null ? entity.gameObject.GetComponent<T>() : null;
        }

        /// <summary>移除组件对象。</summary>
        public void RemoveComponentObject<T>(Entity entity)
            where T : Component
        {
            RequireAlive(entity);
            var manager = WorldManager.Instance;
            if (manager.IsUpdating)
            {
                ulong guid = entity.GUID;
                manager.Defer(() =>
                {
                    if (IsCurrent(entity, guid))
                    {
                        RemoveComponentObject<T>(entity);
                    }
                });
                return;
            }

            var comp = GetComponentObject<T>(entity);
            if (comp == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(comp);
            manager.NotifyGameObjectChanged(entity);
        }

        /// <summary>检查是否存在组件。</summary>
        public bool HasComponent<T>(Entity entity)
            where T : class, IComponent
        {
            return GetComponentData<T>(entity) != null;
        }

        /// <summary>获取组件。</summary>
        public T GetComponent<T>(Entity entity)
            where T : class, IComponent
        {
            return GetComponentData<T>(entity);
        }
    }

    /// <summary>实体对应的扩展方法。为相关类型提供扩展方法。</summary>
    public static class EntityExtensions
    {
        /// <summary>获取 OrAdd 组件数据。</summary>
        public static T GetOrAddComponentData<T>(this Entity entity)
            where T : class, IComponent, new()
        {
            var comp = EntityManager.Instance.GetComponentData<T>(entity);
            return comp ?? EntityManager.Instance.AddComponentData<T>(entity);
        }

        /// <summary>获取 OrAdd 对象组件。</summary>
        public static T GetOrAddObjectComponent<T>(this Entity entity)
            where T : Component
        {
            var comp = EntityManager.Instance.GetComponentObject<T>(entity);
            return comp != null ? comp : EntityManager.Instance.AddComponentObject<T>(entity);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using UnityEngine;
#if ODIN_INSPECTOR
using Sirenix.OdinInspector;

#endif
namespace ECS
{
    /// <summary>Main-thread structural transactions and change routing for registered worlds.</summary>
    /// <remarks>世界对应的管理器。管理对应模块的状态和操作入口。</remarks>
    public class WorldManager : SingletonBehaviour<WorldManager>
    {
        /// <summary>表示世界管理器中的待处理变更。</summary>
        private sealed class PendingChange
        {
            internal ulong Guid;
            internal bool All;
            internal bool GameObject;
            internal readonly List<int> ComponentTypes = new List<int>();
            /// <summary>恢复该类型约定的初始状态。</summary>
            internal void Reset()
            {
                Guid = 0;
                All = false;
                GameObject = false;
                ComponentTypes.Clear();
            }
        }

        private World LocalWorld;
        /// <summary>The stable default world, automatically updated by this Unity behaviour.</summary>
        public World DefaultWorld
        {
            get
            {
                return LocalWorld;
            }
        }

#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        private readonly List<IWorld> AllWorlds = new List<IWorld>();
        private readonly HashSet<IWorld> registeredWorlds = new HashSet<IWorld>();
        private readonly List<Action> commands = new List<Action>();
        private readonly List<Action> afterChanges = new List<Action>();
        private readonly Dictionary<Entity, PendingChange> pendingChanges = new Dictionary<Entity, PendingChange>();
        private readonly List<Entity> dirtyOrder = new List<Entity>();
        private readonly Stack<PendingChange> changePool = new Stack<PendingChange>();
        private int updateDepth;
        private int batchDepth;
        private int dispatchDepth;
        private bool isFlushing;
        private bool deleted;
        internal bool IsStopping => deleted || IsDestroying || Ecs.IsQuitting;
        /// <summary>Also defers reentrant mutations requested by user injection callbacks.</summary>
        public bool IsUpdating => updateDepth > 0 || dispatchDepth > 0;
        public bool IsBatching => IsUpdating || batchDepth > 0 || isFlushing;
        internal bool HasOpenBatch => batchDepth > 0;

        /// <summary>初始化世界管理器。</summary>
        public override void Initialize()
        {
        }

        /// <summary>开始批次。</summary>
        public void BeginBatch()
        {
            if (IsStopping)
            {
                throw new InvalidOperationException("Cannot begin a batch while ECS is shutting down.");
            }

            ++batchDepth;
        }

        /// <summary>Pairs BeginBatch/EndBatch even when the caller throws; scopes can be nested.</summary>
        /// <remarks>开启实体变更批次，释放作用域后结束批次并按当前调度状态处理累计变更。</remarks>
        public IDisposable Batch()
        {
            BeginBatch();
            return new BatchScope(this);
        }

        /// <summary>批次对应的作用域。保存作用域内需要统一管理的信息。</summary>
        private sealed class BatchScope : IDisposable
        {
            private WorldManager owner;
            /// <summary>使用调用方提供的owner创建 BatchScope。</summary>
            internal BatchScope(WorldManager owner)
            {
                this.owner = owner;
            }

            /// <summary>结束批次作用域对相关资源的持有，并执行该类型的清理流程。</summary>
            public void Dispose()
            {
                var manager = owner;
                owner = null;
                if (manager != null)
                {
                    manager.EndBatch();
                }
            }
        }

        /// <summary>结束批次。</summary>
        public void EndBatch()
        {
            if (batchDepth == 0)
            {
                throw new InvalidOperationException("EndBatch has no matching BeginBatch.");
            }

            --batchDepth;
            FlushIfReady();
        }

        /// <summary>开始更新。</summary>
        internal void BeginUpdate()
        {
            ++updateDepth;
        }

        /// <summary>开始 Dispatch。</summary>
        internal void BeginDispatch()
        {
            ++dispatchDepth;
        }

        /// <summary>结束 Dispatch。</summary>
        internal void EndDispatch()
        {
            --dispatchDepth;
            FlushIfReady();
        }

        /// <summary>结束更新。</summary>
        internal void EndUpdate()
        {
            if (updateDepth == 0)
            {
                throw new InvalidOperationException("EndUpdate has no matching BeginUpdate.");
            }

            --updateDepth;
            FlushIfReady();
        }

        /// <summary>在世界暂时不适合修改结构时排队命令。</summary>
        public void Defer(Action command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            if (IsUpdating)
            {
                commands.Add(command);
            }
            else
            {
                command();
            }
        }

        /// <summary>Release removed components only after every cached system reference is detached.</summary>
        /// <remarks>调用 nameof 完成之后变更 Applied。</remarks>
        public void AfterChangesApplied(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (IsBatching || pendingChanges.Count > 0)
            {
                afterChanges.Add(action);
            }
            else
            {
                action();
            }
        }

        /// <summary>注入实体 ToWorlds。</summary>
        public void InjectEntityToWorlds(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (IsBatching)
            {
                GetPending(entity).All = true;
            }
            else
            {
                Dispatch(entity, null, false, true, 0, false);
            }
        }

        /// <summary>通知组件变化。</summary>
        public void NotifyComponentChanged(Entity entity, int componentTypeId)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (deleted)
            {
                return;
            }

            if (IsBatching)
            {
                var change = GetPending(entity);
                if (!change.All && !change.ComponentTypes.Contains(componentTypeId))
                {
                    change.ComponentTypes.Add(componentTypeId);
                }
            }
            else
            {
                Dispatch(entity, null, false, false, componentTypeId, true);
            }
        }

        /// <summary>通知 GameObject 变化。</summary>
        public void NotifyGameObjectChanged(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (deleted)
            {
                return;
            }

            if (IsBatching)
            {
                GetPending(entity).GameObject = true;
            }
            else
            {
                Dispatch(entity, null, true, false, 0, false);
            }
        }

        /// <summary>Call after marking dead/clearing data and before recycling any entity or component.</summary>
        /// <remarks>通知实体已销毁。</remarks>
        public void NotifyEntityDestroyed(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (pendingChanges.TryGetValue(entity, out var change))
            {
                pendingChanges.Remove(entity);
                RecycleChange(change);
            }

            Dispatch(entity, null, false, true, 0, false);
        }

        /// <summary>获取待处理。</summary>
        private PendingChange GetPending(Entity entity)
        {
            if (pendingChanges.TryGetValue(entity, out var change))
            {
                return change;
            }

            change = changePool.Count > 0 ? changePool.Pop() : new PendingChange();
            change.Guid = entity.GUID;
            pendingChanges.Add(entity, change);
            dirtyOrder.Add(entity);
            return change;
        }

        /// <summary>回收变更。</summary>
        private void RecycleChange(PendingChange change)
        {
            change.Reset();
            changePool.Push(change);
        }

        /// <summary>调度世界管理器。</summary>
        private void Dispatch(Entity entity, IList<int> componentTypeIds, bool gameObjectChanged, bool all, int singleType, bool single)
        {
            ++dispatchDepth;
            Exception firstError = null;
            try
            {
                for (int i = 0; i < AllWorlds.Count; ++i)
                {
                    var world = AllWorlds[i];
                    try
                    {
                        if (all || !(world is World filteredWorld))
                        {
                            world.InjectEntity(entity);
                        }
                        else if (single)
                        {
                            filteredWorld.InjectComponentChanged(entity, singleType);
                        }
                        else if (componentTypeIds == null && gameObjectChanged)
                        {
                            filteredWorld.InjectGameObjectChanged(entity);
                        }
                        else
                        {
                            filteredWorld.InjectChangedEntity(entity, componentTypeIds, gameObjectChanged);
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
            finally
            {
                --dispatchDepth;
                try
                {
                    FlushIfReady();
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

        /// <summary>刷新缓冲区 If 就绪。</summary>
        private void FlushIfReady()
        {
            if (IsUpdating || batchDepth > 0 || isFlushing)
            {
                return;
            }

            isFlushing = true;
            Exception firstError = null;
            try
            {
                // Callback-generated commands append to these queues and are drained before returning.
                while (commands.Count > 0 || dirtyOrder.Count > 0 || afterChanges.Count > 0)
                {
                    for (int i = 0; i < commands.Count; ++i)
                    {
                        var command = commands[i];
                        commands[i] = null;
                        try
                        {
                            command();
                        }
                        catch (Exception error)
                        {
                            if (firstError == null)
                            {
                                firstError = error;
                            }
                        }
                    }

                    commands.Clear();
                    for (int i = 0; i < dirtyOrder.Count; ++i)
                    {
                        var entity = dirtyOrder[i];
                        if (!pendingChanges.TryGetValue(entity, out var change))
                        {
                            continue;
                        }

                        pendingChanges.Remove(entity);
                        try
                        {
                            if (entity.IsAlive && entity.GUID == change.Guid)
                            {
                                Dispatch(entity, change.ComponentTypes, change.GameObject, change.All, 0, false);
                            }
                        }
                        catch (Exception error)
                        {
                            if (firstError == null)
                            {
                                firstError = error;
                            }
                        }
                        finally
                        {
                            RecycleChange(change);
                        }
                    }

                    dirtyOrder.Clear();
                    // Dispatch hooks may have requested a structural command. Apply it before pooling.
                    if (commands.Count > 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < afterChanges.Count; ++i)
                    {
                        var action = afterChanges[i];
                        afterChanges[i] = null;
                        try
                        {
                            action();
                        }
                        catch (Exception error)
                        {
                            if (firstError == null)
                            {
                                firstError = error;
                            }
                        }
                    }

                    afterChanges.Clear();
                }
            }
            finally
            {
                isFlushing = false;
            }

            if (firstError != null)
            {
                ExceptionDispatchInfo.Capture(firstError).Throw();
            }
        }

        /// <summary>登记世界。</summary>
        public void RegisterWorld(IWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (IsStopping)
            {
                throw new InvalidOperationException("Cannot register a World while its manager is shutting down.");
            }

            if (IsUpdating)
            {
                Defer(() => RegisterWorld(world));
                return;
            }

            if (registeredWorlds.Add(world))
            {
                AllWorlds.Add(world);
            }
        }

        /// <summary>移除世界。</summary>
        public void RemoveWorld(IWorld world)
        {
            if (world == null)
            {
                return;
            }

            if (IsUpdating)
            {
                Defer(() => RemoveWorld(world));
                return;
            }

            if (registeredWorlds.Remove(world))
            {
                AllWorlds.Remove(world);
            }
        }

        /// <summary>响应已初始化。</summary>
        protected override void OnInitialized()
        {
            deleted = false;
            base.OnInitialized();
            LocalWorld = new World();
            LocalWorld.Initialize();
        }

        /// <summary>响应 Delete。</summary>
        protected override void OnDelete()
        {
            deleted = true;
            var worlds = AllWorlds.ToArray();
            Exception firstError = null;
            for (int i = 0; i < worlds.Length; ++i)
            {
                try
                {
                    worlds[i].Destroy();
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }

            AllWorlds.Clear();
            registeredWorlds.Clear();
            commands.Clear();
            // Removed components and replaced native leases may already be waiting for the
            // matching barrier. All worlds are detached now; never lose those release callbacks.
            for (int i = 0; i < afterChanges.Count; ++i)
            {
                var release = afterChanges[i];
                afterChanges[i] = null;
                if (release == null)
                {
                    continue;
                }

                try
                {
                    release();
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }

            afterChanges.Clear();
            pendingChanges.Clear();
            dirtyOrder.Clear();
            changePool.Clear();
            LocalWorld = null;
            updateDepth = 0;
            batchDepth = 0;
            dispatchDepth = 0;
            isFlushing = false;
            base.OnDelete();
            if (firstError != null)
            {
                ExceptionDispatchInfo.Capture(firstError).Throw();
            }
        }

        /// <summary>响应更新。</summary>
        protected override void OnUpdate(float delta)
        {
            if (LocalWorld != null)
            {
                LocalWorld.OnUpdate(delta);
            }
        }
    }
}

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
    public class WorldManager : SingletonBehaviour<WorldManager>
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void RuntimeOnDisableDomainReload() { applicationIsQuitting = false; }

        private sealed class PendingChange
        {
            internal ulong Guid;
            internal bool All;
            internal bool GameObject;
            internal readonly List<int> ComponentTypes = new List<int>();
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
        public World DefaultWorld { get { return LocalWorld; } }
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
        public void Initialize() { }

        public void BeginBatch()
        {
            if (IsStopping) throw new InvalidOperationException("Cannot begin a batch while ECS is shutting down.");
            ++batchDepth;
        }

        /// <summary>Pairs BeginBatch/EndBatch even when the caller throws; scopes can be nested.</summary>
        public IDisposable Batch()
        {
            BeginBatch();
            return new BatchScope(this);
        }

        private sealed class BatchScope : IDisposable
        {
            private WorldManager owner;
            internal BatchScope(WorldManager owner) { this.owner = owner; }
            public void Dispose()
            {
                var manager = owner;
                owner = null;
                if (manager != null) manager.EndBatch();
            }
        }
        public void EndBatch()
        {
            if (batchDepth == 0) throw new InvalidOperationException("EndBatch has no matching BeginBatch.");
            --batchDepth;
            FlushIfReady();
        }
        internal void BeginUpdate() { ++updateDepth; }
        internal void BeginDispatch() { ++dispatchDepth; }
        internal void EndDispatch()
        {
            --dispatchDepth;
            FlushIfReady();
        }
        internal void EndUpdate()
        {
            if (updateDepth == 0) throw new InvalidOperationException("EndUpdate has no matching BeginUpdate.");
            --updateDepth;
            FlushIfReady();
        }

        public void Defer(Action command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (IsUpdating) commands.Add(command);
            else command();
        }

        /// <summary>Release removed components only after every cached system reference is detached.</summary>
        public void AfterChangesApplied(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (IsBatching || pendingChanges.Count > 0) afterChanges.Add(action);
            else action();
        }

        public void InjectEntityToWorlds(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (IsBatching) GetPending(entity).All = true;
            else Dispatch(entity, null, false, true, 0, false);
        }

        public void NotifyComponentChanged(Entity entity, int componentTypeId)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (deleted) return;
            if (IsBatching)
            {
                var change = GetPending(entity);
                if (!change.All && !change.ComponentTypes.Contains(componentTypeId))
                    change.ComponentTypes.Add(componentTypeId);
            }
            else Dispatch(entity, null, false, false, componentTypeId, true);
        }

        public void NotifyGameObjectChanged(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (deleted) return;
            if (IsBatching) GetPending(entity).GameObject = true;
            else Dispatch(entity, null, true, false, 0, false);
        }

        /// <summary>Call after marking dead/clearing data and before recycling any entity or component.</summary>
        public void NotifyEntityDestroyed(Entity entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (pendingChanges.TryGetValue(entity, out var change))
            {
                pendingChanges.Remove(entity);
                RecycleChange(change);
            }
            Dispatch(entity, null, false, true, 0, false);
        }

        private PendingChange GetPending(Entity entity)
        {
            if (pendingChanges.TryGetValue(entity, out var change)) return change;
            change = changePool.Count > 0 ? changePool.Pop() : new PendingChange();
            change.Guid = entity.GUID;
            pendingChanges.Add(entity, change);
            dirtyOrder.Add(entity);
            return change;
        }
        private void RecycleChange(PendingChange change)
        {
            change.Reset();
            changePool.Push(change);
        }

        private void Dispatch(Entity entity, IList<int> componentTypeIds, bool gameObjectChanged,
            bool all, int singleType, bool single)
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
                        if (all || !(world is World filteredWorld)) world.InjectEntity(entity);
                        else if (single) filteredWorld.InjectComponentChanged(entity, singleType);
                        else if (componentTypeIds == null && gameObjectChanged) filteredWorld.InjectGameObjectChanged(entity);
                        else filteredWorld.InjectChangedEntity(entity, componentTypeIds, gameObjectChanged);
                    }
                    catch (Exception error) { if (firstError == null) firstError = error; }
                }
            }
            finally
            {
                --dispatchDepth;
                try { FlushIfReady(); }
                catch (Exception error) { if (firstError == null) firstError = error; }
            }
            if (firstError != null) ExceptionDispatchInfo.Capture(firstError).Throw();
        }

        private void FlushIfReady()
        {
            if (IsUpdating || batchDepth > 0 || isFlushing) return;
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
                        try { command(); }
                        catch (Exception error) { if (firstError == null) firstError = error; }
                    }
                    commands.Clear();
                    for (int i = 0; i < dirtyOrder.Count; ++i)
                    {
                        var entity = dirtyOrder[i];
                        if (!pendingChanges.TryGetValue(entity, out var change)) continue;
                        pendingChanges.Remove(entity);
                        try
                        {
                            if (entity.IsAlive && entity.GUID == change.Guid)
                                Dispatch(entity, change.ComponentTypes, change.GameObject, change.All, 0, false);
                        }
                        catch (Exception error) { if (firstError == null) firstError = error; }
                        finally { RecycleChange(change); }
                    }
                    dirtyOrder.Clear();
                    // Dispatch hooks may have requested a structural command. Apply it before pooling.
                    if (commands.Count > 0) continue;
                    for (int i = 0; i < afterChanges.Count; ++i)
                    {
                        var action = afterChanges[i];
                        afterChanges[i] = null;
                        try { action(); }
                        catch (Exception error) { if (firstError == null) firstError = error; }
                    }
                    afterChanges.Clear();
                }
            }
            finally { isFlushing = false; }
            if (firstError != null) ExceptionDispatchInfo.Capture(firstError).Throw();
        }

        public void RegisterWorld(IWorld world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (IsStopping) throw new InvalidOperationException("Cannot register a World while its manager is shutting down.");
            if (IsUpdating)
            {
                Defer(() => RegisterWorld(world));
                return;
            }
            if (registeredWorlds.Add(world)) AllWorlds.Add(world);
        }
        public void RemoveWorld(IWorld world)
        {
            if (world == null) return;
            if (IsUpdating)
            {
                Defer(() => RemoveWorld(world));
                return;
            }
            if (registeredWorlds.Remove(world)) AllWorlds.Remove(world);
        }

        protected override void OnInitialized()
        {
            deleted = false;
            base.OnInitialized();
            LocalWorld = new World();
            LocalWorld.Initialize();
        }
        protected override void OnDelete()
        {
            deleted = true;
            var worlds = AllWorlds.ToArray();
            Exception firstError = null;
            for (int i = 0; i < worlds.Length; ++i)
            {
                try { worlds[i].Destroy(); }
                catch (Exception error) { if (firstError == null) firstError = error; }
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
                if (release == null) continue;
                try { release(); }
                catch (Exception error) { if (firstError == null) firstError = error; }
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
            if (firstError != null) ExceptionDispatchInfo.Capture(firstError).Throw();
        }
        protected override void OnUpdate(float delta)
        {
            if (LocalWorld != null) LocalWorld.OnUpdate(delta);
        }
    }
}

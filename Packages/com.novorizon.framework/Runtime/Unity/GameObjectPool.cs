using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Game.Framework.Pooling
{
    /// <summary>定义GameObject 对象池 Callbacks的调用契约。</summary>
    public interface IGameObjectPoolCallbacks
    {
        /// <summary>响应 Spawn。</summary>
        void OnSpawn();
        /// <summary>响应 Despawn。</summary>
        void OnDespawn();
    }

    // One pool per prefab. Cache retention is bounded; active instances belong to their callers.
    /// <summary>提供GameObject 对象池的实现。</summary>
    public sealed class GameObjectPool : IDisposable
    {
        /// <summary>定义状态可选的枚举值。</summary>
        private enum State { Spawning, Active, Returning, Cached, Destroyed }
        /// <summary>表示GameObject 对象池中的条目。</summary>
        private sealed class Entry
        {
            internal GameObject Value;
            internal GameObjectPoolMember Member;
            internal IGameObjectPoolCallbacks[] Callbacks;
            internal State State;
            internal int CachedIndex = -1;
        }
        private static readonly HashSet<GameObjectPool> pools = new HashSet<GameObjectPool>();
        private readonly GameObject prefab, root;
        private readonly Vector3 localPosition, localScale;
        private readonly Quaternion localRotation;
        private readonly List<Entry> cached = new List<Entry>();
        private readonly Dictionary<GameObject, Entry> entries = new Dictionary<GameObject, Entry>();
        private readonly HashSet<PrewarmRequest> prewarming = new HashSet<PrewarmRequest>();
        private readonly object prewarmGate = new object();
        private int inactive, active;
        private bool disposed;
        public int MaximumRetained { get; }
        public int CountInactive { get { MainThread(); return inactive; } }
        public int CountActive { get { MainThread(); return active; } }
        public static int PoolCount
        {
            get
            {
                MainThread();
                return pools.Count;
            }
        }

        public static long TotalActiveCount
        {
            get
            {
                MainThread();
                long count = 0;
                foreach (var pool in pools)
                {
                    count += pool.active;
                }

                return count;
            }
        }

        public static long TotalInactiveCount
        {
            get
            {
                MainThread();
                long count = 0;
                foreach (var pool in pools)
                {
                    count += pool.inactive;
                }

                return count;
            }
        }

        /// <summary>使用调用方提供的prefab、maximumRetained、prewarm创建 GameObjectPool。</summary>
        public GameObjectPool(GameObject prefab, int maximumRetained = 64, int prewarm = 0)
        {
            MainThread();
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (maximumRetained < 0) throw new ArgumentOutOfRangeException(nameof(maximumRetained));
            if (prewarm < 0 || prewarm > maximumRetained) throw new ArgumentOutOfRangeException(nameof(prewarm));
            this.prefab = prefab; MaximumRetained = maximumRetained;
            localPosition = prefab.transform.localPosition; localRotation = prefab.transform.localRotation; localScale = prefab.transform.localScale;
            root = new GameObject("Pool: " + prefab.name); root.SetActive(false); Object.DontDestroyOnLoad(root);
            pools.Add(this);
            try { Prewarm(prewarm); } catch { DisposeCore(); throw; }
        }
        /// <summary>校验当前调用位于 Unity 主线程。</summary>
        private static void MainThread()
        { if (!UnityThread.IsMainThread) throw new InvalidOperationException("GameObjectPool must run on the initialized Unity main thread."); }
        /// <summary>调用 nameof 完成就绪。</summary>
        private void Ready() { if (disposed) throw new ObjectDisposedException(nameof(GameObjectPool)); MainThread(); }
        /// <summary>创建 GameObject 对象池。</summary>
        private Entry Create()
        {
            if (prefab == null) throw new InvalidOperationException("Keep the prefab asset alive until the pool is disposed.");
            // An inactive parent prevents the prefab's OnEnable running at its old transform.
            var value = Object.Instantiate(prefab, root.transform, false);
            try
            {
                value.SetActive(false);
                var components = value.GetComponentsInChildren<MonoBehaviour>(true);
                var callbacks = new List<IGameObjectPoolCallbacks>();
                foreach (var component in components) if (component is IGameObjectPoolCallbacks callback) callbacks.Add(callback);
                var member = value.AddComponent<GameObjectPoolMember>();
                var entry = new Entry { Value = value, Member = member, Callbacks = callbacks.ToArray(), State = State.Spawning };
                entries.Add(value, entry); active++; member.Owner = this;
                return entry;
            }
            catch { if (value != null) Object.Destroy(value); throw; }
        }
        /// <summary>生成 GameObject 对象池。</summary>
        public GameObject Spawn(Transform parent = null) => SpawnCore(parent, false, default, default);
        /// <summary>生成 GameObject 对象池。</summary>
        public GameObject Spawn(Vector3 position, Quaternion rotation, Transform parent = null) => SpawnCore(parent, true, position, rotation);
        /// <summary>生成 GameObject 对象池。</summary>
        private GameObject SpawnCore(Transform parent, bool worldPosition, Vector3 position, Quaternion rotation)
        {
            Ready(); Entry entry = null;
            while (cached.Count > 0)
            {
                var candidate = cached[cached.Count - 1]; RemoveCached(candidate);
                if (candidate.State == State.Destroyed) continue;
                if (candidate.Value == null) { DestroyEntry(candidate); continue; }
                entry = candidate; inactive--; active++; entry.State = State.Spawning; break;
            }
            if (entry == null) entry = Create();
            try
            {
                var transform = entry.Value.transform;
                transform.SetParent(parent, false);
                if (parent == null) SceneManager.MoveGameObjectToScene(entry.Value, SceneManager.GetActiveScene());
                transform.localScale = localScale;
                if (worldPosition) transform.SetPositionAndRotation(position, rotation);
                else { transform.localPosition = localPosition; transform.localRotation = localRotation; }
                entry.Value.SetActive(true);
                CheckSpawning(entry);
                foreach (var callback in entry.Callbacks) { callback.OnSpawn(); CheckSpawning(entry); }
                entry.State = State.Active; return entry.Value;
            }
            catch { DestroyEntry(entry); throw; }
        }
        /// <summary>检查 Spawning。</summary>
        private void CheckSpawning(Entry entry)
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameObjectPool));
            if (entry.State == State.Destroyed || entry.Value == null) throw new InvalidOperationException("A spawn callback destroyed the pooled object.");
        }
        /// <summary>移除 GameObject 对象池。</summary>
        public void Despawn(GameObject value)
        {
            Ready();
            if (ReferenceEquals(value, null)) throw new ArgumentNullException(nameof(value));
            if (!entries.TryGetValue(value, out var entry) || entry.State != State.Active)
                throw new InvalidOperationException("Despawn an active instance once, using its owning pool. Do not despawn inside OnSpawn/OnDespawn.");
            entry.State = State.Returning;
            try
            {
                foreach (var callback in entry.Callbacks)
                {
                    callback.OnDespawn();
                    if (disposed || entry.State == State.Destroyed) return;
                }
                if (entry.Value == null) { DestroyEntry(entry); return; }
                entry.Value.SetActive(false);
                if (disposed || entry.State == State.Destroyed) return;
                if (inactive >= MaximumRetained) { DestroyEntry(entry); return; }
                entry.Value.transform.SetParent(root.transform, false);
                active--; inactive++; entry.State = State.Cached; Cache(entry);
            }
            catch { DestroyEntry(entry); throw; }
        }
        /// <summary>预热 GameObject 对象池。</summary>
        public void Prewarm(int count)
        {
            Ready();
            if (count < 0 || count > MaximumRetained) throw new ArgumentOutOfRangeException(nameof(count));
            while (inactive < count)
            {
                var entry = Create(); active--; inactive++; entry.State = State.Cached; Cache(entry);
            }
        }
        // Loading-screen alternative: create at most this many instances per main-thread batch.
        /// <summary>异步预热 GameObject 对象池。</summary>
        public Task PrewarmAsync(int count, int maximumPerFrame = 4, CancellationToken ct = default)
        {
            Ready();
            if (count < 0 || count > MaximumRetained) throw new ArgumentOutOfRangeException(nameof(count));
            if (maximumPerFrame < 1) throw new ArgumentOutOfRangeException(nameof(maximumPerFrame));
            if (ct.IsCancellationRequested) return Task.FromCanceled(ct);
            if (inactive >= count) return Task.CompletedTask;
            var request = new PrewarmRequest(this, count, maximumPerFrame, ct);
            lock (prewarmGate) prewarming.Add(request);
            request.Step(); return request.Task;
        }
        /// <summary>表示GameObject 对象池中的预热请求。</summary>
        private sealed class PrewarmRequest
        {
            private GameObjectPool owner;
            private readonly int count, budget;
            private readonly CancellationToken cancellation;
            private readonly Action step;
            private readonly Action<Exception> rejected;
            private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal Task Task => completion.Task;
            /// <summary>使用调用方提供的owner、count、budget创建 PrewarmRequest。</summary>
            internal PrewarmRequest(GameObjectPool owner, int count, int budget, CancellationToken cancellation)
            { this.owner = owner; this.count = count; this.budget = budget; this.cancellation = cancellation; step = Step; rejected = Reject; }
            /// <summary>调用 Volatile.Read 完成步骤。</summary>
            internal void Step()
            {
                var pool = Volatile.Read(ref owner);
                if (pool == null) return;
                try
                {
                    cancellation.ThrowIfCancellationRequested(); pool.Ready();
                    pool.Prewarm(pool.inactive + Math.Min(budget, count - pool.inactive));
                    if (pool.inactive >= count) { Detach(); completion.TrySetResult(true); }
                    else UnityThread.Post(step, rejected);
                }
                catch (OperationCanceledException) { Cancel(); }
                catch (Exception error) { Detach(); completion.TrySetException(error); }
            }
            /// <summary>分离预热请求。</summary>
            private void Detach()
            {
                var previous = Interlocked.Exchange(ref owner, null);
                if (previous != null) lock (previous.prewarmGate) previous.prewarming.Remove(this);
            }
            /// <summary>拒绝预热请求。</summary>
            private void Reject(Exception error)
            {
                Detach();
                if (error is OperationCanceledException) completion.TrySetCanceled(); else completion.TrySetException(error);
            }
            /// <summary>取消预热请求。</summary>
            internal void Cancel() { Detach(); completion.TrySetCanceled(); }
        }
        /// <summary>裁剪 GameObject 对象池。</summary>
        public void Trim(int retainedCount = 0)
        {
            Ready();
            if (retainedCount < 0) throw new ArgumentOutOfRangeException(nameof(retainedCount));
            while (cached.Count > 0 && inactive > retainedCount) DestroyEntry(cached[cached.Count - 1]);
            if (inactive == 0) cached.Clear();
            cached.TrimExcess(); entries.TrimExcess();
        }
        /// <summary>清空 GameObject 对象池。</summary>
        public void Clear() { Trim(); }
        /// <summary>调用 cached.Add 完成缓存。</summary>
        private void Cache(Entry entry) { entry.CachedIndex = cached.Count; cached.Add(entry); }
        /// <summary>移除已缓存。</summary>
        private void RemoveCached(Entry entry)
        {
            int index = entry.CachedIndex;
            if (index < 0) return;
            var last = cached[cached.Count - 1]; cached[index] = last; last.CachedIndex = index;
            cached.RemoveAt(cached.Count - 1); entry.CachedIndex = -1;
        }
        /// <summary>销毁条目。</summary>
        private void DestroyEntry(Entry entry, bool destroyNative = true)
        {
            if (entry.State == State.Destroyed) return;
            if (entry.State == State.Cached) { inactive--; RemoveCached(entry); } else active--;
            entry.State = State.Destroyed;
            entries.Remove(entry.Value);
            if (entry.Member != null) entry.Member.Owner = null;
            var value = entry.Value; entry.Callbacks = Array.Empty<IGameObjectPoolCallbacks>(); entry.Value = null; entry.Member = null;
            if (destroyNative && value != null) Object.Destroy(value);
        }
        /// <summary>通知已销毁。</summary>
        internal void NotifyDestroyed(GameObject value)
        {
            if (entries.TryGetValue(value, out var entry)) DestroyEntry(entry, false);
        }
        /// <summary>结束GameObject 对象池对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose() { if (disposed) return; MainThread(); DisposeCore(); }
        /// <summary>释放当前对象内部拥有的资源。</summary>
        private void DisposeCore()
        {
            if (disposed) return;
            disposed = true; pools.Remove(this);
            PrewarmRequest[] pendingPrewarm;
            lock (prewarmGate) { pendingPrewarm = new PrewarmRequest[prewarming.Count]; prewarming.CopyTo(pendingPrewarm); }
            foreach (var request in pendingPrewarm) request.Cancel();
            var snapshot = new List<Entry>(entries.Values);
            foreach (var entry in snapshot)
            {
                if (entry.State == State.Active)
                {
                    entry.State = State.Returning;
                    foreach (var callback in entry.Callbacks)
                        try { callback.OnDespawn(); } catch (Exception error) { Logger.Exception(error); }
                }
                DestroyEntry(entry);
            }
            cached.Clear(); cached.TrimExcess(); entries.Clear(); entries.TrimExcess();
            if (root != null) Object.Destroy(root);
        }
        /// <summary>批量释放 GameObject 对象池。</summary>
        internal static void DisposeAll()
        {
            foreach (var pool in new List<GameObjectPool>(pools)) pool.DisposeCore();
        }
    }
}

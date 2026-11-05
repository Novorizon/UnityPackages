#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UI
{
    /// <summary>Owns view instances and their prefab leases. Call from Unity's main thread.</summary>
    public sealed class UIInstanceFactory : IDisposable
    {
        sealed class Entry
        {
            public readonly string Path;
            public readonly GameObject Prefab;
            public readonly UIView View;
            public readonly GameObject Instance;
            public bool LeaseReleased;
            public UIDestructionObserver.Ticket? Destruction;
            public UIKind Kind;
            public UILayer Layer;
            public bool Active;
            public bool AllowMultiple;
            public bool CacheOnClose;
            public bool Destroying;
            public UIBlocker? Blocker;

            public Entry(string path, GameObject prefab, UIView view)
            {
                Path = path;
                Prefab = prefab;
                View = view;
                Instance = view.gameObject;
            }
        }

        readonly IUIAssetLoader loader;
        readonly Dictionary<UILayer, Transform> layerRoots;
        readonly Dictionary<string, Entry> singletonActive = new();
        readonly Dictionary<string, List<Entry>> cached = new();
        readonly Dictionary<UIView, Entry> instances = new();
        readonly TaskCompletionSource<bool> disposedCompletion = new();
        int maxCachedInstances;
        int maxCachedPerPath;
        int nextId;
        int cachedCount;
        GameObject? staging;

        public UIInstanceFactory(IUIAssetLoader loader, Dictionary<UILayer, Transform> layerRoots, int startId,
            int maxCachedInstances = 32, int maxCachedPerPath = 2)
        {
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
            this.layerRoots = layerRoots ?? throw new ArgumentNullException(nameof(layerRoots));
            if (startId <= 0) throw new ArgumentOutOfRangeException(nameof(startId));
            if (maxCachedInstances < 0) throw new ArgumentOutOfRangeException(nameof(maxCachedInstances));
            if (maxCachedPerPath < 0) throw new ArgumentOutOfRangeException(nameof(maxCachedPerPath));
            nextId = startId;
            this.maxCachedInstances = maxCachedInstances;
            this.maxCachedPerPath = maxCachedPerPath;
        }

        public int NextId => nextId;
        public int CachedCount => cachedCount;
        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (Entry entry in instances.Values)
                    if (entry.Active && !entry.Destroying && entry.View != null && !entry.View.IsDestroyed) count++;
                return count;
            }
        }
        public bool IsDisposed { get; private set; }
        public event Action? Disposed;

        public async Task<UIHandle> OpenAsync(UIKind kind, UILayer layer, string prefabPath, object? args,
            bool allowMultiple, bool cacheOnClose, Func<Transform, UIBlocker?>? blockerFactory,
            CancellationToken cancellationToken = default)
        {
            if (IsDisposed || cancellationToken.IsCancellationRequested) return default;
            if (string.IsNullOrWhiteSpace(prefabPath)) throw new ArgumentException("A prefab path is required.", nameof(prefabPath));
            if (!layerRoots.TryGetValue(layer, out Transform? parent) || parent == null)
                throw new ArgumentException($"UI layer is unavailable: {layer}", nameof(layer));
            RequireActiveParent(parent, layer);

            if (!allowMultiple && TryGetSingleton(prefabPath, out Entry? existing))
                return Reopen(existing!, args);

            Entry? entry = TakeCached(prefabPath);
            if (entry == null)
            {
                Task<GameObject?> loadTask = loader.LoadPrefabAsync(prefabPath);
                if (!loadTask.IsCompleted)
                {
                    var cancelled = new TaskCompletionSource<bool>();
                    using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                    {
                        if (await Task.WhenAny(loadTask, disposedCompletion.Task, cancelled.Task) != loadTask)
                        {
                            _ = ReleaseLateLoadAsync(loadTask);
                            return default;
                        }
                    }
                }

                GameObject? prefab = await loadTask;
                if (prefab == null)
                {
                    if (!IsDisposed) Debug.LogError($"[UI] Prefab not found: {prefabPath}");
                    return default;
                }
                if (IsDisposed || cancellationToken.IsCancellationRequested)
                {
                    loader.ReleasePrefab(prefab);
                    return default;
                }
                try { RequireActiveParent(parent, layer); }
                catch { loader.ReleasePrefab(prefab); throw; }
                if (prefab.GetComponent<UIView>() == null)
                {
                    Debug.LogError($"[UI] Prefab must have a UIView-derived component: {prefabPath}");
                    loader.ReleasePrefab(prefab);
                    return default;
                }

                // A second singleton request may have completed while this one was loading.
                if (!allowMultiple && TryGetSingleton(prefabPath, out existing))
                {
                    loader.ReleasePrefab(prefab);
                    return Reopen(existing!, args);
                }

                GameObject? go = null;
                bool unregisteredDestruction = false;
                try
                {
                    // Native Instantiate can run OnEnable before returning for active prefabs.
                    // Stage inactive, register ownership, then activate under the final parent.
                    go = Object.Instantiate(prefab, Staging(), false);
                    go.SetActive(false);
                    go.name = prefab.name;
                    UIView? view = go.GetComponent<UIView>();
                    if (view == null)
                    {
                        Debug.LogError($"[UI] Prefab must have a UIView-derived component: {prefabPath}");
                        unregisteredDestruction = true;
                        DestroyUnregistered(go, prefab);
                        return default;
                    }
                    entry = new Entry(prefabPath, prefab, view);
                    instances.Add(view, entry);
                    view.Destroyed += OnViewDestroyed;
                    go.transform.SetParent(parent, false);
                    if (Unavailable(entry)) { DestroyEntry(entry); return default; }
                    RequireActiveParent(parent, layer);
                    view.InternalSetVisible(true);
                    if (Unavailable(entry)) { DestroyEntry(entry); return default; }
                    view.InternalOnCreate();
                }
                catch
                {
                    if (entry != null) DestroyEntry(entry);
                    else
                    {
                        if (!unregisteredDestruction)
                        {
                            if (go != null) DestroyUnregistered(go, prefab);
                            else loader.ReleasePrefab(prefab);
                        }
                    }
                    throw;
                }
            }

            if (Unavailable(entry)) return default;
            entry.Kind = kind;
            entry.Layer = layer;
            entry.AllowMultiple = allowMultiple;
            entry.CacheOnClose = cacheOnClose;
            entry.Active = true;
            int openingId = entry.View.InstanceId = nextId++;
            try
            {
                entry.View.transform.SetParent(parent, false);
                if (!OwnsOpening(entry, openingId)) return default;
                entry.View.transform.SetAsLastSibling();
                if (!OwnsOpening(entry, openingId)) return default;
                if (!allowMultiple) singletonActive[prefabPath] = entry;

                UIBlocker? blocker = blockerFactory?.Invoke(parent);
                if (!OwnsOpening(entry, openingId))
                {
                    DestroyBlocker(blocker);
                    return default;
                }
                entry.Blocker = blocker;
                if (entry.Blocker != null)
                    entry.Blocker.transform.SetSiblingIndex(entry.View.transform.GetSiblingIndex());
                if (!OwnsOpening(entry, openingId)) return default;
                entry.View.InternalSetVisible(true);
                if (!OwnsOpening(entry, openingId)) return default;
                if (entry.View is UIToast toast) toast.Prepare();
                entry.View.InternalOnOpen(args);
                if (cancellationToken.IsCancellationRequested && OwnsOpening(entry, openingId))
                {
                    CloseApproved(Handle(entry), false, true, UICloseReason.Cancelled);
                    return default;
                }
                return !OwnsOpening(entry, openingId) || !entry.View.IsOpen ? default : Handle(entry);
            }
            catch
            {
                if (OwnsOpening(entry, openingId)) DestroyEntry(entry);
                throw;
            }
        }

        UIHandle Reopen(Entry entry, object? args)
        {
            int openingId = entry.View.InstanceId;
            try
            {
                entry.View.InternalSetVisible(true);
                if (!OwnsOpening(entry, openingId)) return default;
                entry.View.InternalOnOpen(args);
                return !OwnsOpening(entry, openingId) || !entry.View.IsOpen ? default : Handle(entry);
            }
            catch
            {
                if (OwnsOpening(entry, openingId)) DestroyEntry(entry);
                throw;
            }
        }

        static void RequireActiveParent(Transform parent, UILayer layer)
        {
            if (parent == null || !parent.gameObject.activeInHierarchy)
                throw new InvalidOperationException($"Open UI only under an available, active layer: {layer}");
        }

        Transform Staging()
        {
            if (staging == null)
            {
                staging = new GameObject("UI Factory Staging");
                staging.SetActive(false);
                Object.DontDestroyOnLoad(staging);
            }
            return staging.transform;
        }

        bool OwnsOpening(Entry entry, int id) => !Unavailable(entry) && entry.Active && entry.View.InstanceId == id;

        bool Unavailable(Entry entry) => IsDisposed || entry.Destroying || entry.View == null || entry.View.IsDestroyed;

        bool TryGetSingleton(string path, out Entry? entry)
        {
            if (singletonActive.TryGetValue(path, out entry) && entry.Active && !entry.Destroying && entry.View != null && !entry.View.IsDestroyed)
                return true;
            singletonActive.Remove(path);
            entry = null;
            return false;
        }

        Entry? TakeCached(string path)
        {
            if (!cached.TryGetValue(path, out List<Entry>? list)) return null;
            while (list.Count > 0)
            {
                int index = list.Count - 1;
                Entry entry = list[index];
                list.RemoveAt(index);
                cachedCount--;
                if (list.Count == 0) cached.Remove(path);
                if (!entry.Destroying && entry.View != null && !entry.View.IsDestroyed) return entry;
            }
            return null;
        }

        static UIHandle Handle(Entry entry) => new UIHandle(entry.View.InstanceId, entry.Path, entry.Kind, entry.Layer, entry.View);

        public bool CanClose(UIHandle handle, UICloseReason reason = UICloseReason.Programmatic)
        {
            if (!TryGetActive(handle, out Entry? entry)) return false;
            bool allowed = entry!.View.InternalCanClose(reason);
            return allowed && TryGetActive(handle, out _);
        }

        public async Task<bool> CanCloseAsync(UIHandle handle, UICloseReason reason = UICloseReason.Programmatic,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetActive(handle, out Entry? entry)) return false;
            bool allowed;
            try { allowed = await entry!.View.InternalCanCloseAsync(reason, cancellationToken); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !TryGetActive(handle, out _))
            { return false; }
            cancellationToken.ThrowIfCancellationRequested();
            return allowed && TryGetActive(handle, out _);
        }

        public bool TryClose(UIHandle handle, bool destroy, bool cacheOnClose,
            UICloseReason reason = UICloseReason.Programmatic)
        {
            if (!CanClose(handle, reason)) return false;
            CloseApproved(handle, destroy, cacheOnClose, reason);
            return true;
        }

        public async Task<bool> CloseAsync(UIHandle handle, bool destroy, bool cacheOnClose,
            UICloseReason reason = UICloseReason.Programmatic, CancellationToken cancellationToken = default)
        {
            if (!await CanCloseAsync(handle, reason, cancellationToken)) return false;
            CloseApproved(handle, destroy, cacheOnClose, reason);
            return true;
        }

        public void Close(UIHandle handle, bool destroy, bool cacheOnClose)
            => TryClose(handle, destroy, cacheOnClose);

        bool TryGetActive(UIHandle handle, out Entry? entry)
        {
            entry = null;
            return !IsDisposed && handle.IsValid && handle.View != null &&
                instances.TryGetValue(handle.View, out entry) && entry.Active && !entry.Destroying;
        }

        internal void CloseApproved(UIHandle handle, bool destroy, bool cacheOnClose, UICloseReason reason)
        {
            if (!handle.IsValid || handle.View == null || !instances.TryGetValue(handle.View, out Entry? entry) ||
                !entry.Active || entry.Destroying) return;

            entry.Active = false;
            RemoveSingleton(entry);
            DestroyBlocker(entry);
            try
            {
                entry.View.InternalOnClose(reason);
            }
            catch
            {
                DestroyEntry(entry);
                throw;
            }

            if (entry.Destroying || entry.View == null) return;
            if (!destroy && !IsDisposed && cacheOnClose && entry.CacheOnClose && cachedCount < maxCachedInstances && maxCachedPerPath > 0)
            {
                if (!cached.TryGetValue(entry.Path, out List<Entry>? list))
                {
                    list = new List<Entry>();
                    cached.Add(entry.Path, list);
                }
                if (list.Count < maxCachedPerPath)
                {
                    // Publish before OnDisable: user callbacks may clear/dispose/reopen the cache.
                    list.Add(entry);
                    cachedCount++;
                    entry.View.InternalSetVisible(false);
                    return;
                }
            }
            DestroyEntry(entry);
        }

        internal void SetCacheLimits(int total, int perPath)
        {
            if (total < maxCachedInstances || perPath < maxCachedPerPath) ClearCache();
            maxCachedInstances = total;
            maxCachedPerPath = perPath;
        }

        public void ClearCache()
        {
            var snapshot = new List<Entry>();
            foreach (List<Entry> list in cached.Values) snapshot.AddRange(list);
            cached.Clear();
            cachedCount = 0;
            foreach (Entry entry in snapshot) DestroyEntry(entry);
        }

        public void Dispose() => Dispose(UICloseReason.Disposed);

        internal void Dispose(UICloseReason reason)
        {
            if (IsDisposed) return;
            IsDisposed = true;
            disposedCompletion.TrySetResult(true);
            Action? callbacks = Disposed;
            Disposed = null;
            if (callbacks != null)
                foreach (Action callback in callbacks.GetInvocationList())
                    try { callback(); } catch (Exception exception) { Debug.LogException(exception); }
            ClearCache();
            foreach (Entry entry in new List<Entry>(instances.Values)) DestroyEntry(entry, reason);
            singletonActive.Clear();
            if (staging != null) { Object.Destroy(staging); staging = null; }
        }

        void RemoveSingleton(Entry entry)
        {
            if (singletonActive.TryGetValue(entry.Path, out Entry? active) && ReferenceEquals(active, entry))
                singletonActive.Remove(entry.Path);
        }

        void DestroyUnregistered(GameObject instance, GameObject prefab)
        {
            UIDestructionObserver.Observe(instance, () =>
            {
                try { loader.ReleasePrefab(prefab); }
                catch (Exception exception) { Debug.LogException(exception); }
            });
            if (instance == null) return;
            instance.SetActive(false);
            Object.Destroy(instance);
        }

        void DestroyEntry(Entry entry, UICloseReason reason = UICloseReason.Destroyed)
        {
            if (entry.Destroying || entry.LeaseReleased) return;
            entry.Destroying = true;
            entry.Active = false;
            RemoveSingleton(entry);
            DestroyBlocker(entry);
            // Register before callbacks/Destroy: never-active views may not receive OnDestroy.
            entry.Destruction = UIDestructionObserver.Observe(entry.Instance, () => ReleaseEntry(entry));
            if (entry.View != null && entry.View.IsDestroyed) return;
            if (entry.View != null)
            {
                try { entry.View.InternalOnClose(reason); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            if (entry.LeaseReleased || entry.Instance == null) return;
            // Keep the prefab lease until native destruction, never merely until Destroy is requested.
            entry.Instance.SetActive(false);
            Object.Destroy(entry.Instance);
        }

        static void DestroyBlocker(Entry entry)
        {
            UIBlocker? blocker = entry.Blocker;
            entry.Blocker = null;
            DestroyBlocker(blocker);
        }

        static void DestroyBlocker(UIBlocker? blocker)
        {
            if (blocker == null) return;
            blocker.gameObject.SetActive(false);
            Object.Destroy(blocker.gameObject);
        }

        void OnViewDestroyed(UIView view)
        {
            if (instances.TryGetValue(view, out Entry? entry)) ReleaseEntry(entry);
        }

        void ReleaseEntry(Entry entry)
        {
            if (entry.LeaseReleased) return;
            entry.LeaseReleased = true;
            UIDestructionObserver.Cancel(entry.Destruction);
            entry.Destruction = null;
            instances.Remove(entry.View);
            entry.View.Destroyed -= OnViewDestroyed;
            entry.Active = false;
            RemoveSingleton(entry);
            DestroyBlocker(entry);
            if (cached.TryGetValue(entry.Path, out List<Entry>? list) && list.Remove(entry))
            {
                cachedCount--;
                if (list.Count == 0) cached.Remove(entry.Path);
            }
            try { loader.ReleasePrefab(entry.Prefab); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        async Task ReleaseLateLoadAsync(Task<GameObject?> task)
        {
            try
            {
                GameObject? prefab = await task;
                if (prefab != null) loader.ReleasePrefab(prefab);
            }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}

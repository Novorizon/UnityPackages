#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UI
{
    public sealed class PanelOptions
    {
        public bool AllowMultiple { get; set; } = false;
        public bool CacheOnClose { get; set; } = true;
        public UIDuplicatePolicy DuplicatePolicy { get; set; } = UIDuplicatePolicy.Refresh;
        public string? GroupId { get; set; }
    }

    public sealed class PanelManager
    {
        readonly UIInstanceFactory factory;
        readonly Dictionary<string, UIHandle> singletonPanelsByPath = new();
        readonly Dictionary<string, List<UIHandle>> groupStacks = new();
        readonly List<PanelEntry> opened = new();
        readonly SemaphoreSlim navigation = new(1, 1);
        readonly CancellationTokenSource lifetime = new();
        public PanelManager(UIInstanceFactory factory)
        {
            this.factory = factory;
            factory.Disposed += OnFactoryDisposed;
            if (factory.IsDisposed)
            {
                OnFactoryDisposed();
            }
        }

        /// <summary>判断是否已显示。</summary>
        public bool IsShown<TView>(UIKey<TView> key) where TView : UIView
        {
            if (factory.IsDisposed)
            {
                return false;
            }

            for (int i = opened.Count - 1; i >= 0; i--)
            {
                UIHandle handle = opened[i].Handle;
                if (handle.PrefabPath == key.Path && handle.IsValid && handle.View is TView view && view.gameObject.activeSelf)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>异步显示面板管理器。</summary>
        public Task<UIHandle<TView>> ShowAsync<TView>(UIKey<TView> key, object? args = null, PanelOptions? options = null, CancellationToken cancellationToken = default) where TView : UIView
        {
            PanelOptions opt = options ?? new PanelOptions();
            return ShowCoreAsync(key, args, opt.AllowMultiple, opt.CacheOnClose, opt.GroupId, false, cancellationToken, opt.DuplicatePolicy);
        }

        /// <summary>隐藏面板管理器。</summary>
        public bool Hide<TView>(UIKey<TView> key, UICloseReason reason = UICloseReason.Programmatic) where TView : UIView
        {
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                if (opened[i].Handle.PrefabPath == key.Path && opened[i].Handle.IsValid && opened[i].Handle.View is TView)
                {
                    return Hide(opened[i].Handle, reason);
                }
            }

            return false;
        }

        public bool Hide(UIHandle handle, UICloseReason reason = UICloseReason.Programmatic)
        {
            if (factory.IsDisposed || !handle.IsValid)
            {
                return false;
            }

            int index = FindIndex(handle);
            if (index < 0)
            {
                return false;
            }

            if (!factory.CanClose(handle, reason))
            {
                return false;
            }

            index = FindIndex(handle);
            if (index < 0 || !handle.IsValid)
            {
                return false;
            }

            RemovePanel(index, true, reason);
            return true;
        }

        /// <summary>异步隐藏面板管理器。</summary>
        public Task<bool> HideAsync<TView>(UIKey<TView> key, UICloseReason reason = UICloseReason.Programmatic, CancellationToken cancellationToken = default) where TView : UIView
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                if (opened[i].Handle.PrefabPath == key.Path && opened[i].Handle.IsValid && opened[i].Handle.View is TView)
                {
                    return HideAsync(opened[i].Handle, reason, cancellationToken);
                }
            }

            return Task.FromResult(false);
        }

        public Task<bool> HideAsync(UIHandle handle, UICloseReason reason = UICloseReason.Programmatic, CancellationToken cancellationToken = default) => HideCoreAsync(handle, reason, null, false, cancellationToken);
        async Task<bool> HideCoreAsync(UIHandle handle, UICloseReason reason, string? requiredGroup, bool requireBackTop, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (factory.IsDisposed || !handle.IsValid || FindIndex(handle) < 0)
            {
                return false;
            }

            bool approved = await factory.CanCloseAsync(handle, reason, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            int index = FindIndex(handle);
            if (!approved || factory.IsDisposed || !handle.IsValid || index < 0)
            {
                return false;
            }

            if (requiredGroup != null)
            {
                if (!groupStacks.TryGetValue(requiredGroup, out List<UIHandle>? group))
                {
                    return false;
                }

                PruneGroup(group);
                if (group.Count == 0 || !Same(group[group.Count - 1], handle))
                {
                    return false;
                }
            }

            if (requireBackTop)
            {
                UIHandle? top = PeekBackClosablePanel();
                if (!top.HasValue || !Same(top.Value, handle))
                {
                    return false;
                }
            }

            RemovePanel(index, true, reason);
            return true;
        }

        static bool Same(UIHandle first, UIHandle second) => first.Id == second.Id && ReferenceEquals(first.View, second.View);
        void RemovePanel(int index, bool close, UICloseReason reason = UICloseReason.Destroyed)
        {
            PanelEntry entry = opened[index];
            UIHandle handle = entry.Handle;
            opened.RemoveAt(index);
            Unsubscribe(handle);
            if (singletonPanelsByPath.TryGetValue(handle.PrefabPath, out UIHandle singleton) && singleton.Id == handle.Id)
            {
                singletonPanelsByPath.Remove(handle.PrefabPath);
            }

            bool restoreGroup = false;
            if (entry.GroupId != null && groupStacks.TryGetValue(entry.GroupId, out List<UIHandle>? stack))
            {
                for (int i = stack.Count - 1; i >= 0; i--)
                {
                    if (stack[i].Id == handle.Id)
                    {
                        restoreGroup = i == stack.Count - 1;
                        stack.RemoveAt(i);
                        break;
                    }
                }
            }

            try
            {
                if (close)
                {
                    factory.CloseApproved(handle, false, true, reason);
                }
            }
            finally
            {
                // Cleanup callbacks may fail or push a replacement. Restore the current top.
                if (restoreGroup && entry.GroupId != null)
                {
                    ShowGroupTop(entry.GroupId);
                }
            }
        }

        /// <summary>异步切换面板管理器。</summary>
        public Task<UIHandle<TView>> ToggleAsync<TView>(UIKey<TView> key, object? args = null, CancellationToken cancellationToken = default) where TView : UIView
        {
            return ShowCoreAsync(key, args, false, true, null, true, cancellationToken);
        }

        /// <summary>异步压入 In 分组。</summary>
        public Task<UIHandle<TView>> PushInGroupAsync<TView>(string groupId, UIKey<TView> key, object? args = null, PanelOptions? options = null, CancellationToken cancellationToken = default) where TView : UIView
        {
            if (groupId == null)
            {
                throw new System.ArgumentNullException(nameof(groupId));
            }

            PanelOptions opt = options ?? new PanelOptions();
            return ShowCoreAsync(key, args, true, opt.CacheOnClose, groupId, false, cancellationToken);
        }

        public bool PopGroup(string groupId, UICloseReason reason = UICloseReason.Back)
        {
            if (factory.IsDisposed || !groupStacks.TryGetValue(groupId, out List<UIHandle>? stack))
            {
                return false;
            }

            PruneGroup(stack);
            return stack.Count > 0 && Hide(stack[stack.Count - 1], reason);
        }

        public Task<bool> PopGroupAsync(string groupId, UICloseReason reason = UICloseReason.Back, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (factory.IsDisposed || !groupStacks.TryGetValue(groupId, out List<UIHandle>? stack))
            {
                return Task.FromResult(false);
            }

            PruneGroup(stack);
            return stack.Count == 0 ? Task.FromResult(false) : HideCoreAsync(stack[stack.Count - 1], reason, groupId, false, cancellationToken);
        }

        public UIHandle? PeekBackClosablePanel()
        {
            if (factory.IsDisposed)
            {
                return null;
            }

            for (int i = opened.Count - 1; i >= 0; i--)
            {
                UIHandle handle = opened[i].Handle;
                if (handle.IsValid && handle.View is UIPanel panel && panel.HideOnBack && panel.gameObject.activeInHierarchy)
                {
                    return handle;
                }
            }

            return null;
        }

        public bool HideAnyBackClosablePanel()
        {
            UIHandle? top = PeekBackClosablePanel();
            return top.HasValue && Hide(top.Value, UICloseReason.Back);
        }

        public Task<bool> HideAnyBackClosablePanelAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UIHandle? top = PeekBackClosablePanel();
            return top.HasValue ? HideCoreAsync(top.Value, UICloseReason.Back, null, true, cancellationToken) : Task.FromResult(false);
        }

        /// <summary>异步显示面板管理器。</summary>
        async Task<UIHandle<TView>> ShowCoreAsync<TView>(UIKey<TView> key, object? args, bool allowMultiple, bool cacheOnClose, string? groupId, bool toggle, CancellationToken cancellationToken, UIDuplicatePolicy duplicatePolicy = UIDuplicatePolicy.Refresh) where TView : UIView
        {
            string prefabPath = key.Path;
            cancellationToken.ThrowIfCancellationRequested();
            if (!System.Enum.IsDefined(typeof(UIDuplicatePolicy), duplicatePolicy))
            {
                throw new System.ArgumentOutOfRangeException(nameof(duplicatePolicy));
            }

            if (factory.IsDisposed)
            {
                return default;
            }

            using CancellationTokenSource? linked = cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken) : null;
            CancellationToken operationToken = linked?.Token ?? lifetime.Token;
            bool acquired = false;
            try
            {
                await navigation.WaitAsync(operationToken);
                acquired = true;
                cancellationToken.ThrowIfCancellationRequested();
                if (factory.IsDisposed)
                {
                    return default;
                }

                if (toggle && IsShown(key))
                {
                    await HideAsync(key, UICloseReason.Programmatic, operationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return default;
                }

                if (groupId == null && !allowMultiple && singletonPanelsByPath.TryGetValue(prefabPath, out UIHandle existing) && existing.IsValid && existing.View != null)
                {
                    if (!UIInstanceFactory.MatchesView(existing.View, prefabPath, UIKey<TView>.MatchesView))
                    {
                        return default;
                    }

                    if (duplicatePolicy == UIDuplicatePolicy.Reject)
                    {
                        throw new System.InvalidOperationException("Panel is already open: " + prefabPath);
                    }

                    existing.View.transform.SetAsLastSibling();
                    existing.View.InternalSetVisible(true);
                    if (factory.IsDisposed || !existing.IsValid)
                    {
                        return default;
                    }

                    if (duplicatePolicy == UIDuplicatePolicy.Refresh)
                    {
                        existing.View.InternalOnOpen(args);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    return !factory.IsDisposed && existing.IsValid ? new UIHandle<TView>(existing) : default;
                }

                UIHandle handle = await factory.OpenAsync(UIKind.Panel, UILayer.Panel, prefabPath, args, groupId != null || allowMultiple, cacheOnClose, null, operationToken, expectedView: UIKey<TView>.MatchesView);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (handle.IsValid)
                    {
                        factory.CloseApproved(handle, false, true, UICloseReason.Cancelled);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (factory.IsDisposed || !handle.IsValid)
                {
                    return default;
                }

                UIHandle? previous = null;
                if (groupId != null)
                {
                    if (!groupStacks.TryGetValue(groupId, out List<UIHandle>? stack))
                    {
                        stack = new List<UIHandle>();
                        groupStacks.Add(groupId, stack);
                    }

                    PruneGroup(stack);
                    if (stack.Count > 0)
                    {
                        previous = stack[stack.Count - 1];
                    }

                    stack.Add(handle);
                }
                else if (!allowMultiple)
                {
                    singletonPanelsByPath[prefabPath] = handle;
                }

                opened.Add(new PanelEntry(handle, groupId));
                handle.View!.Destroyed += OnPanelDestroyed;
                if (previous.HasValue && previous.Value.IsValid && previous.Value.View != null)
                {
                    previous.Value.View.InternalSetVisible(false);
                }

                return !factory.IsDisposed && handle.IsValid ? new UIHandle<TView>(handle) : default;
            }
            catch (System.OperationCanceledException) when (!cancellationToken.IsCancellationRequested && lifetime.IsCancellationRequested)
            {
                return default;
            }
            finally
            {
                if (acquired)
                {
                    navigation.Release();
                }
            }
        }

        int FindIndex(UIHandle handle)
        {
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                if (opened[i].Handle.Id == handle.Id && opened[i].Handle.View == handle.View)
                {
                    return i;
                }
            }

            return -1;
        }

        void ShowGroupTop(string groupId)
        {
            if (factory.IsDisposed || !groupStacks.TryGetValue(groupId, out List<UIHandle>? stack))
            {
                return;
            }

            PruneGroup(stack);
            if (stack.Count == 0)
            {
                groupStacks.Remove(groupId);
            }
            else if (stack[stack.Count - 1].View != null)
            {
                stack[stack.Count - 1].View!.InternalSetVisible(true);
            }
        }

        static void PruneGroup(List<UIHandle> stack)
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                if (!stack[i].IsValid)
                {
                    stack.RemoveAt(i);
                }
            }
        }

        void OnFactoryDisposed()
        {
            factory.Disposed -= OnFactoryDisposed;
            foreach (PanelEntry entry in opened)
            {
                Unsubscribe(entry.Handle);
            }

            opened.Clear();
            singletonPanelsByPath.Clear();
            groupStacks.Clear();
            lifetime.Cancel();
        }

        void Unsubscribe(UIHandle handle)
        {
            if (!ReferenceEquals(handle.View, null))
            {
                handle.View.Destroyed -= OnPanelDestroyed;
            }
        }

        void OnPanelDestroyed(UIView view)
        {
            view.Destroyed -= OnPanelDestroyed;
            if (factory.IsDisposed)
            {
                return;
            }

            for (int i = opened.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(opened[i].Handle.View, view))
                {
                    RemovePanel(i, false);
                    return;
                }
            }
        }

        readonly struct PanelEntry
        {
            public readonly UIHandle Handle;
            public readonly string? GroupId;
            public PanelEntry(UIHandle handle, string? groupId)
            {
                Handle = handle;
                GroupId = groupId;
            }
        }
    }
}

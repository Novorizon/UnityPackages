#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UI
{
    /// <summary>面板对应的选项。定义可由调用方设置的选项。</summary>
    public sealed class PanelOptions
    {
        public bool AllowMultiple { get; set; } = false;
        public bool CacheOnClose { get; set; } = true;
        public UIDuplicatePolicy DuplicatePolicy { get; set; } = UIDuplicatePolicy.Refresh;
        public string? GroupId { get; set; }
    }

    /// <summary>面板对应的管理器。管理对应模块的状态和操作入口。</summary>
    public sealed class PanelManager
    {
        readonly UIInstanceFactory factory;
        readonly Dictionary<string, UIHandle> singletonPanelsByPath = new();
        readonly Dictionary<string, List<UIHandle>> groupStacks = new();
        readonly List<PanelEntry> opened = new();
        readonly SemaphoreSlim navigation = new(1, 1);
        readonly CancellationTokenSource lifetime = new();
        /// <summary>使用调用方提供的factory创建 PanelManager。</summary>
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
        public bool IsShown(string prefabPath)
        {
            if (factory.IsDisposed)
            {
                return false;
            }

            for (int i = opened.Count - 1; i >= 0; i--)
            {
                UIHandle handle = opened[i].Handle;
                if (handle.PrefabPath == prefabPath && handle.IsValid && handle.View != null && handle.View.gameObject.activeSelf)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>异步显示面板管理器。</summary>
        public Task<UIHandle> ShowAsync(string prefabPath, object? args = null, PanelOptions? options = null, CancellationToken cancellationToken = default)
        {
            PanelOptions opt = options ?? new PanelOptions();
            return ShowCoreAsync(prefabPath, args, opt.AllowMultiple, opt.CacheOnClose, opt.GroupId, false, cancellationToken, opt.DuplicatePolicy);
        }

        /// <summary>隐藏面板管理器。</summary>
        public bool Hide(string prefabPath, UICloseReason reason = UICloseReason.Programmatic)
        {
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                if (opened[i].Handle.PrefabPath == prefabPath && opened[i].Handle.IsValid)
                {
                    return Hide(opened[i].Handle, reason);
                }
            }

            return false;
        }

        /// <summary>隐藏面板管理器。</summary>
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
        public Task<bool> HideAsync(string prefabPath, UICloseReason reason = UICloseReason.Programmatic, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                if (opened[i].Handle.PrefabPath == prefabPath && opened[i].Handle.IsValid)
                {
                    return HideAsync(opened[i].Handle, reason, cancellationToken);
                }
            }

            return Task.FromResult(false);
        }

        /// <summary>异步隐藏面板管理器。</summary>
        public Task<bool> HideAsync(UIHandle handle, UICloseReason reason = UICloseReason.Programmatic, CancellationToken cancellationToken = default) => HideCoreAsync(handle, reason, null, false, cancellationToken);
        /// <summary>异步隐藏面板管理器。</summary>
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

        /// <summary>比较是否相同面板管理器。</summary>
        static bool Same(UIHandle first, UIHandle second) => first.Id == second.Id && ReferenceEquals(first.View, second.View);
        /// <summary>移除面板。</summary>
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
        public Task<UIHandle> ToggleAsync(string prefabPath, object? args = null, CancellationToken cancellationToken = default)
        {
            return ShowCoreAsync(prefabPath, args, false, true, null, true, cancellationToken);
        }

        /// <summary>异步压入 In 分组。</summary>
        public Task<UIHandle> PushInGroupAsync(string groupId, string prefabPath, object? args = null, PanelOptions? options = null, CancellationToken cancellationToken = default)
        {
            if (groupId == null)
            {
                throw new System.ArgumentNullException(nameof(groupId));
            }

            PanelOptions opt = options ?? new PanelOptions();
            return ShowCoreAsync(prefabPath, args, true, opt.CacheOnClose, groupId, false, cancellationToken);
        }

        /// <summary>弹出分组。</summary>
        public bool PopGroup(string groupId, UICloseReason reason = UICloseReason.Back)
        {
            if (factory.IsDisposed || !groupStacks.TryGetValue(groupId, out List<UIHandle>? stack))
            {
                return false;
            }

            PruneGroup(stack);
            return stack.Count > 0 && Hide(stack[stack.Count - 1], reason);
        }

        /// <summary>异步弹出分组。</summary>
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

        /// <summary>查看返回可关闭面板。</summary>
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

        /// <summary>隐藏任意返回可关闭面板。</summary>
        public bool HideAnyBackClosablePanel()
        {
            UIHandle? top = PeekBackClosablePanel();
            return top.HasValue && Hide(top.Value, UICloseReason.Back);
        }

        /// <summary>异步隐藏任意返回可关闭面板。</summary>
        public Task<bool> HideAnyBackClosablePanelAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UIHandle? top = PeekBackClosablePanel();
            return top.HasValue ? HideCoreAsync(top.Value, UICloseReason.Back, null, true, cancellationToken) : Task.FromResult(false);
        }

        /// <summary>异步显示面板管理器。</summary>
        async Task<UIHandle> ShowCoreAsync(string prefabPath, object? args, bool allowMultiple, bool cacheOnClose, string? groupId, bool toggle, CancellationToken cancellationToken, UIDuplicatePolicy duplicatePolicy = UIDuplicatePolicy.Refresh)
        {
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

                if (toggle && IsShown(prefabPath))
                {
                    await HideAsync(prefabPath, UICloseReason.Programmatic, operationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return default;
                }

                if (groupId == null && !allowMultiple && singletonPanelsByPath.TryGetValue(prefabPath, out UIHandle existing) && existing.IsValid && existing.View != null)
                {
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
                    return !factory.IsDisposed && existing.IsValid ? existing : default;
                }

                UIHandle handle = await factory.OpenAsync(UIKind.Panel, UILayer.Panel, prefabPath, args, groupId != null || allowMultiple, cacheOnClose, null, operationToken);
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

                return !factory.IsDisposed && handle.IsValid ? handle : default;
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

        /// <summary>查找索引。</summary>
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

        /// <summary>显示分组顶部。</summary>
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

        /// <summary>剪枝分组。</summary>
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

        /// <summary>响应工厂已释放。</summary>
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

        /// <summary>取消订阅面板管理器。</summary>
        void Unsubscribe(UIHandle handle)
        {
            if (!ReferenceEquals(handle.View, null))
            {
                handle.View.Destroyed -= OnPanelDestroyed;
            }
        }

        /// <summary>响应面板已销毁。</summary>
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

        /// <summary>保存面板条目所需的数据与配套操作。</summary>
        readonly struct PanelEntry
        {
            public readonly UIHandle Handle;
            public readonly string? GroupId;
            /// <summary>使用调用方提供的handle、groupId创建 PanelEntry。</summary>
            public PanelEntry(UIHandle handle, string? groupId)
            {
                Handle = handle;
                GroupId = groupId;
            }
        }
    }
}

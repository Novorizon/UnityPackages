#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>弹窗对应的选项。定义可由调用方设置的选项。</summary>
    public sealed class PopupOptions
    {
        public bool Modal { get; set; } = true;
        public bool SingletonByPath { get; set; }
        public bool CacheOnClose { get; set; } = true;
        public UIDuplicatePolicy DuplicatePolicy { get; set; } = UIDuplicatePolicy.Refresh;
    }

    /// <summary>弹窗对应的管理器。管理对应模块的状态和操作入口。</summary>
    public sealed class PopupManager
    {
        readonly UIInstanceFactory factory;
        readonly List<UIHandle> opened = new();
        /// <summary>使用调用方提供的factory创建 PopupManager。</summary>
        public PopupManager(UIInstanceFactory factory)
        {
            this.factory = factory;
            factory.Disposed += OnFactoryDisposed;
            if (factory.IsDisposed)
            {
                OnFactoryDisposed();
            }
        }

        public int Count
        {
            get
            {
                RemoveInvalid();
                return opened.Count;
            }
        }

        /// <summary>查看弹窗管理器。</summary>
        public UIHandle? Peek()
        {
            RemoveInvalid();
            return opened.Count == 0 ? (UIHandle? )null : opened[opened.Count - 1];
        }

        /// <summary>异步打开弹窗管理器。</summary>
        public async Task<UIHandle> OpenAsync(string prefabPath, object? args = null, PopupOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PopupOptions opt = options ?? new PopupOptions();
            UIBlocker? blocker = null;
            UIHandle handle = await factory.OpenAsync(UIKind.Popup, UILayer.Popup, prefabPath, args, allowMultiple: !opt.SingletonByPath, cacheOnClose: opt.CacheOnClose, blockerFactory: parent => blocker = opt.Modal ? UIBlocker.Create(parent) : null, cancellationToken: cancellationToken, duplicatePolicy: opt.DuplicatePolicy);
            if (cancellationToken.IsCancellationRequested)
            {
                // A singleton refresh may return a lease that was already owned by another call.
                if (handle.IsValid && (!opt.SingletonByPath || IndexOf(handle) < 0))
                {
                    factory.CloseApproved(handle, false, true, UICloseReason.Cancelled);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!handle.IsValid)
            {
                return default;
            }

            RemoveInvalid();
            int previousIndex = IndexOf(handle);
            if (previousIndex >= 0)
            {
                opened.RemoveAt(previousIndex);
                opened.Add(handle);
                return handle;
            }

            if (handle.View is UIPopup popup && blocker != null)
            {
                blocker.Clicked += () =>
                {
                    if (popup.CloseOnBlockerClick)
                    {
                        _ = CloseFromBlockerAsync(handle);
                    }
                };
            }

            opened.Add(handle);
            handle.View!.Destroyed += OnPopupDestroyed;
            return handle;
        }

        /// <summary>关闭顶部。</summary>
        public bool CloseTop(UICloseReason reason = UICloseReason.Back)
        {
            UIHandle? top = Peek();
            return top.HasValue && TryClose(top.Value, reason);
        }

        /// <summary>异步关闭顶部。</summary>
        public async Task<bool> CloseTopAsync(UICloseReason reason = UICloseReason.Back, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UIHandle? top = Peek();
            if (!top.HasValue)
            {
                return false;
            }

            return await CloseCoreAsync(top.Value, reason, true, cancellationToken);
        }

        /// <summary>关闭弹窗管理器。</summary>
        public void Close(UIHandle handle) => TryClose(handle);
        /// <summary>尝试处理 Close，返回是否成功。</summary>
        public bool TryClose(UIHandle handle, UICloseReason reason = UICloseReason.Programmatic)
        {
            if (IndexOf(handle) < 0 || !factory.CanClose(handle, reason))
            {
                return false;
            }

            return ForceClose(handle, reason);
        }

        /// <summary>异步关闭弹窗管理器。</summary>
        public Task<bool> CloseAsync(UIHandle handle, UICloseReason reason = UICloseReason.Programmatic, CancellationToken cancellationToken = default) => CloseCoreAsync(handle, reason, false, cancellationToken);
        /// <summary>异步关闭弹窗管理器。</summary>
        async Task<bool> CloseCoreAsync(UIHandle handle, UICloseReason reason, bool requireTop, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IndexOf(handle) < 0)
            {
                return false;
            }

            bool approved = await factory.CanCloseAsync(handle, reason, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!approved || IndexOf(handle) < 0)
            {
                return false;
            }

            if (requireTop)
            {
                UIHandle? top = Peek();
                if (!top.HasValue || top.Value.Id != handle.Id || !ReferenceEquals(top.Value.View, handle.View))
                {
                    return false;
                }
            }

            return ForceClose(handle, reason);
        }

        /// <summary>返回ForceClose的判断结果。</summary>
        internal bool ForceClose(UIHandle handle, UICloseReason reason, bool destroy = false)
        {
            int index = IndexOf(handle);
            if (index < 0 || !handle.IsValid || factory.IsDisposed)
            {
                return false;
            }

            opened.RemoveAt(index);
            Unsubscribe(handle);
            factory.CloseApproved(handle, destroy, !destroy, reason);
            return true;
        }

        /// <summary>异步关闭 From 屏蔽项。</summary>
        async Task CloseFromBlockerAsync(UIHandle handle)
        {
            try
            {
                await CloseCoreAsync(handle, UICloseReason.Blocker, true, default);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>移除无效。</summary>
        void RemoveInvalid()
        {
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                if (opened[i].IsValid)
                {
                    continue;
                }

                UIHandle handle = opened[i];
                opened.RemoveAt(i);
                Unsubscribe(handle);
            }
        }

        /// <summary>响应弹窗已销毁。</summary>
        void OnPopupDestroyed(UIView view)
        {
            view.Destroyed -= OnPopupDestroyed;
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(opened[i].View, view))
                {
                    opened.RemoveAt(i);
                }
            }
        }

        /// <summary>响应工厂已释放。</summary>
        void OnFactoryDisposed()
        {
            factory.Disposed -= OnFactoryDisposed;
            foreach (UIHandle handle in opened)
            {
                Unsubscribe(handle);
            }

            opened.Clear();
        }

        /// <summary>取消订阅弹窗管理器。</summary>
        void Unsubscribe(UIHandle handle)
        {
            if (!ReferenceEquals(handle.View, null))
            {
                handle.View.Destroyed -= OnPopupDestroyed;
            }
        }

        /// <summary>建立索引 Of。</summary>
        int IndexOf(UIHandle handle)
        {
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                if (opened[i].Id == handle.Id && ReferenceEquals(opened[i].View, handle.View))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}

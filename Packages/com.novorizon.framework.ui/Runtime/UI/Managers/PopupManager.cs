#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    public sealed class PopupOptions
    {
        public bool Modal { get; set; } = true;
        public bool SingletonByPath { get; set; }
        public bool CacheOnClose { get; set; } = true;
        public UIDuplicatePolicy DuplicatePolicy { get; set; } = UIDuplicatePolicy.Refresh;
    }

    public sealed class PopupManager
    {
        readonly UIInstanceFactory factory;
        readonly List<UIHandle> opened = new();
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

        public UIHandle? Peek()
        {
            RemoveInvalid();
            return opened.Count == 0 ? (UIHandle? )null : opened[opened.Count - 1];
        }

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

        public bool CloseTop(UICloseReason reason = UICloseReason.Back)
        {
            UIHandle? top = Peek();
            return top.HasValue && TryClose(top.Value, reason);
        }

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

        public void Close(UIHandle handle) => TryClose(handle);
        public bool TryClose(UIHandle handle, UICloseReason reason = UICloseReason.Programmatic)
        {
            if (IndexOf(handle) < 0 || !factory.CanClose(handle, reason))
            {
                return false;
            }

            return ForceClose(handle, reason);
        }

        public Task<bool> CloseAsync(UIHandle handle, UICloseReason reason = UICloseReason.Programmatic, CancellationToken cancellationToken = default) => CloseCoreAsync(handle, reason, false, cancellationToken);
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

        void OnFactoryDisposed()
        {
            factory.Disposed -= OnFactoryDisposed;
            foreach (UIHandle handle in opened)
            {
                Unsubscribe(handle);
            }

            opened.Clear();
        }

        void Unsubscribe(UIHandle handle)
        {
            if (!ReferenceEquals(handle.View, null))
            {
                handle.View.Destroyed -= OnPopupDestroyed;
            }
        }

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

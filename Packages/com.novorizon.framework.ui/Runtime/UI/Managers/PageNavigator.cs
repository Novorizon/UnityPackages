#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UI
{
    public sealed class PageNavigator
    {
        readonly UIInstanceFactory factory;
        readonly Stack<UIHandle> stack = new();
        readonly SemaphoreSlim navigation = new(1, 1);
        readonly CancellationTokenSource lifetime = new();
        int pendingNavigation;

        public PageNavigator(UIInstanceFactory factory)
        {
            this.factory = factory;
            factory.Disposed += OnFactoryDisposed;
            if (factory.IsDisposed) OnFactoryDisposed();
        }

        public int Count { get { PruneClosedPages(); return stack.Count; } }
        public UIHandle? Peek() { PruneClosedPages(); return stack.Count > 0 ? stack.Peek() : (UIHandle?)null; }

        public Task<UIHandle> PushAsync(string prefabPath, object? args = null, CancellationToken cancellationToken = default)
            => NavigateAsync(prefabPath, args, Navigation.Push, cancellationToken);
        public Task<UIHandle> ReplaceAsync(string prefabPath, object? args = null, CancellationToken cancellationToken = default)
            => NavigateAsync(prefabPath, args, Navigation.Replace, cancellationToken);
        public Task<UIHandle> ResetToAsync(string prefabPath, object? args = null, CancellationToken cancellationToken = default)
            => NavigateAsync(prefabPath, args, Navigation.Reset, cancellationToken);

        public bool Pop(UICloseReason reason = UICloseReason.Back)
        {
            if (!CanStartPop(out UIHandle top) || !factory.CanClose(top, reason) || !IsTop(top)) return false;
            return PopApproved(top, reason);
        }

        public async Task<bool> PopAsync(UICloseReason reason = UICloseReason.Back, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CanStartPop(out UIHandle top)) return false;
            bool approved = await factory.CanCloseAsync(top, reason, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!approved || factory.IsDisposed || pendingNavigation != 0 || !IsTop(top)) return false;
            return PopApproved(top, reason);
        }

        bool CanStartPop(out UIHandle top)
        {
            top = default;
            if (factory.IsDisposed || pendingNavigation != 0) return false;
            UIHandle? current = Peek();
            if (!current.HasValue) return false;
            top = current.Value;
            return true;
        }
        bool IsTop(UIHandle handle)
        {
            UIHandle? current = Peek();
            return handle.IsValid && current.HasValue && current.Value.Id == handle.Id && ReferenceEquals(current.Value.View, handle.View);
        }
        bool PopApproved(UIHandle top, UICloseReason reason)
        {
            if (!IsTop(top)) return false;
            Unsubscribe(stack.Pop());
            try { factory.CloseApproved(top, false, true, reason); }
            finally { ShowTop(); }
            return true;
        }

        async Task<UIHandle> NavigateAsync(string prefabPath, object? args, Navigation mode, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (factory.IsDisposed) return default;
            using CancellationTokenSource? linked = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken) : null;
            CancellationToken operationToken = linked?.Token ?? lifetime.Token;
            bool acquired = false;
            pendingNavigation++;
            try
            {
                await navigation.WaitAsync(operationToken);
                acquired = true;
                cancellationToken.ThrowIfCancellationRequested();
                if (factory.IsDisposed) return default;

                UIHandle? expectedPrevious = Peek();
                if (mode == Navigation.Replace && expectedPrevious.HasValue)
                {
                    bool approved = await factory.CanCloseAsync(expectedPrevious.Value, UICloseReason.Replaced, operationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!approved || factory.IsDisposed || !IsTop(expectedPrevious.Value)) return default;
                }

                // The old page stays visible until the destination has loaded successfully.
                UIHandle handle = await factory.OpenAsync(UIKind.Page, UILayer.Page, prefabPath, args, true, true, null, operationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    if (handle.IsValid) factory.CloseApproved(handle, false, true, UICloseReason.Cancelled);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (factory.IsDisposed || !handle.IsValid) return default;
                if (mode == Navigation.Replace && expectedPrevious.HasValue && !IsTop(expectedPrevious.Value))
                {
                    factory.CloseApproved(handle, false, true, UICloseReason.Cancelled);
                    return default;
                }

                UIHandle? previous = Peek();
                UIHandle[]? replaced = null;
                if (mode == Navigation.Reset)
                {
                    replaced = stack.ToArray();
                    foreach (UIHandle old in replaced) Unsubscribe(old);
                    stack.Clear();
                }
                else if (mode == Navigation.Replace && previous.HasValue) Unsubscribe(stack.Pop());

                // Publish the new top before any old page cleanup callback can navigate again.
                stack.Push(handle);
                handle.View!.Destroyed += OnPageDestroyed;
                if (replaced != null)
                {
                    Exception? failure = null;
                    foreach (UIHandle old in replaced)
                        try { factory.CloseApproved(old, false, true, UICloseReason.Reset); }
                        catch (Exception exception) { failure ??= exception; }
                    if (failure != null) throw failure;
                }
                else if (mode == Navigation.Replace && previous.HasValue)
                    factory.CloseApproved(previous.Value, false, true, UICloseReason.Replaced);
                else if (previous.HasValue && previous.Value.IsValid)
                    previous.Value.View!.InternalSetVisible(false);

                return !factory.IsDisposed && handle.IsValid ? handle : default;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && lifetime.IsCancellationRequested)
            { return default; }
            finally
            {
                pendingNavigation--;
                if (acquired) navigation.Release();
            }
        }

        void ShowTop()
        {
            UIHandle? next = Peek();
            if (next.HasValue && next.Value.IsValid) next.Value.View!.InternalSetVisible(true);
        }
        void PruneClosedPages()
        {
            bool removed = false;
            while (stack.Count > 0 && !stack.Peek().IsValid) { Unsubscribe(stack.Pop()); removed = true; }
            if (removed && !factory.IsDisposed && stack.Count > 0 && stack.Peek().IsValid)
                stack.Peek().View!.InternalSetVisible(true);
        }
        void OnFactoryDisposed()
        {
            factory.Disposed -= OnFactoryDisposed;
            foreach (UIHandle handle in stack) Unsubscribe(handle);
            stack.Clear(); lifetime.Cancel();
        }
        void Unsubscribe(UIHandle handle)
        {
            if (!ReferenceEquals(handle.View, null)) handle.View.Destroyed -= OnPageDestroyed;
        }
        void OnPageDestroyed(UIView view)
        {
            view.Destroyed -= OnPageDestroyed;
            if (factory.IsDisposed) return;
            UIHandle[] history = stack.ToArray();
            bool wasTop = history.Length > 0 && ReferenceEquals(history[0].View, view);
            stack.Clear();
            for (int i = history.Length - 1; i >= 0; i--)
                if (!ReferenceEquals(history[i].View, view) && history[i].IsValid) stack.Push(history[i]);
            if (wasTop) ShowTop();
        }
        enum Navigation { Push, Replace, Reset }
    }
}

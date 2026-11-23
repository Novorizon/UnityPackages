#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace UI
{
    public sealed class OverlayManager
    {
        readonly UIInstanceFactory factory;
        UIHandle blockingOverlay;
        TaskCompletionSource<UIHandle>? opening;
        CancellationTokenSource? openingCancellation;
        string? blockingPath;
        int blockingCount;
        int generation;

        public OverlayManager(UIInstanceFactory factory)
        {
            this.factory = factory;
            factory.Disposed += OnFactoryDisposed;
            if (factory.IsDisposed) OnFactoryDisposed();
        }

        /// <summary>Includes callers still waiting for the shared overlay to load.</summary>
        public bool IsBlocking => !factory.IsDisposed && blockingCount > 0;

        /// <summary>异步显示阻塞。</summary>
        public async Task<UIOverlayHandle<TView>> ShowBlockingAsync<TView>(UIKey<TView> key, object? args = null, CancellationToken cancellationToken = default) where TView : UIView
        {
            string prefabPath = key.Path;
            cancellationToken.ThrowIfCancellationRequested();
            if (factory.IsDisposed) return UIOverlayHandle<TView>.Empty;
            if (blockingCount > 0 && blockingPath != prefabPath)
                throw new InvalidOperationException("A blocking overlay already owns this manager. Release its tokens before opening a different prefab path.");

            int requestGeneration;
            Task<UIHandle> request;
            if (blockingCount == 0)
            {
                requestGeneration = ++generation;
                blockingPath = prefabPath;
                blockingCount = 1;
                var source = new TaskCompletionSource<UIHandle>(TaskCreationOptions.RunContinuationsAsynchronously);
                var cancellation = new CancellationTokenSource();
                opening = source; openingCancellation = cancellation;
                request = source.Task;
                _ = CompleteOpeningAsync(source, requestGeneration, key, args, cancellation);
            }
            else
            {
                requestGeneration = generation;
                blockingCount++;
                request = opening!.Task;
            }

            try
            {
                UIHandle handle = await UICancellation.WaitAsync(request, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (factory.IsDisposed || requestGeneration != generation) return UIOverlayHandle<TView>.Empty;
                if (!handle.IsValid || !UIInstanceFactory.MatchesView(handle.View, prefabPath, UIKey<TView>.MatchesView))
                {
                    ReleaseBlocking(requestGeneration);
                    return UIOverlayHandle<TView>.Empty;
                }

                return new UIOverlayHandle<TView>(this, requestGeneration, new UIHandle<TView>(handle));
            }
            catch { ReleaseBlocking(requestGeneration); throw; }
        }

        /// <summary>异步完成 Opening。</summary>
        async Task CompleteOpeningAsync<TView>(TaskCompletionSource<UIHandle> source, int requestGeneration, UIKey<TView> key, object? args, CancellationTokenSource cancellation) where TView : UIView
        {
            try
            {
                UIHandle handle = await factory.OpenAsync(UIKind.Overlay, UILayer.Overlay, key.Path, args, false, true, null, cancellation.Token, expectedView: UIKey<TView>.MatchesView);
                if (factory.IsDisposed || requestGeneration != generation || cancellation.IsCancellationRequested)
                {
                    if (handle.IsValid) factory.CloseApproved(handle, false, true, UICloseReason.Cancelled);
                    source.TrySetResult(default); return;
                }
                blockingOverlay = handle;
                if (handle.IsValid) handle.View!.Destroyed += OnOverlayDestroyed;
                source.TrySetResult(handle);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            { source.TrySetResult(default); }
            catch (Exception exception) { source.TrySetException(exception); }
            finally
            {
                if (ReferenceEquals(openingCancellation, cancellation)) openingCancellation = null;
                cancellation.Dispose();
            }
        }

        /// <summary>释放阻塞。</summary>
        internal void ReleaseBlocking(int tokenGeneration)
        {
            if (tokenGeneration != generation || blockingCount <= 0) return;
            if (--blockingCount != 0) return;
            UIHandle handle = blockingOverlay;
            Unsubscribe(handle);
            blockingOverlay = default; blockingPath = null; opening = null;
            generation++;
            CancelOpening();
            if (handle.IsValid) factory.CloseApproved(handle, false, true, UICloseReason.Completed);
        }
        void CancelOpening()
        {
            CancellationTokenSource? cancellation = openingCancellation;
            openingCancellation = null;
            cancellation?.Cancel(); // The load runner owns disposal, including synchronous completion.
        }
        void OnFactoryDisposed()
        {
            factory.Disposed -= OnFactoryDisposed;
            Invalidate();
        }
        void Invalidate()
        {
            TaskCompletionSource<UIHandle>? pending = opening;
            Unsubscribe(blockingOverlay);
            opening = null; blockingOverlay = default; blockingCount = 0; blockingPath = null;
            generation++; CancelOpening(); pending?.TrySetResult(default);
        }
        void Unsubscribe(UIHandle handle)
        {
            if (!ReferenceEquals(handle.View, null)) handle.View.Destroyed -= OnOverlayDestroyed;
        }
        void OnOverlayDestroyed(UIView view)
        {
            view.Destroyed -= OnOverlayDestroyed;
            if (ReferenceEquals(blockingOverlay.View, view)) Invalidate();
        }
    }

    /// <summary>一次阻塞请求。多个请求可共享视图，调用方释放自己的句柄后才解除这一份阻塞。</summary>
    public sealed class UIOverlayHandle<TView> : IDisposable where TView : UIView
    {
        internal static readonly UIOverlayHandle<TView> Empty = new(null, 0, default);

        OverlayManager? owner;
        readonly int generation;

        internal UIOverlayHandle(OverlayManager? owner, int generation, UIHandle<TView> handle)
        {
            this.owner = owner;
            this.generation = generation;
            Handle = handle;
        }

        public UIHandle<TView> Handle { get; }
        public bool IsValid => owner != null && Handle.IsValid;
        public TView? View => IsValid ? Handle.View : null;

        public void Dispose()
        {
            OverlayManager? current = owner;
            owner = null;
            current?.ReleaseBlocking(generation);
        }
    }

    internal static class UICancellation
    {
        // Only cancel this caller's wait. Shared loads keep running for their remaining owners.
        internal static async Task<T> WaitAsync<T>(Task<T> operation, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!cancellationToken.CanBeCanceled) return await operation;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                {
                    if (await Task.WhenAny(operation, cancelled.Task) != operation) cancellationToken.ThrowIfCancellationRequested();
                    cancellationToken.ThrowIfCancellationRequested();
                    return await operation;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _ = ObserveFailureAsync(operation);
                throw;
            }
        }
        static async Task ObserveFailureAsync<T>(Task<T> operation)
        { try { await operation.ConfigureAwait(false); } catch { } }
    }
}

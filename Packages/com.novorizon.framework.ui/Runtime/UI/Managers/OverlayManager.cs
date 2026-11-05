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

        public async Task<IDisposable> ShowBlockingAsync(string prefabPath, object? args = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (factory.IsDisposed) return EmptyToken.Instance;
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
                _ = CompleteOpeningAsync(source, requestGeneration, prefabPath, args, cancellation);
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
                if (factory.IsDisposed || requestGeneration != generation) return EmptyToken.Instance;
                if (!handle.IsValid) { ReleaseBlocking(requestGeneration); return EmptyToken.Instance; }
                return new Token(this, requestGeneration);
            }
            catch { ReleaseBlocking(requestGeneration); throw; }
        }

        async Task CompleteOpeningAsync(TaskCompletionSource<UIHandle> source, int requestGeneration, string prefabPath, object? args, CancellationTokenSource cancellation)
        {
            try
            {
                UIHandle handle = await factory.OpenAsync(UIKind.Overlay, UILayer.Overlay, prefabPath, args, false, true, null, cancellation.Token);
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

        void ReleaseBlocking(int tokenGeneration)
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
        sealed class Token : IDisposable
        {
            OverlayManager? owner;
            readonly int generation;
            public Token(OverlayManager owner, int generation) { this.owner = owner; this.generation = generation; }
            public void Dispose() { OverlayManager? current = owner; owner = null; current?.ReleaseBlocking(generation); }
        }
        sealed class EmptyToken : IDisposable
        {
            public static readonly EmptyToken Instance = new();
            public void Dispose() { }
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

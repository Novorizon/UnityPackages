#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>Await a result while owning exactly one popup open handle.</summary>
    public sealed class DialogManager
    {
        abstract class TrackedRequest
        {
            internal readonly CancellationToken Cancellation;
            internal readonly TaskCompletionSource<bool> Cleaned = new(TaskCreationOptions.RunContinuationsAsynchronously);
            protected TrackedRequest(CancellationToken cancellation) { Cancellation = cancellation; }
        }

        sealed class OpenRequest<TResult> : TrackedRequest, IUIOpenContext
        {
            readonly string path;
            public object? Args { get; }
            internal UIHandle Handle { get; private set; }
            internal Task<UIDialogResult<TResult>>? Result { get; private set; }

            internal OpenRequest(string path, object? args, CancellationToken cancellation) : base(cancellation)
            { this.path = path; Args = args; }

            public void Prepared(UIView view)
            {
                if (!(view is UIResultPopup<TResult> popup))
                    throw new InvalidOperationException($"Dialog {path} must derive from UIResultPopup<{typeof(TResult).Name}>.");
                if (Result != null) throw new InvalidOperationException("The dialog open request was already prepared.");
                Handle = new UIHandle(view.InstanceId, path, UIKind.Popup, UILayer.Popup, view);
                Result = popup.GetResultTask(Handle.Id);
            }
        }

        readonly PopupManager popups;
        readonly HashSet<TrackedRequest> requests = new();

        public DialogManager(PopupManager popups)
        {
            this.popups = popups ?? throw new ArgumentNullException(nameof(popups));
        }

        /// <summary>
        /// Selection returns a value; user dismissal returns a reason without a value.
        /// Caller cancellation throws OperationCanceledException and closes only this dialog.
        /// Call on Unity's main thread; cancellation callbacks only signal a task.
        /// </summary>
        public async Task<UIDialogResult<TResult>> ShowAsync<TResult>(string prefabPath, object? args = null,
            PopupOptions? options = null, CancellationToken cancellationToken = default)
        {
            var request = new OpenRequest<TResult>(prefabPath, args, cancellationToken);
            requests.Add(request);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (options != null && options.SingletonByPath)
                    throw new ArgumentException("Result dialogs require independent instances; SingletonByPath must be false.", nameof(options));

                // Copy mutable caller options before awaiting the load.
                var openingOptions = new PopupOptions
                {
                    Modal = options?.Modal ?? true,
                    CacheOnClose = options?.CacheOnClose ?? true,
                    SingletonByPath = false
                };
                UIHandle opened = await popups.OpenAsync(prefabPath, request, openingOptions, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                Task<UIDialogResult<TResult>>? resultTask = request.Result;
                if (resultTask == null)
                    throw new InvalidOperationException($"The dialog could not be opened: {prefabPath}");
                if (opened.IsValid && (opened.Id != request.Handle.Id || !ReferenceEquals(opened.View, request.Handle.View)))
                    throw new InvalidOperationException("The popup handle does not belong to this dialog open request.");

                // The view may already have closed or been reused between the factory,
                // popup manager and this continuation. Its captured task still belongs
                // to the original open; never ask the cached component for it again.
                if (!resultTask.IsCompleted && cancellationToken.CanBeCanceled)
                {
                    var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (cancellationToken.Register(() => cancelled.TrySetResult(true)))
                    {
                        if (await Task.WhenAny(resultTask, cancelled.Task) != resultTask)
                            cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                cancellationToken.ThrowIfCancellationRequested();
                UIDialogResult<TResult> result = await resultTask;

                // A business selection is a terminal result, not a second close request
                // to be vetoed by an asynchronous confirmation guard.
                bool closed = popups.ForceClose(request.Handle, result.CloseReason);
                if (!closed && request.Handle.IsValid)
                    throw new InvalidOperationException("The completed dialog still has a live popup registration.");
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cleanup(request.Handle, UICloseReason.Cancelled);
                throw;
            }
            catch
            {
                Cleanup(request.Handle, UICloseReason.Programmatic);
                throw;
            }
            finally
            {
                // Mark cleanup only after all UI work in the catch/normal completion.
                // The manager is main-thread owned; cancellation callbacks only signal tasks.
                requests.Remove(request);
                request.Cleaned.TrySetResult(true);
            }
        }

        /// <summary>
        /// Wait for this manager's currently cancelled dialog requests to finish UI cleanup.
        /// Does not wait for uncancelled dialogs, user guards, or an unabortable underlying load.
        /// Use the owner's lifetime token here, never the cancelled close-request token.
        /// </summary>
        internal async Task DrainCancelledAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<Task<bool>>? pending = null;
            foreach (TrackedRequest request in requests)
            {
                if (!request.Cancellation.IsCancellationRequested || request.Cleaned.Task.IsCompleted) continue;
                (pending ??= new List<Task<bool>>()).Add(request.Cleaned.Task);
            }
            if (pending == null) return;
            await UICancellation.WaitAsync(Task.WhenAll(pending), cancellationToken);
        }

        void Cleanup(UIHandle handle, UICloseReason reason, bool destroy = false)
        {
            try { popups.ForceClose(handle, reason, destroy); }
            catch (Exception exception) { Debug.LogException(exception); }
        }
    }
}

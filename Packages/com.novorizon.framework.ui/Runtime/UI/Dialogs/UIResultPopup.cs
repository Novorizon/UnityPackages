#nullable enable
using System;
using System.Threading.Tasks;

namespace UI
{
    /// <summary>Derive a concrete, non-generic prefab component for a typed dialog.</summary>
    public abstract class UIResultPopup<TResult> : UIPopup, IUIResultSource
    {
        sealed class ResultSession
        {
            internal readonly int Id;
            internal readonly TaskCompletionSource<UIDialogResult<TResult>> Completion =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal ResultSession(int id) { Id = id; }
        }

        ResultSession? session;

        void IUIResultSource.PrepareResult()
        {
            // An active singleton may be refreshed by other popup callers. It still owns
            // the same lease and must not replace a task already awaited by the dialog.
            if (session != null && session.Id == InstanceId) return;
            session?.Completion.TrySetResult(UIDialogResult<TResult>.Dismissed(UICloseReason.Replaced));
            session = new ResultSession(InstanceId);
            Closed -= OnResultClosed;
            Closed += OnResultClosed;
            Destroyed -= OnResultDestroyed;
            Destroyed += OnResultDestroyed;
        }

        internal Task<UIDialogResult<TResult>> GetResultTask(int id)
        {
            if (session == null || session.Id != id)
                throw new InvalidOperationException("The dialog result session does not belong to this open handle.");
            return session.Completion.Task;
        }

        /// <summary>Complete the current dialog; duplicate or closed attempts are ignored.</summary>
        protected void Complete(TResult result) => TryComplete(result);

        protected bool TryComplete(TResult result) => TryComplete(session, result);

        /// <summary>
        /// Capture when starting asynchronous work. A callback retained by an old open
        /// cannot complete a later dialog that reuses the same cached component.
        /// </summary>
        protected Action<TResult> CaptureCompletion()
        {
            ResultSession? captured = session;
            return result => TryComplete(captured, result);
        }

        bool TryComplete(ResultSession? expected, TResult result)
        {
            if (expected == null || !ReferenceEquals(session, expected) ||
                expected.Id != InstanceId || !IsOpen || IsDestroyed) return false;
            return expected.Completion.TrySetResult(UIDialogResult<TResult>.Completed(result));
        }

        void OnResultClosed(UIView view)
        {
            ResultSession? current = session;
            if (current != null && current.Id == InstanceId)
            {
                // Cached components must not retain the last result or its business graph.
                session = null;
                current.Completion.TrySetResult(UIDialogResult<TResult>.Dismissed(LastCloseReason));
            }
        }

        void OnResultDestroyed(UIView view)
        {
            ResultSession? current = session;
            session = null;
            current?.Completion.TrySetResult(UIDialogResult<TResult>.Dismissed(UICloseReason.Destroyed));
        }
    }
}

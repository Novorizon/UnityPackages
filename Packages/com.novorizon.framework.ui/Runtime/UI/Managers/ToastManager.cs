#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    public sealed class ToastOptions
    {
        public string? MergeKey { get; set; }
    }

    public sealed class ToastManager : IDisposable
    {
        readonly UIInstanceFactory factory;
        readonly Queue<(string path, object? args, ToastOptions? options)> queue = new();
        UIHandle current;
        bool isRunning;
        bool disposed;
        int generation;
        CancellationTokenSource? runningCancellation;
        public ToastManager(UIInstanceFactory factory)
        {
            this.factory = factory;
            factory.Disposed += Dispose;
        }

        public void Enqueue(string prefabPath, object? args = null, ToastOptions? options = null)
        {
            if (disposed || factory.IsDisposed)
            {
                return;
            }

            queue.Enqueue((prefabPath, args, options));
            StartIfNeeded();
        }

        void StartIfNeeded()
        {
            if (isRunning || disposed || factory.IsDisposed || queue.Count == 0)
            {
                return;
            }

            isRunning = true;
            var cancellation = new CancellationTokenSource();
            runningCancellation = cancellation;
            _ = RunAsync(generation, cancellation);
        }

        async Task RunAsync(int runGeneration, CancellationTokenSource cancellation)
        {
            try
            {
                while (!disposed && runGeneration == generation && queue.Count > 0)
                {
                    var item = queue.Dequeue();
                    UIHandle handle;
                    try
                    {
                        handle = await factory.OpenAsync(UIKind.Toast, UILayer.Toast, item.path, item.args, true, true, null, cancellation.Token);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        continue;
                    }

                    if (disposed || runGeneration != generation)
                    {
                        CloseItem(handle);
                        return;
                    }

                    if (!handle.IsValid || !(handle.View is UIToast toast))
                    {
                        CloseItem(handle, destroy: true);
                        continue;
                    }

                    current = handle;
                    try
                    {
                        await toast.Completion;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                    finally
                    {
                        CloseItem(handle);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                cancellation.Dispose();
                if (ReferenceEquals(runningCancellation, cancellation))
                {
                    runningCancellation = null;
                }

                if (runGeneration == generation)
                {
                    isRunning = false;
                    StartIfNeeded();
                }
            }
        }

        void CloseItem(UIHandle handle, bool destroy = false)
        {
            try
            {
                factory.CloseApproved(handle, destroy, !destroy, destroy ? UICloseReason.Cancelled : UICloseReason.Completed);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                // OnClose/OnDisable may clear the queue and open a new generation synchronously.
                if (current.Id == handle.Id && ReferenceEquals(current.View, handle.View))
                {
                    current = default;
                }
            }
        }

        /// <summary>Discard queued toasts and finish the currently displayed toast.</summary>
        public void Clear()
        {
            generation++;
            queue.Clear();
            UIHandle handle = current;
            current = default;
            isRunning = false;
            CancellationTokenSource? cancellation = runningCancellation;
            runningCancellation = null;
            try
            {
                cancellation?.Cancel();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }

            factory.CloseApproved(handle, false, true, UICloseReason.Cancelled);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            factory.Disposed -= Dispose;
            Clear();
        }
    }
}

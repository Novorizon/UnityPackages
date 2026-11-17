#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>提示对应的选项。定义可由调用方设置的选项。</summary>
    public sealed class ToastOptions
    {
        public string? MergeKey { get; set; }
    }

    /// <summary>提示对应的管理器。管理对应模块的状态和操作入口。</summary>
    public sealed class ToastManager : IDisposable
    {
        readonly UIInstanceFactory factory;
        readonly Queue<(string path, object? args, ToastOptions? options)> queue = new();
        UIHandle current;
        bool isRunning;
        bool disposed;
        int generation;
        CancellationTokenSource? runningCancellation;
        /// <summary>使用调用方提供的factory创建 ToastManager。</summary>
        public ToastManager(UIInstanceFactory factory)
        {
            this.factory = factory;
            factory.Disposed += Dispose;
        }

        /// <summary>入队提示管理器。</summary>
        public void Enqueue(string prefabPath, object? args = null, ToastOptions? options = null)
        {
            if (disposed || factory.IsDisposed)
            {
                return;
            }

            queue.Enqueue((prefabPath, args, options));
            StartIfNeeded();
        }

        /// <summary>启动 IfNeeded。</summary>
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

        /// <summary>异步运行当前操作。</summary>
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

        /// <summary>关闭条目。</summary>
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
        /// <remarks>清空提示管理器。</remarks>
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

        /// <summary>结束提示管理器对相关资源的持有，并执行该类型的清理流程。</summary>
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

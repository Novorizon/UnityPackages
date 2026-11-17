using System;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework
{
    /// <summary>提供任务 Util的实现。</summary>
    public static class TaskUtil
    {
        /// <summary>异步等待当前操作完成。</summary>
        public static async Task WaitAsync(Task task, CancellationToken ct = default)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (ct.IsCancellationRequested) { Observe(task); ct.ThrowIfCancellationRequested(); }
            if (!ct.CanBeCanceled || task.IsCompleted) { await task.ConfigureAwait(false); return; }
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => canceled.TrySetCanceled()))
            {
                if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task)
                { Observe(task); ct.ThrowIfCancellationRequested(); }
                await task.ConfigureAwait(false);
            }
        }
        /// <summary>异步等待当前操作完成。</summary>
        public static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct = default)
        { await WaitAsync((Task)task, ct).ConfigureAwait(false); return await task.ConfigureAwait(false); }
        /// <summary>观察任务 Util。</summary>
        private static void Observe(Task task)
        {
            _ = task.ContinueWith(value => { _ = value.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}

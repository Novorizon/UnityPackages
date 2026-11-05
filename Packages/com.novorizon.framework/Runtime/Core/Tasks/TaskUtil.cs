using System;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework
{
    public static class TaskUtil
    {
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
        public static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct = default)
        { await WaitAsync((Task)task, ct).ConfigureAwait(false); return await task.ConfigureAwait(false); }
        private static void Observe(Task task)
        {
            _ = task.ContinueWith(value => { _ = value.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}

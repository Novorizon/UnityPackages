using System;
using System.Threading;
using System.Threading.Tasks;
namespace Game.Framework
{
    // One-shot asynchronous signal. Caller cancellation is distinct from a five-second timeout.
    /// <summary>提供信号的实现。</summary>
    public class Signal
    {
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        /// <summary>异步等待当前操作完成。</summary>
        public Task WaitAsync(CancellationToken ct = default) => TaskUtil.WaitAsync(completion.Task, ct);
        /// <summary>设置 True。</summary>
        public void SetTrue() => completion.TrySetResult(true);
        /// <summary>异步等待信号。</summary>
        public async Task<bool> WaitSignalAsync(CancellationToken ct = default)
        {
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                try { await WaitAsync(timeout.Token).ConfigureAwait(false); return true; }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return false; }
            }
        }
    }
}

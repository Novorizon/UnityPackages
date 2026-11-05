using System;
using System.Threading;
using System.Threading.Tasks;
namespace Game.Framework
{
    // One-shot asynchronous signal. Caller cancellation is distinct from a five-second timeout.
    public class Signal
    {
        private readonly TaskCompletionSource<bool> completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WaitAsync(CancellationToken ct = default) => TaskUtil.WaitAsync(completion.Task, ct);
        public void SetTrue() => completion.TrySetResult(true);
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

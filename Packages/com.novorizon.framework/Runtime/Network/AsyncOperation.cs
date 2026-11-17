using System.Threading;
using System.Threading.Tasks;
namespace Game.Framework.Network
{
    /// <summary>提供异步操作的实现。</summary>
    internal static class AsyncOperation
    {
        /// <summary>异步等待当前操作完成。</summary>
        internal static Task WaitAsync(Task task, CancellationToken ct) => Game.Framework.TaskUtil.WaitAsync(task, ct);
        /// <summary>异步等待当前操作完成。</summary>
        internal static Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct) => Game.Framework.TaskUtil.WaitAsync(task, ct);
    }
}

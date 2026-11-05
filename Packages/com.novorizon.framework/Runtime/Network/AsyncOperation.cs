using System.Threading;
using System.Threading.Tasks;
namespace Game.Framework.Network
{
    internal static class AsyncOperation
    {
        internal static Task WaitAsync(Task task, CancellationToken ct) => Game.Framework.TaskUtil.WaitAsync(task, ct);
        internal static Task<T> WaitAsync<T>(Task<T> task, CancellationToken ct) => Game.Framework.TaskUtil.WaitAsync(task, ct);
    }
}

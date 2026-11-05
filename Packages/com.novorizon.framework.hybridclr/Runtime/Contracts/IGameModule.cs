using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.HotUpdate
{
    // AOT contract. Changes require a new player baseline and contract revision.
    public interface IGameHost
    {
        void Log(string message);
        void ReportReady(string moduleVersion);
    }

    public interface IGameModule
    {
        Task InitializeAsync(IGameHost host, CancellationToken token);
        void Shutdown();
    }
}

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

    /// <summary>可选的项目启动参数。原有只使用 IGameHost 的模块不需要实现或依赖此接口。</summary>
    public interface IGameContextHost : IGameHost
    {
        object StartupContext { get; }
    }

    public interface IGameModule
    {
        Task InitializeAsync(IGameHost host, CancellationToken token);
        void Shutdown();
    }
}

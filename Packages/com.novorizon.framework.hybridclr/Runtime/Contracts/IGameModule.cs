using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.HotUpdate
{
    // AOT contract. Changes require a new player baseline and contract revision.
    /// <summary>定义游戏宿主的调用契约。</summary>
    public interface IGameHost
    {
        /// <summary>记录游戏宿主。</summary>
        void Log(string message);
        /// <summary>上报就绪。</summary>
        void ReportReady(string moduleVersion);
    }

    /// <summary>可选的项目启动参数。原有只使用 IGameHost 的模块不需要实现或依赖此接口。</summary>
    public interface IGameContextHost : IGameHost
    {
        object StartupContext { get; }
    }

    /// <summary>定义游戏应用事件的调用契约。</summary>
    public interface IGameApplicationEvents
    {
        /// <summary>响应应用进入后台或从后台恢复。</summary>
        void OnApplicationPause(bool paused);
        /// <summary>响应应用焦点的变化。</summary>
        void OnApplicationFocus(bool focused);
    }

    /// <summary>定义游戏模块的调用契约。</summary>
    public interface IGameModule
    {
        /// <summary>异步初始化游戏模块。</summary>
        Task InitializeAsync(IGameHost host, CancellationToken token);
        /// <summary>关闭游戏模块。</summary>
        void Shutdown();
    }
}

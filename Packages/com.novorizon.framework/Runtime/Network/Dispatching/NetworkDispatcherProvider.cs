using System;

namespace Game.Framework.Network.Dispatching
{
    /// <summary>定义自动网络调度器的调用契约。</summary>
    internal interface IAutomaticNetworkDispatcher : INetworkDispatcher, IDisposable { }

    // Optional host integration installs this at startup; the core has no Unity dependency.
    /// <summary>网络调度器对应的厂商适配器。提供此模块需要的能力或数据。</summary>
    internal static class NetworkDispatcherProvider
    {
        internal static Func<IAutomaticNetworkDispatcher> CreateAutomatic { get; set; }
    }
}

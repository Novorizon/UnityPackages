using System;

namespace Game.Framework.Network.Dispatching
{
    internal interface IAutomaticNetworkDispatcher : INetworkDispatcher, IDisposable { }

    // Optional host integration installs this at startup; the core has no Unity dependency.
    internal static class NetworkDispatcherProvider
    {
        internal static Func<IAutomaticNetworkDispatcher> CreateAutomatic { get; set; }
    }
}

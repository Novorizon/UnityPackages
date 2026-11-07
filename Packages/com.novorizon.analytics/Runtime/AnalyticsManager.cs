using System.Collections.Generic;

namespace Game.Framework.Analytics
{
    /// <summary>Business-facing entry point; configure vendor adapters at application startup.</summary>
    public sealed class AnalyticsManager : AnalyticsClient
    {
        public AnalyticsManager(
            IEnumerable<IAnalyticsBackend> backends,
            bool ownsBackends = false) : base(backends, ownsBackends)
        {
        }
    }
}

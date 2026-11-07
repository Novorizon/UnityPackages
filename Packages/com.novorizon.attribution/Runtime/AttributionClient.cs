using System;
using Game.Framework.Analytics;

namespace Game.Framework.Attribution
{
    /// <summary>Allowlisted marketing data; excludes raw SDK response bodies and device/ad identifiers.</summary>
    public sealed class AttributionSnapshot
    {
        public string Provider { get; }
        public string Network { get; }
        public string Campaign { get; }
        public string Adgroup { get; }
        public string Creative { get; }
        public bool? IsOrganic { get; }

        public AttributionSnapshot(string provider, string network, string campaign, string adgroup, string creative, bool? isOrganic)
        {
            Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            Network = network;
            Campaign = campaign;
            Adgroup = adgroup;
            Creative = creative;
            IsOrganic = isOrganic;
        }
    }

    public interface IAttributionBackend : IAnalyticsBackend, IRevenueAnalytics
    {
        event Action<AttributionSnapshot> AttributionChanged;
    }

    public abstract class AttributionBackend : AnalyticsBackend, IAttributionBackend
    {
        public event Action<AttributionSnapshot> AttributionChanged;

        protected void Publish(AttributionSnapshot snapshot)
        {
            if (IsDisposed)
            {
                return;
            }
            long epoch = CollectionEpoch;
            // Always queue: SDKs can call back inside initialization. Business handlers run afterwards.
            UnityThread.TryPost(() =>
            {
                if (IsDisposed || !CollectionEnabled || CollectionEpoch != epoch)
                {
                    return;
                }
                var handlers = AttributionChanged;
                if (handlers == null)
                {
                    return;
                }
                foreach (Action<AttributionSnapshot> handler in handlers.GetInvocationList())
                {
                    try
                    {
                        handler(snapshot);
                    }
                    catch (Exception error)
                    {
                        Logger.Exception(error);
                    }
                }
            });
        }

        public void LogRevenue(RevenueEvent revenue)
        {
            Ready(true);
            if (!CollectionEnabled || revenue == null)
            {
                throw new InvalidOperationException("Revenue reporting needs an enabled backend and a revenue event.");
            }
            OnLogRevenue(revenue);
        }

        protected abstract void OnLogRevenue(RevenueEvent revenue);

        protected override void OnDispose()
        {
            AttributionChanged = null;
        }
    }

    /// <summary>Select one MMP per client/project. Product analytics remains a separate route.</summary>
    public class AttributionClient : IDisposable
    {
        private readonly AnalyticsClient client;
        private readonly IAttributionBackend backend;
        private bool disposed;
        public event Action<AttributionSnapshot> AttributionChanged;

        public AttributionClient(IAttributionBackend backend, bool ownsBackend = false)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            client = new AnalyticsClient(new[] { backend }, ownsBackend);
            backend.AttributionChanged += OnChanged;
        }

        public TelemetryReport Start(bool collectionEnabled) => client.Start(collectionEnabled);
        public TelemetryReport SetCollectionEnabled(bool enabled) => client.SetCollectionEnabled(enabled);
        public TelemetryReport SetUserId(string userId) => client.SetUserId(userId);
        public TelemetryReport LogEvent(string name, System.Collections.Generic.IReadOnlyDictionary<string, object> properties = null) => client.LogEvent(name, properties);
        public TelemetryReport LogRevenue(RevenueEvent revenue) => client.LogRevenue(revenue);

        private void OnChanged(AttributionSnapshot snapshot)
        {
            if (disposed || !client.CollectionEnabled)
            {
                return;
            }
            var handlers = AttributionChanged;
            if (handlers == null)
            {
                return;
            }
            foreach (Action<AttributionSnapshot> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(snapshot);
                }
                catch (Exception error)
                {
                    Logger.Exception(error);
                }
            }
        }

        public void Dispose()
        {
            AnalyticsBackend.MainThread();
            if (disposed)
            {
                return;
            }
            // If called reentrantly, let AnalyticsClient reject disposal before removing the listener.
            client.Dispose();
            disposed = true;
            backend.AttributionChanged -= OnChanged;
            AttributionChanged = null;
        }
    }
}

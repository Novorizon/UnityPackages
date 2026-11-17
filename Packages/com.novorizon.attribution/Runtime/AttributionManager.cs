using System;
using Game.Framework.Analytics;

namespace Game.Framework.Attribution
{
    /// <summary>Allowlisted marketing data; excludes raw SDK response bodies and device/ad identifiers.</summary>
    /// <remarks>归因对应的快照。保存采样时刻的数据，供其他流程读取。</remarks>
    public sealed class AttributionSnapshot
    {
        public string Provider { get; }
        public string Network { get; }
        public string Campaign { get; }
        public string Adgroup { get; }
        public string Creative { get; }
        public bool? IsOrganic { get; }

        /// <summary>使用调用方提供的provider、network、campaign创建 AttributionSnapshot。</summary>
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

    /// <summary>定义归因后端的调用契约。</summary>
    public interface IAttributionBackend : IAnalyticsBackend, IRevenueAnalytics
    {
        event Action<AttributionSnapshot> AttributionChanged;
    }

    /// <summary>归因对应的后端。承接上层请求，调用具体平台或实现。</summary>
    public abstract class AttributionBackend : AnalyticsBackend, IAttributionBackend
    {
        public event Action<AttributionSnapshot> AttributionChanged;

        /// <summary>发布归因后端。</summary>
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

        /// <summary>记录收入。</summary>
        public void LogRevenue(RevenueEvent revenue)
        {
            Ready(true);
            if (!CollectionEnabled || revenue == null)
            {
                throw new InvalidOperationException("Revenue reporting needs an enabled backend and a revenue event.");
            }
            OnLogRevenue(revenue);
        }

        /// <summary>响应 Log 收入。</summary>
        protected abstract void OnLogRevenue(RevenueEvent revenue);

        /// <summary>响应 Dispose。</summary>
        protected override void OnDispose()
        {
            AttributionChanged = null;
        }
    }

    /// <summary>Select one MMP per client/project. Product analytics remains a separate route.</summary>
    /// <remarks>接收安装来源和活动归因变化，复用统计分发流程上报归因事件及收入。</remarks>
    public sealed class AttributionManager : IDisposable
    {
        private readonly AnalyticsManager client;
        private readonly IAttributionBackend backend;
        private bool disposed;
        public event Action<AttributionSnapshot> AttributionChanged;

        /// <summary>绑定一个归因后端，并订阅它的安装来源变化事件。</summary>
        public AttributionManager(IAttributionBackend backend, bool ownsBackend = false)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            client = new AnalyticsManager(new[] { backend }, ownsBackend);
            backend.AttributionChanged += OnChanged;
        }

        /// <summary>启动已配置的归因后端，并传入初始采集开关。</summary>
        public TelemetryReport Start(bool collectionEnabled) => client.Start(collectionEnabled);
        /// <summary>切换归因采集状态，并返回后端的处理结果。</summary>
        public TelemetryReport SetCollectionEnabled(bool enabled) => client.SetCollectionEnabled(enabled);
        /// <summary>同步业务用户标识，供支持该能力的归因后端关联事件。</summary>
        public TelemetryReport SetUserId(string userId) => client.SetUserId(userId);
        /// <summary>向当前归因后端发送自定义事件和属性。</summary>
        public TelemetryReport LogEvent(string name, System.Collections.Generic.IReadOnlyDictionary<string, object> properties = null) => client.LogEvent(name, properties);
        /// <summary>向归因后端上报收入，供投放来源评估使用。</summary>
        public TelemetryReport LogRevenue(RevenueEvent revenue) => client.LogRevenue(revenue);

        // 将渠道归因变化通知各订阅者。
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

        /// <summary>释放归因分发流程并解除事件订阅，是否释放原生后端由构造参数决定。</summary>
        public void Dispose()
        {
            AnalyticsBackend.MainThread();
            if (disposed)
            {
                return;
            }
            // If called reentrantly, let AnalyticsManager reject disposal before removing the listener.
            client.Dispose();
            disposed = true;
            backend.AttributionChanged -= OnChanged;
            AttributionChanged = null;
        }
    }
}

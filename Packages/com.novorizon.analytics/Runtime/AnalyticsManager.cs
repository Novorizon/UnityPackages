using System;
using System.Collections.Generic;

namespace Game.Framework.Analytics
{
    /// <summary>Explicit, main-thread event router. Disabled events are discarded, never replayed later.</summary>
    /// <remarks>把统计事件分发给启动时注入的后端，并汇总各后端的返回状态。</remarks>
    public sealed class AnalyticsManager : IDisposable
    {
        private readonly List<IAnalyticsBackend> backends = new List<IAnalyticsBackend>();
        private readonly bool ownsBackends;
        private IReadOnlyDictionary<string, object> superProperties = AnalyticsValues.Copy(null);
        private bool startRequested, enabled, disposed, dispatching;
        private string userId;
        public bool CollectionEnabled => enabled;

        /// <summary>添加具有唯一 ID 的统计后端。ownsBackends 为 true 时由管理器负责销毁后端。</summary>
        public AnalyticsManager(IEnumerable<IAnalyticsBackend> backends, bool ownsBackends = false)
        {
            this.ownsBackends = ownsBackends;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var backend in backends ?? throw new ArgumentNullException(nameof(backends)))
            {
                if (backend == null || string.IsNullOrWhiteSpace(backend.Id) || !names.Add(backend.Id))
                {
                    throw new ArgumentException("Provide non-null backends with unique IDs.", nameof(backends));
                }
                this.backends.Add(backend);
            }
        }

        /// <summary>开始管理采集状态。初始关闭时，尚未启动的后端会等到采集开启再初始化。</summary>
        public TelemetryReport Start(bool collectionEnabled)
        {
            Ready();
            startRequested = true;
            return SetCollectionEnabled(collectionEnabled);
        }

        /// <summary>切换采集状态，并分别记录各后端应用开关的结果。</summary>
        public TelemetryReport SetCollectionEnabled(bool value)
        {
            Ready();
            enabled = value;
            return Dispatch(backend =>
            {
                if (!startRequested || (!value && !backend.IsStarted))
                {
                    return TelemetryStatus.Disabled;
                }
                if (!backend.IsStarted)
                {
                    backend.Initialize(userId);
                }
                else
                {
                    // Stage the current account before resuming subsequent manual events.
                    backend.SetUserId(userId);
                    backend.SetCollectionEnabled(value);
                }
                return TelemetryStatus.AcceptedBySdk;
            });
        }

        /// <summary>更新统计用户 ID。未启用的后端会在下次启动或恢复采集时收到当前 ID。</summary>
        public TelemetryReport SetUserId(string value)
        {
            Ready();
            userId = AnalyticsValues.UserId(value);
            return Dispatch(backend =>
            {
                if (!CanSend(backend, out var status))
                {
                    return status;
                }
                backend.SetUserId(userId);
                return TelemetryStatus.AcceptedBySdk;
            });
        }

        /// <summary>Attached to manual events only; event-specific values override these defaults.</summary>
        /// <remarks>复制手工上报事件的公共属性。单次事件提供同名属性时以该次事件值为准。</remarks>
        public void SetSuperProperties(IReadOnlyDictionary<string, object> properties)
        {
            Ready();
            superProperties = AnalyticsValues.Copy(properties);
        }

        /// <summary>合并公共属性和单次事件属性，再分发给允许上报的后端。</summary>
        public TelemetryReport LogEvent(string name, IReadOnlyDictionary<string, object> properties = null)
        {
            Ready();
            AnalyticsValues.Name(name);
            var merged = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in superProperties)
            {
                merged.Add(pair.Key, pair.Value);
            }
            foreach (var pair in AnalyticsValues.Copy(properties))
            {
                merged[pair.Key] = pair.Value;
            }
            var snapshot = AnalyticsValues.Copy(merged);
            return Dispatch(backend =>
            {
                if (!CanSend(backend, out var status))
                {
                    return status;
                }
                backend.LogEvent(name, snapshot);
                return TelemetryStatus.AcceptedBySdk;
            });
        }

        /// <summary>把属性修改发给支持该操作的后端，并报告不支持该操作的后端。</summary>
        public TelemetryReport ApplyUserProperties(UserPropertyChange change)
        {
            Ready();
            if (change == null)
            {
                throw new ArgumentNullException(nameof(change));
            }
            return Dispatch(backend =>
            {
                if (!CanSend(backend, out var status))
                {
                    return status;
                }
                if (!backend.Supports(change.Operation))
                {
                    return TelemetryStatus.Unsupported;
                }
                backend.ApplyUserProperties(change);
                return TelemetryStatus.AcceptedBySdk;
            });
        }

        /// <summary>请求已启用且支持主动刷新的后端提交本地缓冲事件。</summary>
        public TelemetryReport Flush()
        {
            Ready();
            return Dispatch(backend =>
            {
                if (!CanSend(backend, out var status))
                {
                    return status;
                }
                if (!backend.CanFlush)
                {
                    return TelemetryStatus.Unsupported;
                }
                backend.Flush();
                return TelemetryStatus.AcceptedBySdk;
            });
        }

        /// <summary>将收入事件发给支持收入接口且允许采集的后端。</summary>
        public TelemetryReport LogRevenue(RevenueEvent revenue)
        {
            Ready();
            if (revenue == null)
            {
                throw new ArgumentNullException(nameof(revenue));
            }
            return Dispatch(backend =>
            {
                if (!CanSend(backend, out var status))
                {
                    return status;
                }
                if (!(backend is IRevenueAnalytics target))
                {
                    return TelemetryStatus.Unsupported;
                }
                target.LogRevenue(revenue);
                return TelemetryStatus.AcceptedBySdk;
            });
        }

        // 检查采集开关和后端启动状态，并为跳过的事件给出结果。
        private bool CanSend(IAnalyticsBackend backend, out TelemetryStatus status)
        {
            status = !enabled ? TelemetryStatus.Disabled : TelemetryStatus.NotStarted;
            return startRequested && enabled && backend.IsStarted;
        }

        // 逐个后端执行并收集结果，单个后端失败时其余后端也能收到事件。
        private TelemetryReport Dispatch(Func<IAnalyticsBackend, TelemetryStatus> action)
        {
            var results = new List<TelemetryDelivery>(backends.Count);
            dispatching = true;
            try
            {
                foreach (var backend in backends)
                {
                    try
                    {
                        results.Add(new TelemetryDelivery(backend.Id, action(backend)));
                    }
                    catch (TelemetryUnsupportedException error)
                    {
                        results.Add(new TelemetryDelivery(backend.Id, TelemetryStatus.Unsupported, error));
                    }
                    catch (Exception error)
                    {
                        results.Add(new TelemetryDelivery(backend.Id, TelemetryStatus.Failed, error));
                    }
                }
            }
            finally
            {
                dispatching = false;
            }
            return new TelemetryReport(results);
        }

        // 检查调用线程与状态，防止同一个管理器在分发回调里被重入调用。
        private void Ready()
        {
            AnalyticsBackend.MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AnalyticsManager));
            }
            if (dispatching)
            {
                throw new InvalidOperationException("Do not reenter telemetry while a backend is dispatching.");
            }
        }

        /// <summary>结束统计管理器生命周期，只释放由它持有的后端。</summary>
        public void Dispose()
        {
            AnalyticsBackend.MainThread();
            if (disposed)
            {
                return;
            }
            Ready();
            disposed = true;
            if (!ownsBackends)
            {
                return;
            }
            var errors = new List<Exception>();
            foreach (var backend in backends)
            {
                try
                {
                    backend.Dispose();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            if (errors.Count != 0)
            {
                throw new AggregateException("Telemetry cleanup failed.", errors);
            }
        }
    }
}

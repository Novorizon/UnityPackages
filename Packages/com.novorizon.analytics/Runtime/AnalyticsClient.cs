using System;
using System.Collections.Generic;

namespace Game.Framework.Analytics
{
    /// <summary>Explicit, main-thread event router. Disabled events are discarded, never replayed later.</summary>
    public class AnalyticsClient : IDisposable
    {
        private readonly List<IAnalyticsBackend> backends = new List<IAnalyticsBackend>();
        private readonly bool ownsBackends;
        private IReadOnlyDictionary<string, object> superProperties = AnalyticsValues.Copy(null);
        private bool startRequested, enabled, disposed, dispatching;
        private string userId;
        public bool CollectionEnabled => enabled;

        public AnalyticsClient(IEnumerable<IAnalyticsBackend> backends, bool ownsBackends = false)
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

        public TelemetryReport Start(bool collectionEnabled)
        {
            Ready();
            startRequested = true;
            return SetCollectionEnabled(collectionEnabled);
        }

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
        public void SetSuperProperties(IReadOnlyDictionary<string, object> properties)
        {
            Ready();
            superProperties = AnalyticsValues.Copy(properties);
        }

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

        private bool CanSend(IAnalyticsBackend backend, out TelemetryStatus status)
        {
            status = !enabled ? TelemetryStatus.Disabled : TelemetryStatus.NotStarted;
            return startRequested && enabled && backend.IsStarted;
        }

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

        private void Ready()
        {
            AnalyticsBackend.MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AnalyticsClient));
            }
            if (dispatching)
            {
                throw new InvalidOperationException("Do not reenter telemetry while a backend is dispatching.");
            }
        }

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

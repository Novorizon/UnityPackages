using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.Framework.Analytics
{
    /// <summary>One explicit owner per SDK. Does not destroy a vendor's global objects or default app.</summary>
    public abstract class AnalyticsBackend : IAnalyticsBackend
    {
        private static readonly Dictionary<string, AnalyticsBackend> owners = new Dictionary<string, AnalyticsBackend>(StringComparer.Ordinal);
        private bool attempted, disposed, enabled;
        private string userId;
        private long collectionEpoch;
        public abstract string Id { get; }
        public bool IsStarted { get; private set; }
        protected bool IsDisposed => disposed;
        protected bool CollectionEnabled => enabled;
        protected long CollectionEpoch => Interlocked.Read(ref collectionEpoch);
        public virtual bool CanFlush => false;

        public void Initialize(string initialUserId)
        {
            Ready();
            AnalyticsValues.UserId(initialUserId);
            if (IsStarted)
            {
                return;
            }
            if (attempted)
            {
                throw new InvalidOperationException("Dispose and recreate a backend whose SDK startup failed.");
            }
            if (owners.TryGetValue(Id, out var current) && !ReferenceEquals(current, this))
            {
                throw new InvalidOperationException("Another backend owns this SDK.");
            }
            owners[Id] = this;
            attempted = true;
            userId = initialUserId;
            OnInitialize(initialUserId);
            IsStarted = true;
            enabled = true;
        }

        public void SetCollectionEnabled(bool value)
        {
            Ready(true);
            if (value != enabled)
            {
                Interlocked.Increment(ref collectionEpoch);
            }
            OnSetCollectionEnabled(value);
            enabled = value;
            if (value)
            {
                OnSetUserId(userId);
            }
        }

        public void SetUserId(string value)
        {
            Ready(true);
            userId = AnalyticsValues.UserId(value);
            if (enabled)
            {
                OnSetUserId(value);
            }
        }

        public void LogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
            Ready(true);
            if (!enabled)
            {
                throw new InvalidOperationException("Telemetry collection is disabled.");
            }
            AnalyticsValues.Name(name);
            OnLogEvent(name, AnalyticsValues.Copy(properties));
        }

        public virtual bool Supports(UserPropertyOperation operation) => false;

        public void ApplyUserProperties(UserPropertyChange change)
        {
            Ready(true);
            if (!enabled || change == null || !Supports(change.Operation))
            {
                throw new NotSupportedException("This user property operation is disabled or unsupported.");
            }
            OnApplyUserProperties(change);
        }

        public void Flush()
        {
            Ready(true);
            if (!enabled || !CanFlush)
            {
                throw new NotSupportedException("Flushing is disabled or unsupported.");
            }
            OnFlush();
        }

        protected abstract void OnInitialize(string initialUserId);
        protected abstract void OnSetCollectionEnabled(bool enabled);
        protected abstract void OnSetUserId(string userId);
        protected abstract void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties);
        protected virtual void OnApplyUserProperties(UserPropertyChange change) => throw new NotSupportedException();
        protected virtual void OnFlush() => throw new NotSupportedException();
        protected virtual void OnDispose()
        {
        }
        protected virtual void OnShutdown() => OnSetCollectionEnabled(false);

        protected void Ready(bool requireStarted = false)
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
            if (requireStarted && !IsStarted)
            {
                throw new InvalidOperationException("Initialize the SDK explicitly first.");
            }
        }

        public static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use telemetry on the initialized Unity main thread.");
            }
        }

        public void Dispose()
        {
            MainThread();
            if (disposed)
            {
                return;
            }
            disposed = true;
            Interlocked.Increment(ref collectionEpoch);
            try
            {
                if (IsStarted)
                {
                    OnShutdown();
                }
            }
            finally
            {
                try
                {
                    OnDispose();
                }
                finally
                {
                    if (owners.TryGetValue(Id, out var current) && ReferenceEquals(current, this))
                    {
                        owners.Remove(Id);
                    }
                }
            }
        }
    }
}

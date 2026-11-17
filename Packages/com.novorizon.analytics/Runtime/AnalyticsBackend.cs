using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.Framework.Analytics
{
    /// <summary>One explicit owner per SDK. Does not destroy a vendor's global objects or default app.</summary>
    /// <remarks>统计对应的后端。承接上层请求，调用具体平台或实现。</remarks>
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

        /// <summary>初始化统计后端。</summary>
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

        /// <summary>设置采集开启。</summary>
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

        /// <summary>设置用户标识。</summary>
        public void SetUserId(string value)
        {
            Ready(true);
            userId = AnalyticsValues.UserId(value);
            if (enabled)
            {
                OnSetUserId(value);
            }
        }

        /// <summary>记录事件。</summary>
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

        /// <summary>检查是否支持统计后端。</summary>
        public virtual bool Supports(UserPropertyOperation operation) => false;

        /// <summary>应用用户属性。</summary>
        public void ApplyUserProperties(UserPropertyChange change)
        {
            Ready(true);
            if (!enabled || change == null || !Supports(change.Operation))
            {
                throw new NotSupportedException("This user property operation is disabled or unsupported.");
            }
            OnApplyUserProperties(change);
        }

        /// <summary>刷新缓冲区统计后端。</summary>
        public void Flush()
        {
            Ready(true);
            if (!enabled || !CanFlush)
            {
                throw new NotSupportedException("Flushing is disabled or unsupported.");
            }
            OnFlush();
        }

        /// <summary>响应 Initialize。</summary>
        protected abstract void OnInitialize(string initialUserId);
        /// <summary>响应集合采集开启。</summary>
        protected abstract void OnSetCollectionEnabled(bool enabled);
        /// <summary>响应集合用户标识。</summary>
        protected abstract void OnSetUserId(string userId);
        /// <summary>响应 Log 事件。</summary>
        protected abstract void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties);
        /// <summary>响应 Apply 用户属性。</summary>
        protected virtual void OnApplyUserProperties(UserPropertyChange change) => throw new NotSupportedException();
        /// <summary>响应 Flush。</summary>
        protected virtual void OnFlush() => throw new NotSupportedException();
        /// <summary>响应 Dispose。</summary>
        protected virtual void OnDispose()
        {
        }
        /// <summary>响应 Shutdown。</summary>
        protected virtual void OnShutdown() => OnSetCollectionEnabled(false);

        /// <summary>调用 MainThread 完成就绪。</summary>
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

        /// <summary>校验当前调用位于 Unity 主线程。</summary>
        public static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use telemetry on the initialized Unity main thread.");
            }
        }

        /// <summary>结束统计后端对相关资源的持有，并执行该类型的清理流程。</summary>
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

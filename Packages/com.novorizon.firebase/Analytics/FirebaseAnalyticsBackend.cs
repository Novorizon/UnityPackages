using System;
using System.Collections.Generic;
using Game.Framework.Analytics;
using AnalyticsSdk = global::Firebase.Analytics.FirebaseAnalytics;

namespace Game.Framework.Firebase.Unity
{
    /// <summary>Optional bridge to AnalyticsManager; the original Firebase IAnalytics API remains available.</summary>
    /// <remarks>Firebase 统计对应的后端。承接上层请求，调用具体平台或实现。</remarks>
    public sealed class FirebaseAnalyticsBackend : AnalyticsBackend, IRevenueAnalytics
    {
        private readonly FirebaseManager runtime;
        private FirebaseAnalyticsAdapter adapter;
        public override string Id => "firebase-analytics";

        /// <summary>使用调用方提供的runtime创建 FirebaseAnalyticsBackend。</summary>
        public FirebaseAnalyticsBackend(FirebaseManager runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <summary>响应 Initialize。</summary>
        protected override void OnInitialize(string initialUserId)
        {
            adapter = new FirebaseAnalyticsAdapter(runtime);
            adapter.SetCollectionEnabled(true);
            adapter.SetUserId(initialUserId);
        }

        /// <summary>响应集合采集开启。</summary>
        protected override void OnSetCollectionEnabled(bool enabled) => adapter.SetCollectionEnabled(enabled);
        /// <summary>响应集合用户标识。</summary>
        protected override void OnSetUserId(string userId) => adapter.SetUserId(userId);

        /// <summary>响应 Log 事件。</summary>
        protected override void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
            if (name.Length > 40 || Reserved(name) || properties.Count > 25)
            {
                throw new ArgumentException("Firebase supports non-reserved event names up to 40 characters and 25 parameters.");
            }
            foreach (var pair in properties)
            {
                if (pair.Key.Length > 40 || Reserved(pair.Key) || (pair.Value is string text && text.Length > 100))
                {
                    throw new ArgumentException("Firebase parameter names/strings exceed supported limits or use a reserved prefix.");
                }
            }
            adapter.LogEvent(name, properties);
        }

        /// <summary>返回Reserved的判断结果。</summary>
        private static bool Reserved(string name) => name.StartsWith("firebase_", StringComparison.Ordinal) || name.StartsWith("google_", StringComparison.Ordinal) || name.StartsWith("ga_", StringComparison.Ordinal);
        /// <summary>检查是否支持 Firebase 统计后端。</summary>
        public override bool Supports(UserPropertyOperation operation) => operation == UserPropertyOperation.Set || operation == UserPropertyOperation.Unset;

        /// <summary>响应 Apply 用户属性。</summary>
        protected override void OnApplyUserProperties(UserPropertyChange change)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in change.Properties)
            {
                string value = change.Operation == UserPropertyOperation.Unset ? null : AnalyticsValues.Invariant(pair.Value);
                if (pair.Key.Length > 24 || Reserved(pair.Key) || (value != null && value.Length > 36))
                {
                    throw new ArgumentException("Firebase user properties support non-reserved names up to 24 and values up to 36 characters.");
                }
                values.Add(pair.Key, value);
            }
            // Validate the complete batch before changing any native properties.
            foreach (var pair in values)
            {
                AnalyticsSdk.SetUserProperty(pair.Key, pair.Value);
            }
        }

        /// <summary>记录收入。</summary>
        public void LogRevenue(RevenueEvent revenue)
        {
            Ready(true);
            if (!CollectionEnabled || revenue == null)
            {
                throw new InvalidOperationException("Revenue reporting needs an enabled backend and a revenue event.");
            }
            var values = new Dictionary<string, object>(revenue.Properties)
            {
                ["value"] = (double)revenue.Amount,
                ["currency"] = revenue.Currency,
                ["transaction_id"] = revenue.TransactionId
            };
            OnLogEvent(revenue.EventName, values);
        }
    }
}

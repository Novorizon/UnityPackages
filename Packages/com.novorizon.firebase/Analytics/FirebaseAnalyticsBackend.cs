using System;
using System.Collections.Generic;
using Game.Framework.Analytics;
using AnalyticsSdk = global::Firebase.Analytics.FirebaseAnalytics;

namespace Game.Framework.Firebase.Unity
{
    /// <summary>Optional bridge to AnalyticsManager; the original Firebase IAnalytics API remains available.</summary>
    public sealed class FirebaseAnalyticsBackend : AnalyticsBackend, IRevenueAnalytics
    {
        private readonly FirebaseRuntime runtime;
        private FirebaseAnalyticsAdapter adapter;
        public override string Id => "firebase-analytics";

        public FirebaseAnalyticsBackend(FirebaseRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        protected override void OnInitialize(string initialUserId)
        {
            adapter = new FirebaseAnalyticsAdapter(runtime);
            adapter.SetCollectionEnabled(true);
            adapter.SetUserId(initialUserId);
        }

        protected override void OnSetCollectionEnabled(bool enabled) => adapter.SetCollectionEnabled(enabled);
        protected override void OnSetUserId(string userId) => adapter.SetUserId(userId);

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

        private static bool Reserved(string name) => name.StartsWith("firebase_", StringComparison.Ordinal) || name.StartsWith("google_", StringComparison.Ordinal) || name.StartsWith("ga_", StringComparison.Ordinal);
        public override bool Supports(UserPropertyOperation operation) => operation == UserPropertyOperation.Set || operation == UserPropertyOperation.Unset;

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

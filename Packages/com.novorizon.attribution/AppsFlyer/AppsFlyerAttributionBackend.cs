using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Framework.Analytics;
using UnityEngine;
using AfSdk = AppsFlyerSDK.AppsFlyer;
using Object = UnityEngine.Object;

namespace Game.Framework.Attribution.AppsFlyer
{
    /// <summary>AppsFlyer 6.18.x. v7 requires a newer Unity baseline and is deliberately excluded.</summary>
    public sealed class AppsFlyerAttributionBackend : AttributionBackend
    {
        private readonly string devKey, iosAppId;
        private readonly bool advertisingIdentifiers;
        private readonly Dictionary<string, string> eventNames;
        private AppsFlyerCallbackHost host;
        public override string Id => "appsflyer";

        public AppsFlyerAttributionBackend(string devKey, string iosAppId = null, IReadOnlyDictionary<string, string> eventNames = null, bool collectAdvertisingIdentifiers = false)
        {
            if (string.IsNullOrWhiteSpace(devKey))
            {
                throw new ArgumentException("Configure the AppsFlyer client dev key.", nameof(devKey));
            }
            this.devKey = devKey;
            this.iosAppId = iosAppId;
            this.eventNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in eventNames ?? new Dictionary<string, string>())
            {
                AnalyticsValues.Name(pair.Key);
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    throw new ArgumentException("AppsFlyer event mappings need a native event name.", nameof(eventNames));
                }
                this.eventNames.Add(pair.Key, pair.Value);
            }
            advertisingIdentifiers = collectAdvertisingIdentifiers;
        }

        protected override void OnInitialize(string initialUserId)
        {
            if (Application.isEditor || (Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer))
            {
                throw new PlatformNotSupportedException("AppsFlyer attribution requires an Android or iOS player.");
            }
            if (Application.platform == RuntimePlatform.IPhonePlayer && string.IsNullOrWhiteSpace(iosAppId))
            {
                throw new InvalidOperationException("Configure the numeric Apple App Store ID for AppsFlyer iOS.");
            }
            var root = new GameObject("NovorizonAppsFlyer_" + Guid.NewGuid().ToString("N"));
            Object.DontDestroyOnLoad(root);
            host = root.AddComponent<AppsFlyerCallbackHost>();
            host.Received = Receive;
            AfSdk.initSDK(devKey, iosAppId, host);
            AfSdk.setIsDebug(false);
            AfSdk.setCollectAndroidID(false);
            AfSdk.setCollectIMEI(false);
            AfSdk.setDisableAdvertisingIdentifiers(!advertisingIdentifiers);
            AfSdk.setDisableCollectAppleAdSupport(!advertisingIdentifiers);
            OnSetUserId(initialUserId);
            AfSdk.stopSDK(false);
            AfSdk.startSDK();
        }

        protected override void OnSetCollectionEnabled(bool enabled)
        {
            AfSdk.stopSDK(!enabled);
            if (enabled)
            {
                AfSdk.startSDK();
            }
        }

        protected override void OnSetUserId(string userId) => AfSdk.setCustomerUserId(userId ?? "");

        protected override void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
            AfSdk.sendEvent(NativeName(name), Strings(properties));
        }

        private string NativeName(string name) => eventNames.TryGetValue(name, out var value) ? value : name;

        internal static Dictionary<string, string> Strings(IReadOnlyDictionary<string, object> properties)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in properties)
            {
                result.Add(pair.Key, AnalyticsValues.Invariant(pair.Value));
            }
            return result;
        }

        protected override void OnLogRevenue(RevenueEvent revenue)
        {
            AfSdk.sendEvent(NativeName(revenue.EventName), RevenueValues(revenue));
        }

        internal static Dictionary<string, string> RevenueValues(RevenueEvent revenue)
        {
            var values = Strings(revenue.Properties);
            values["af_revenue"] = revenue.Amount.ToString(CultureInfo.InvariantCulture);
            values["af_currency"] = revenue.Currency;
            values["af_order_id"] = revenue.TransactionId;
            return values;
        }

        private void Receive(string json)
        {
            if (IsDisposed || !CollectionEnabled)
            {
                return;
            }
            Dictionary<string, object> values;
            try
            {
                values = AfSdk.CallbackStringToDictionary(json);
            }
            catch (Exception)
            {
                return;
            }
            if (values == null)
            {
                return;
            }
            string status = Text(values, "af_status");
            bool? organic = string.Equals(status, "Organic", StringComparison.OrdinalIgnoreCase) ? true : string.Equals(status, "Non-organic", StringComparison.OrdinalIgnoreCase) ? false : (bool?)null;
            Publish(new AttributionSnapshot(Id, Text(values, "media_source"), Text(values, "campaign"), Text(values, "af_adset"), Text(values, "af_ad"), organic));
        }

        private static string Text(Dictionary<string, object> values, string key) => values.TryGetValue(key, out var value) ? value as string : null;

        protected override void OnDispose()
        {
            base.OnDispose();
            if (host != null)
            {
                host.Received = null;
                Object.Destroy(host.gameObject);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using AdjustSdk;
using Game.Framework.Analytics;
using UnityEngine;
using AdjustApi = AdjustSdk.Adjust;

namespace Game.Framework.Attribution.Adjust
{
    /// <summary>Adjust 归因对应的后端。承接上层请求，调用具体平台或实现。</summary>
    public sealed class AdjustAttributionBackend : AttributionBackend
    {
        private readonly string appToken;
        private readonly bool sandbox, advertisingIdentifiers;
        private readonly Dictionary<string, string> eventTokens;
        public override string Id => "adjust";

        /// <summary>使用调用方提供的appToken、eventTokens、sandbox创建 AdjustAttributionBackend。</summary>
        public AdjustAttributionBackend(string appToken, IReadOnlyDictionary<string, string> eventTokens, bool sandbox = true, bool collectAdvertisingIdentifiers = false)
        {
            if (string.IsNullOrWhiteSpace(appToken))
            {
                throw new ArgumentException("Configure the Adjust app token.", nameof(appToken));
            }
            this.appToken = appToken;
            this.sandbox = sandbox;
            advertisingIdentifiers = collectAdvertisingIdentifiers;
            this.eventTokens = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in eventTokens ?? throw new ArgumentNullException(nameof(eventTokens)))
            {
                AnalyticsValues.Name(pair.Key);
                if (string.IsNullOrWhiteSpace(pair.Value))
                {
                    throw new ArgumentException("Map logical events to tokens from the Adjust dashboard.", nameof(eventTokens));
                }
                this.eventTokens.Add(pair.Key, pair.Value);
            }
        }

        /// <summary>响应 Initialize。</summary>
        protected override void OnInitialize(string initialUserId)
        {
            if (Application.isEditor || (Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer))
            {
                throw new PlatformNotSupportedException("Adjust requires an Android or iOS player.");
            }
            var config = new AdjustConfig(appToken, sandbox ? AdjustEnvironment.Sandbox : AdjustEnvironment.Production, true)
            {
                LogLevel = AdjustLogLevel.Suppress,
                IsIdfaReadingEnabled = advertisingIdentifiers,
                IsGoogleAdIdReadingEnabled = advertisingIdentifiers,
                IsAndroidIdReadingEnabled = false,
                IsFbIdReadingEnabled = false,
                IsLinkMeEnabled = false,
                IsDeferredDeeplinkOpeningEnabled = false,
                AttributionChangedDelegate = OnAttribution
            };
            // A game account is a mutable callback parameter, never Adjust's persistent device identity.
            OnSetUserId(initialUserId);
            AdjustApi.InitSdk(config);
            AdjustApi.Enable();
        }

        /// <summary>响应集合采集开启。</summary>
        protected override void OnSetCollectionEnabled(bool enabled)
        {
            if (enabled)
            {
                AdjustApi.Enable();
            }
            else
            {
                AdjustApi.Disable();
            }
        }

        /// <summary>响应集合用户标识。</summary>
        protected override void OnSetUserId(string userId)
        {
            AdjustApi.RemoveGlobalCallbackParameter("game_user_id");
            if (userId != null)
            {
                AdjustApi.AddGlobalCallbackParameter("game_user_id", userId);
            }
        }

        /// <summary>响应 Log 事件。</summary>
        protected override void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
            var value = Event(name, properties);
            AdjustApi.TrackEvent(value);
        }

        /// <summary>根据传入参数取得事件。</summary>
        private AdjustEvent Event(string name, IReadOnlyDictionary<string, object> properties)
        {
            if (!eventTokens.TryGetValue(name, out var token))
            {
                throw new TelemetryUnsupportedException("This logical event has no Adjust event-token mapping.");
            }
            var value = new AdjustEvent(token);
            foreach (var pair in properties)
            {
                if (pair.Key == "game_user_id")
                {
                    throw new ArgumentException("game_user_id is owned by SetUserId.", nameof(properties));
                }
                value.AddCallbackParameter(pair.Key, AnalyticsValues.Invariant(pair.Value));
            }
            return value;
        }

        /// <summary>响应 Log 收入。</summary>
        protected override void OnLogRevenue(RevenueEvent revenue)
        {
            AdjustApi.TrackEvent(BuildRevenueEvent(revenue));
        }

        /// <summary>构建收入事件。</summary>
        internal AdjustEvent BuildRevenueEvent(RevenueEvent revenue)
        {
            var value = Event(revenue.EventName, revenue.Properties);
            value.SetRevenue((double)revenue.Amount, revenue.Currency);
            value.DeduplicationId = revenue.TransactionId;
            return value;
        }

        /// <summary>响应归因。</summary>
        private void OnAttribution(AdjustAttribution value)
        {
            if (value != null)
            {
                bool? organic = string.Equals(value.Network, "Organic", StringComparison.OrdinalIgnoreCase) ? true : string.IsNullOrEmpty(value.Network) ? (bool?)null : false;
                Publish(new AttributionSnapshot(Id, value.Network, value.Campaign, value.Adgroup, value.Creative, organic));
            }
        }
    }
}

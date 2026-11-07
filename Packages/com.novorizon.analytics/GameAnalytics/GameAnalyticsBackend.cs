using System;
using System.Collections.Generic;
using GameAnalyticsSDK;
using UnityEngine;
using GaSdk = GameAnalyticsSDK.GameAnalytics;
using Object = UnityEngine.Object;

namespace Game.Framework.Analytics.GameAnalytics
{
    public enum ProgressionStatus
    {
        Start,
        Complete,
        Fail
    }

    public enum ResourceFlow
    {
        Source,
        Sink
    }

    /// <summary>GameAnalytics 8.3. Configure official SDK Settings keys/build/resource lists in the project.</summary>
    public sealed class GameAnalyticsBackend : AnalyticsBackend
    {
        private GameObject ownedRoot;
        public override string Id => "gameanalytics";

        protected override void OnInitialize(string initialUserId)
        {
            if (!Application.isPlaying)
            {
                throw new InvalidOperationException("Initialize GameAnalytics in a running player.");
            }
            var settings = GaSdk.SettingsGA;
            if (settings == null)
            {
                throw new InvalidOperationException("Create and configure the official GameAnalytics Settings asset first.");
            }
            int index = settings.Platforms.IndexOf(Application.platform);
            if (index < 0 || string.IsNullOrWhiteSpace(settings.GetGameKey(index)) || string.IsNullOrWhiteSpace(settings.GetSecretKey(index)))
            {
                throw new InvalidOperationException("Configure GameAnalytics keys for the current runtime platform. Editor is not a mobile player.");
            }
            if (Object.FindObjectOfType<GaSdk>() == null)
            {
                ownedRoot = new GameObject("NovorizonGameAnalytics");
                Object.DontDestroyOnLoad(ownedRoot);
                ownedRoot.AddComponent<GaSdk>();
            }
            if (!GaSdk.Initialized)
            {
                GaSdk.Initialize();
            }
            GaSdk.SetEnabledEventSubmission(true, false);
            OnSetUserId(initialUserId);
        }

        protected override void OnSetCollectionEnabled(bool enabled) => GaSdk.SetEnabledEventSubmission(enabled, false);
        protected override void OnSetUserId(string userId) => GaSdk.SetExternalUserId(userId ?? "");

        protected override void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
            GaSdk.NewDesignEvent(name, new Dictionary<string, object>(properties));
        }

        public void LogProgression(ProgressionStatus status, string progression, string level, string phase = null, int? score = null)
        {
            Ready(true);
            if (!CollectionEnabled)
            {
                return;
            }
            if (!Enum.IsDefined(typeof(ProgressionStatus), status) || string.IsNullOrWhiteSpace(progression) || string.IsNullOrWhiteSpace(level))
            {
                throw new ArgumentException("Provide a valid progression state and stable progression/level IDs.");
            }
            var sdkStatus = status == ProgressionStatus.Start ? GAProgressionStatus.Start : status == ProgressionStatus.Complete ? GAProgressionStatus.Complete : GAProgressionStatus.Fail;
            if (phase == null)
            {
                if (score.HasValue)
                {
                    GaSdk.NewProgressionEvent(sdkStatus, progression, level, score.Value);
                }
                else
                {
                    GaSdk.NewProgressionEvent(sdkStatus, progression, level);
                }
            }
            else if (score.HasValue)
            {
                GaSdk.NewProgressionEvent(sdkStatus, progression, level, phase, score.Value);
            }
            else
            {
                GaSdk.NewProgressionEvent(sdkStatus, progression, level, phase);
            }
        }

        public void LogResource(ResourceFlow flow, string currency, float amount, string itemType, string itemId)
        {
            Ready(true);
            if (!CollectionEnabled)
            {
                return;
            }
            if (!Enum.IsDefined(typeof(ResourceFlow), flow) || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0 ||
                string.IsNullOrWhiteSpace(currency) || string.IsNullOrWhiteSpace(itemType) || string.IsNullOrWhiteSpace(itemId))
            {
                throw new ArgumentException("Resource events need configured currency/item type, item ID and a positive finite amount.");
            }
            GaSdk.NewResourceEvent(flow == ResourceFlow.Source ? GAResourceFlowType.Source : GAResourceFlowType.Sink, currency, amount, itemType, itemId);
        }

        /// <summary>GameAnalytics expects integer cents; report only after the game backend verifies fulfillment.</summary>
        public void LogBusiness(string currency, int amountInCents, string itemType, string itemId, string cartType)
        {
            Ready(true);
            if (!CollectionEnabled)
            {
                return;
            }
            if (currency == null || currency.Length != 3 || amountInCents < 0 || string.IsNullOrWhiteSpace(itemType) || string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(cartType))
            {
                throw new ArgumentException("Business events need currency, integer cents, item type/ID and cart type.");
            }
            GaSdk.NewBusinessEvent(currency, amountInCents, itemType, itemId, cartType);
        }

        protected override void OnDispose()
        {
            if (ownedRoot != null)
            {
                Object.Destroy(ownedRoot);
            }
        }
    }
}

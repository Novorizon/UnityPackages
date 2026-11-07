using System;
using System.Collections.Generic;
using ThinkingData.Analytics;
using ThinkingData.Analytics.Utils;
using UnityEngine;
using TdSdk = ThinkingData.Analytics.TDAnalytics;
using Object = UnityEngine.Object;

namespace Game.Framework.Analytics.ThinkingData
{
    public sealed class ThinkingDataAnalyticsBackend : AnalyticsBackend
    {
        private readonly string appId, serverUrl;
        private readonly bool autoTrackLifecycle;
        private GameObject ownedRoot;
        public override string Id => "thinkingdata";
        public override bool CanFlush => true;

        public ThinkingDataAnalyticsBackend(string appId, string serverUrl, bool autoTrackLifecycle = false)
        {
            if (string.IsNullOrWhiteSpace(appId) || !Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException("ThinkingData needs an App ID and an absolute HTTPS receiver URL.");
            }
            this.appId = appId;
            this.serverUrl = serverUrl;
            this.autoTrackLifecycle = autoTrackLifecycle;
        }

        protected override void OnInitialize(string initialUserId)
        {
            var component = Object.FindObjectOfType<TdSdk>();
            if (component == null)
            {
                ownedRoot = new GameObject("NovorizonThinkingData");
                Object.DontDestroyOnLoad(ownedRoot);
                component = ownedRoot.AddComponent<TdSdk>();
            }
            if (!component.startManually)
            {
                throw new InvalidOperationException("Disable ThinkingData automatic startup; this backend owns explicit initialization.");
            }
            component.enableLog = false;
            TdSdk.EnableLog(false);
            TdSdk.Init(new[] { new TDConfig(appId, serverUrl) });
            TdSdk.SetTrackStatus(TDTrackStatus.Normal, appId);
            OnSetUserId(initialUserId);
            if (autoTrackLifecycle)
            {
                TdSdk.EnableAutoTrack(TDAutoTrackEventType.AppStart | TDAutoTrackEventType.AppEnd | TDAutoTrackEventType.AppInstall, appId: appId);
            }
        }

        protected override void OnSetCollectionEnabled(bool enabled)
        {
            // Pause/SaveOnly would retain events and replay them later. Stop clears SDK's unsent cache.
            TdSdk.SetTrackStatus(enabled ? TDTrackStatus.Normal : TDTrackStatus.Stop, appId);
        }

        protected override void OnSetUserId(string userId)
        {
            if (userId == null)
            {
                TdSdk.Logout(appId);
            }
            else
            {
                TdSdk.Login(userId, appId);
            }
        }

        protected override void OnLogEvent(string name, IReadOnlyDictionary<string, object> properties)
        {
            ValidateNames(name, properties);
            TdSdk.Track(name, Mutable(properties), appId);
        }
        public override bool Supports(UserPropertyOperation operation) => Enum.IsDefined(typeof(UserPropertyOperation), operation);

        protected override void OnApplyUserProperties(UserPropertyChange change)
        {
            ValidateNames(null, change.Properties);
            var values = Mutable(change.Properties);
            switch (change.Operation)
            {
                case UserPropertyOperation.Set:
                    TdSdk.UserSet(values, appId);
                    break;
                case UserPropertyOperation.SetOnce:
                    TdSdk.UserSetOnce(values, appId);
                    break;
                case UserPropertyOperation.Add:
                    TdSdk.UserAdd(values, appId);
                    break;
                case UserPropertyOperation.Unset:
                    TdSdk.UserUnset(new List<string>(values.Keys), appId);
                    break;
                case UserPropertyOperation.Append:
                    TdSdk.UserAppend(values, appId);
                    break;
                case UserPropertyOperation.AppendUnique:
                    TdSdk.UserUniqAppend(values, appId);
                    break;
                case UserPropertyOperation.DeleteProfile:
                    TdSdk.UserDelete(appId);
                    break;
            }
        }

        private static void ValidateNames(string eventName, IReadOnlyDictionary<string, object> properties)
        {
            if (eventName != null && eventName.Length > 50)
            {
                throw new ArgumentException("ThinkingData event names support at most 50 characters.");
            }
            foreach (var pair in properties)
            {
                if (pair.Key.Length > 50)
                {
                    throw new ArgumentException("ThinkingData property names support at most 50 characters.");
                }
            }
            if (!TDPropertiesChecker.CheckProperties(Mutable(properties)))
            {
                throw new ArgumentException("ThinkingData rejected the property value range or UTF-8 string length.");
            }
        }

        private static Dictionary<string, object> Mutable(IReadOnlyDictionary<string, object> source)
        {
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in source)
            {
                values.Add(pair.Key, pair.Value is IReadOnlyList<string> list ? new List<string>(list) : pair.Value);
            }
            return values;
        }

        public string GetDistinctId()
        {
            Ready(true);
            return TdSdk.GetDistinctId(appId);
        }

        protected override void OnFlush() => TdSdk.Flush(appId);
        // Normal wrapper shutdown must not erase accepted, unsent events. Explicit opt-out uses Stop.
        protected override void OnShutdown() => TdSdk.SetTrackStatus(TDTrackStatus.Pause, appId);

        protected override void OnDispose()
        {
            if (ownedRoot != null)
            {
                Object.Destroy(ownedRoot);
            }
        }
    }
}

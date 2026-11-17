using Game.Framework.Analytics.Editor;
using UnityEditor;

namespace Game.Framework.Attribution.Editor
{
    /// <summary>提供归因 SDK 配置的实现。</summary>
    public static class AttributionSdkSetup
    {
        /// <summary>调用 AnalyticsSdkSetup.EnableInstalled 完成AppsFlyer。</summary>
        [MenuItem("Novorizon/Analytics/Enable installed AppsFlyer 6.18")]
        public static void AppsFlyer() => AnalyticsSdkSetup.EnableInstalled("AppsFlyer", "NOVORIZON_APPSFLYER_6");

        /// <summary>调整归因 SDK 配置。</summary>
        [MenuItem("Novorizon/Analytics/Enable installed Adjust 5.8")]
        public static void Adjust() => AnalyticsSdkSetup.EnableInstalled("AdjustSdk.Scripts", "NOVORIZON_ADJUST_5");
    }
}

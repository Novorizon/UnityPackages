using Game.Framework.Analytics.Editor;
using UnityEditor;

namespace Game.Framework.Attribution.Editor
{
    public static class AttributionSdkSetup
    {
        [MenuItem("Novorizon/Analytics/Enable installed AppsFlyer 6.18")]
        public static void AppsFlyer() => AnalyticsSdkSetup.EnableInstalled("AppsFlyer", "NOVORIZON_APPSFLYER_6");

        [MenuItem("Novorizon/Analytics/Enable installed Adjust 5.8")]
        public static void Adjust() => AnalyticsSdkSetup.EnableInstalled("AdjustSdk.Scripts", "NOVORIZON_ADJUST_5");
    }
}

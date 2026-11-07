using System;
using System.Linq;
using UnityEditor;

namespace Game.Framework.Analytics.Editor
{
    public static class AnalyticsSdkSetup
    {
        [MenuItem("Novorizon/Analytics/Enable installed ThinkingData 3.5")]
        public static void ThinkingData() => EnableInstalled("ThinkingAnalytics", "NOVORIZON_THINKINGDATA");

        [MenuItem("Novorizon/Analytics/Enable installed GameAnalytics 8.3")]
        public static void GameAnalytics() => EnableInstalled("GameAnalyticsSDK", "NOVORIZON_GAMEANALYTICS");

        public static void EnableInstalled(string assembly, string symbol)
        {
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(value => value.GetName().Name == assembly))
            {
                throw new InvalidOperationException("Import the supported official SDK first: " + assembly);
            }
            var target = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (target == BuildTargetGroup.Unknown)
            {
                throw new InvalidOperationException("Select a build target first.");
            }
            var values = PlayerSettings.GetScriptingDefineSymbolsForGroup(target).Split(';').Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            if (!values.Contains(symbol))
            {
                values.Add(symbol);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(target, string.Join(";", values));
            }
        }
    }
}

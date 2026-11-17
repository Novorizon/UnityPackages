using System;
using System.Linq;
using UnityEditor;

namespace Game.Framework.Analytics.Editor
{
    /// <summary>提供统计 SDK 配置的实现。</summary>
    public static class AnalyticsSdkSetup
    {
        /// <summary>调用 EnableInstalled 完成思考数据。</summary>
        [MenuItem("Novorizon/Analytics/Enable installed ThinkingData 3.5")]
        public static void ThinkingData() => EnableInstalled("ThinkingAnalytics", "NOVORIZON_THINKINGDATA");

        /// <summary>调用 EnableInstalled 完成游戏统计。</summary>
        [MenuItem("Novorizon/Analytics/Enable installed GameAnalytics 8.3")]
        public static void GameAnalytics() => EnableInstalled("GameAnalyticsSDK", "NOVORIZON_GAMEANALYTICS");

        /// <summary>启用 Installed。</summary>
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

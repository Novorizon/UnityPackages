using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Firebase.Editor
{
    public static class FirebaseSdkSetup
    {
        [MenuItem("Novorizon/Platform SDKs/Synchronize installed Firebase SDKs")]
        public static void Synchronize()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group == BuildTargetGroup.Unknown)
            {
                throw new InvalidOperationException("Select a build target first.");
            }
            var products = new Dictionary<string, string>
            {
                { "APP", "App" },
                { "AUTH", "Auth" },
                { "ANALYTICS", "Analytics" },
                { "CRASHLYTICS", "Crashlytics" },
                { "REMOTE_CONFIG", "RemoteConfig" },
                { "MESSAGING", "Messaging" }
            };
            var files = new HashSet<string>(Directory.GetFiles(Application.dataPath, "Firebase.*.dll", SearchOption.AllDirectories).Select(Path.GetFileName), StringComparer.Ordinal);
            var symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';').Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            foreach (var product in products)
            {
                string symbol = "NOVORIZON_FIREBASE_" + product.Key;
                symbols.Remove(symbol);
                if (files.Contains("Firebase.App.dll") && files.Contains("Firebase." + product.Value + ".dll"))
                {
                    symbols.Add(symbol);
                }
            }
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", symbols));
            Debug.Log("Firebase adapter symbols synchronized for " + group + ". No native SDK or project credentials were downloaded.");
        }
    }
}

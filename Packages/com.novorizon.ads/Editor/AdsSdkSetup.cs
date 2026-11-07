using System;
using System.IO;
using System.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace Game.Framework.Ads.Editor
{
    public static class AdsSdkSetup
    {
        [MenuItem("Novorizon/Platform SDKs/Install domestic CSJ")]
        public static void InstallNative()
        {
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(item => item.GetName().Name == "Google.JarResolver" || item.GetName().Name == "Google.IOSResolver"))
            {
                throw new InvalidOperationException("Install Google's External Dependency Manager for Unity before installing native SDK dependencies.");
            }
            string source = Path.Combine(PackageInfo.FindForAssembly(typeof(AdsSdkSetup).Assembly).resolvedPath, "Native~");
            string target = "Assets/NovorizonPlatform/Ads";
            foreach (string path in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, path.Substring(source.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                // Preserve project edits. Review template diffs when upgrading this package.
                if (!File.Exists(destination))
                {
                    File.Copy(path, destination);
                }
            }
            AssetDatabase.Refresh();
            Enable("NOVORIZON_CSJ");
            Debug.Log("Native SDK bridge installed. Run External Dependency Manager resolution for your build target; see this package README.");
        }

        [MenuItem("Novorizon/Platform SDKs/Enable installed AppLovin MAX")]
        public static void EnableMax()
        {
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(item => item.GetName().Name == "MaxSdk.Scripts"))
            {
                throw new InvalidOperationException("Import the official AppLovin MAX Unity SDK first.");
            }
            Enable("NOVORIZON_MAX");
        }

        private static void Enable(string symbol)
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            if (group == BuildTargetGroup.Unknown)
            {
                throw new InvalidOperationException("Select an Android or iOS build target first.");
            }
            var symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(group).Split(';').Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            if (!symbols.Contains(symbol))
            {
                symbols.Add(symbol);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", symbols));
            }
        }
    }
}

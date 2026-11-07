using System;
using System.IO;
using System.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using UnityEngine;

namespace Game.Framework.Auth.Editor
{
    public static class IdentitySdkSetup
    {
        [MenuItem("Novorizon/Platform SDKs/Install native identity")]
        public static void InstallNative()
        {
            if (!AppDomain.CurrentDomain.GetAssemblies().Any(item => item.GetName().Name == "Google.JarResolver" || item.GetName().Name == "Google.IOSResolver"))
            {
                throw new InvalidOperationException("Install Google's External Dependency Manager for Unity before installing native SDK dependencies.");
            }
            string source = Path.Combine(PackageInfo.FindForAssembly(typeof(IdentitySdkSetup).Assembly).resolvedPath, "Native~");
            string target = "Assets/NovorizonPlatform/Auth";
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
            Enable("NOVORIZON_NATIVE_AUTH");
            Debug.Log("Native SDK bridge installed. Run External Dependency Manager resolution for your build target; see this package README.");
        }

        [MenuItem("Novorizon/Platform SDKs/Enable installed Facebook SDK")]
        public static void EnableFacebook()
        {
            if (!Directory.GetFiles(Application.dataPath, "Facebook.Unity.dll", SearchOption.AllDirectories).Any())
            {
                throw new InvalidOperationException("Import the official Facebook Unity SDK first.");
            }
            Enable("NOVORIZON_FACEBOOK");
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

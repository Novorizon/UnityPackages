#if UNITY_IOS && NOVORIZON_NATIVE_AUTH
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

namespace Game.Framework.Auth.Editor
{
    public static class IdentityIosBuild
    {
        [PostProcessBuild(90)]
        public static void Configure(BuildTarget target, string path)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }
            var settings = AssetDatabase.FindAssets("t:NativeIdentitySettings")
                .Select(id => AssetDatabase.LoadAssetAtPath<NativeIdentitySettings>(AssetDatabase.GUIDToAssetPath(id)))
                .ToArray();
            if (settings.Length != 1 || string.IsNullOrWhiteSpace(settings[0].googleIosClientId) || !settings[0].googleIosClientId.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Create exactly one Novorizon Native Identity Settings asset with the Google iOS OAuth client ID.");
            }
            string reversed = string.Join(".", settings[0].googleIosClientId.Split('.').Reverse());
            string plistPath = Path.Combine(path, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            PlistElementArray types = plist.root.values.TryGetValue("CFBundleURLTypes", out var existing) ? existing.AsArray() : plist.root.CreateArray("CFBundleURLTypes");
            bool present = types.values.Any(item => item.AsDict().values.TryGetValue("CFBundleURLSchemes", out var schemes) && schemes.AsArray().values.Any(scheme => scheme.AsString() == reversed));
            if (!present)
            {
                types.AddDict().CreateArray("CFBundleURLSchemes").AddString(reversed);
            }
            plist.WriteToFile(plistPath);
            string pbxPath = PBXProject.GetPBXProjectPath(path);
            var pbx = new PBXProject();
            pbx.ReadFromFile(pbxPath);
            string app = pbx.GetUnityMainTargetGuid();
            string entitlementPath = pbx.GetBuildPropertyForAnyConfig(app, "CODE_SIGN_ENTITLEMENTS");
            if (string.IsNullOrEmpty(entitlementPath))
            {
                entitlementPath = "NovorizonIdentity.entitlements";
            }
            string fullPath = Path.Combine(path, entitlementPath);
            var entitlements = new PlistDocument();
            if (File.Exists(fullPath))
            {
                entitlements.ReadFromFile(fullPath);
            }
            entitlements.root.CreateArray("com.apple.developer.applesignin").AddString("Default");
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            entitlements.WriteToFile(fullPath);
            if (string.IsNullOrEmpty(pbx.FindFileGuidByProjectPath(entitlementPath)))
            {
                pbx.AddFile(entitlementPath, entitlementPath);
            }
            pbx.SetBuildProperty(app, "CODE_SIGN_ENTITLEMENTS", entitlementPath);
            pbx.AddFrameworkToProject(pbx.GetUnityFrameworkTargetGuid(), "AuthenticationServices.framework", false);
            foreach (string guid in new[] { app, pbx.GetUnityFrameworkTargetGuid() })
            {
                string current = pbx.GetBuildPropertyForAnyConfig(guid, "IPHONEOS_DEPLOYMENT_TARGET");
                if (!Version.TryParse(current, out var version) || version < new Version(15, 0))
                {
                    pbx.SetBuildProperty(guid, "IPHONEOS_DEPLOYMENT_TARGET", "15.0");
                }
            }
            pbx.WriteToFile(pbxPath);
        }
    }
}
#endif

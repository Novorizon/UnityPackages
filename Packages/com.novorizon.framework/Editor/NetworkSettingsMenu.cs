#if UNITY_EDITOR
using System.IO;
using Game.Framework.Network.Unity;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Editor
{
    public static class NetworkSettingsMenu
    {
        [MenuItem("Tools/Novorizon/Create Default Network Settings")]
        public static void CreateDefaultSettings()
        {
            const string path = "Assets/Resources/NetworkSettings.asset";
            var existing = AssetDatabase.LoadAssetAtPath<NetworkSettings>(path);
            if (existing != null) { Selection.activeObject = existing; return; }
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
            var settings = ScriptableObject.CreateInstance<NetworkSettings>();
            AssetDatabase.CreateAsset(settings, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = settings;
        }
    }
}
#endif

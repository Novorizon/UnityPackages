using System;
using System.IO;
using Game.Framework.Data.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Localization.Editor
{
    public static class LocalizationLubanMenu
    {
        [MenuItem("Novorizon/Localization/Create Luban Project")]
        public static void CreateProject()
        {
            const string settingsPath = "Assets/Settings/LocalizationGenerator.asset";
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string target = Path.Combine(root, "Config/Localization");
            if (Directory.Exists(target) && Directory.GetFileSystemEntries(target).Length != 0)
            {
                throw new InvalidOperationException("Localization project already exists: " + target);
            }

            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalizationLubanMenu).Assembly);
            string source = Path.Combine(package.resolvedPath, "Templates~/LubanProject");
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, file.Substring(source.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            var settings = AssetDatabase.LoadAssetAtPath<LubanGeneratorSettings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LubanGeneratorSettings>();
                settings.configFile = "Config/Localization/luban.conf";
                settings.codeOutput = "Assets/Generated/Localization";
                settings.dataOutput = "Assets/Resources/Localization";
                settings.locationPrefix = "Localization";
                settings.generatedAssembly = "Game.Localization.Generated";
                AssetDatabase.CreateAsset(settings, settingsPath);
            }

            AssetDatabase.Refresh();
            Selection.activeObject = settings;
            Debug.Log("Edit Config/Localization/Data/translations.xlsx, keep LocalizationGenerator selected, then run Novorizon/Data/Luban/Generate.");
        }
    }
}

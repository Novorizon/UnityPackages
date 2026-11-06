using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Data.Editor
{
    public static class LubanGeneratorMenu
    {
        private const string SettingsPath = "Assets/Settings/LubanGenerator.asset";
        private static bool busy;
        [MenuItem("Novorizon/Data/Luban/Create Project")]
        public static void CreateProject()
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LubanGeneratorMenu).Assembly);
            string source = Path.Combine(package.resolvedPath, "Templates~/LubanProject");
            string target = Path.Combine(root, "Config/Luban");
            if (Directory.Exists(target) && Directory.GetFileSystemEntries(target).Length != 0)
            {
                throw new InvalidOperationException("Luban project already exists: " + target);
            }

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string output = Path.Combine(target, file.Substring(source.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.Copy(file, output);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            var settings = AssetDatabase.LoadAssetAtPath<LubanGeneratorSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LubanGeneratorSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            AssetDatabase.Refresh();
            Selection.activeObject = settings;
            Debug.Log("Luban project created at Config/Luban. Edit Data/example.xlsx, then use Novorizon/Data/Luban/Generate.");
        }

        [MenuItem("Novorizon/Data/Luban/Generate")]
        public static async void Generate()
        {
            if (busy)
            {
                Debug.LogWarning("Luban generation is already running.");
                return;
            }

            busy = true;
            try
            {
                var settings = Selection.activeObject as LubanGeneratorSettings ?? AssetDatabase.LoadAssetAtPath<LubanGeneratorSettings>(SettingsPath);
                if (settings == null)
                {
                    throw new InvalidOperationException("Create a Luban project first: Novorizon/Data/Luban/Create Project.");
                }

                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string dll = string.IsNullOrWhiteSpace(settings.lubanDll) ? await LubanExporter.EnsureToolAsync(root, settings.dotnetExecutable) : Path.GetFullPath(Path.Combine(root, settings.lubanDll));
                await LubanExporter.GenerateAsync(root, settings.configFile, settings.target, settings.codeOutput, settings.dataOutput, settings.locationPrefix, settings.dotnetExecutable, dll, settings.generatedAssembly, settings.generatedAutoReferenced);
                AssetDatabase.Refresh();
                Debug.Log("Luban C# tables, binary assets and manifest generated successfully.");
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                busy = false;
            }
        }
    }
}

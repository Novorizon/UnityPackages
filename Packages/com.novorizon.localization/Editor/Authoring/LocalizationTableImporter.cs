using System;
using System.IO;
using System.Linq;
using System.Text;
using Game.Framework.Localization.Unity;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Localization.Editor
{
    public static class LocalizationTableImporter
    {
        /// <summary>Validate all cells before modifying an existing asset; failed parsing retains its last data.</summary>
        public static void Import(string sourcePath, LocalizationTableAsset target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }
            var data = Read(sourcePath);
            Undo.RecordObject(target, "Import Localization Table");
            target.SetData(data);
            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssetIfDirty(target);
        }

        public static LocalizationTableData Read(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException("Choose a CSV or TSV source file.", nameof(sourcePath));
            }
            string extension = Path.GetExtension(sourcePath);
            if (!string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".tsv", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Export the spreadsheet as UTF-8 CSV or TSV first.", nameof(sourcePath));
            }
            return LocalizationDelimitedText.Parse(File.ReadAllText(sourcePath, new UTF8Encoding(false, true)), extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',');
        }

        [MenuItem("Assets/Novorizon/Localization/Create Setup From Selected CSV or TSV", true)]
        private static bool CanCreateSetup()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return Selection.activeObject != null &&
                (path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase));
        }

        [MenuItem("Assets/Novorizon/Localization/Create Setup From Selected CSV or TSV")]
        private static void CreateSelectedSetup()
        {
            if (!CanCreateSetup())
            {
                throw new InvalidOperationException("Select an imported UTF-8 CSV/TSV asset first.");
            }
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            var data = Read(path);
            string stem = Path.ChangeExtension(path, null);
            string tablePath = stem + ".LocalizationTable.asset";
            string settingsPath = stem + ".LocalizationSettings.asset";
            if (AssetDatabase.LoadMainAssetAtPath(tablePath) != null || AssetDatabase.LoadMainAssetAtPath(settingsPath) != null ||
                File.Exists(tablePath) || File.Exists(settingsPath))
            {
                throw new IOException("Setup output paths already exist. Select the existing table and use Import CSV/TSV to update it.");
            }
            var table = ScriptableObject.CreateInstance<LocalizationTableAsset>();
            var settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            try
            {
                table.SetData(data);
                settings.table = table;
                settings.defaultLanguage = data.Languages.FirstOrDefault(language => language == "en") ?? data.Languages[0];
                settings.fallbackLanguage = settings.defaultLanguage;
                AssetDatabase.CreateAsset(table, tablePath);
                AssetDatabase.CreateAsset(settings, settingsPath);
                AssetDatabase.SaveAssetIfDirty(table);
                AssetDatabase.SaveAssetIfDirty(settings);
                Selection.activeObject = settings;
                EditorGUIUtility.PingObject(settings);
            }
            catch
            {
                // Both paths were unoccupied on entry. Clean only outputs this invocation owns.
                if (AssetDatabase.LoadMainAssetAtPath(settingsPath) == settings)
                {
                    AssetDatabase.DeleteAsset(settingsPath);
                }
                if (AssetDatabase.LoadMainAssetAtPath(tablePath) == table)
                {
                    AssetDatabase.DeleteAsset(tablePath);
                }
                throw;
            }
            finally
            {
                if (settings != null && !EditorUtility.IsPersistent(settings))
                {
                    UnityEngine.Object.DestroyImmediate(settings);
                }
                if (table != null && !EditorUtility.IsPersistent(table))
                {
                    UnityEngine.Object.DestroyImmediate(table);
                }
            }
        }
    }
}

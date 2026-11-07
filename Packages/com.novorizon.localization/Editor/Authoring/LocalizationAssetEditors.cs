using System;
using System.Linq;
using Game.Framework.Localization.Unity;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Localization.Editor
{
    [CustomEditor(typeof(LocalizationTableAsset))]
    public sealed class LocalizationTableAssetEditor : UnityEditor.Editor
    {
        private string validation;
        private MessageType severity;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var table = (LocalizationTableAsset)target;
            if (GUILayout.Button("Import UTF-8 CSV / TSV"))
            {
                string source = EditorUtility.OpenFilePanelWithFilters("Import Localization Table", "", new[] { "CSV / TSV", "csv,tsv" });
                if (!string.IsNullOrEmpty(source))
                {
                    try
                    {
                        LocalizationTableImporter.Import(source, table);
                        Validate(table);
                    }
                    catch (Exception error)
                    {
                        validation = error.Message;
                        severity = MessageType.Error;
                    }
                }
            }
            if (GUILayout.Button("Validate Keys / Formats / Missing Translations"))
            {
                Validate(table);
            }
            if (!string.IsNullOrEmpty(validation))
            {
                EditorGUILayout.HelpBox(validation, severity);
            }
            EditorGUILayout.HelpBox("Table cells are baked into this asset. Editing it does not mutate an already initialized runtime catalog; restart its explicit owner to load a new snapshot.", MessageType.Info);
        }

        private void Validate(LocalizationTableAsset table)
        {
            try
            {
                var data = table.GetData();
                var missing = data.FindMissingTranslations();
                validation = data.RowCount + " keys / " + data.Languages.Count + " languages. " + missing.Length + " empty translation cells.";
                if (missing.Length != 0)
                {
                    validation += "\n" + string.Join("\n", missing.Take(20).Select(cell => cell.Key + " / " + cell.Language));
                    if (missing.Length > 20)
                    {
                        validation += "\n…";
                    }
                }
                severity = missing.Length == 0 ? MessageType.Info : MessageType.Warning;
            }
            catch (Exception error)
            {
                validation = error.Message;
                severity = MessageType.Error;
            }
        }
    }

    [CustomEditor(typeof(LocalizationSettings))]
    public sealed class LocalizationSettingsEditor : UnityEditor.Editor
    {
        private string result;
        private MessageType severity;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Validate Startup Settings"))
            {
                try
                {
                    var catalog = ((LocalizationSettings)target).CreateCatalog();
                    result = catalog.KeyCount + " keys ready. Initialize this settings asset before opening localized UI.";
                    severity = MessageType.Info;
                }
                catch (Exception error)
                {
                    result = error.Message;
                    severity = MessageType.Error;
                }
            }
            if (!string.IsNullOrEmpty(result))
            {
                EditorGUILayout.HelpBox(result, severity);
            }
        }
    }
}

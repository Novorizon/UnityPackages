using System;
using System.Globalization;
using System.Linq;
using Game.Framework.Localization.UGUI;
using Game.Framework.Localization.Unity;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Localization.Editor
{
    /// <summary>Authoring preview only. Never creates or reads a runtime localization singleton.</summary>
    [CustomEditor(typeof(LocalizedText))]
    public sealed class LocalizedTextEditor : UnityEditor.Editor
    {
        private static LocalizationSettings previewSettings;
        private string search = "";
        private string chosen = "";
        private int languageIndex;
        private LocalizationTableAsset cachedTable;
        private int cachedVersion = -1;
        private LocalizationCatalog catalog;
        private string error;
        private string glyphResult;
        private string glyphContext;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var key = serializedObject.FindProperty("key");
            var fonts = serializedObject.FindProperty("fonts");
            EditorGUILayout.PropertyField(key);
            EditorGUILayout.PropertyField(fonts, true);
            if (serializedObject.ApplyModifiedProperties())
            {
                glyphResult = null;
            }
            EditorGUILayout.Space();
            previewSettings = (LocalizationSettings)EditorGUILayout.ObjectField("Preview Settings", previewSettings, typeof(LocalizationSettings), false);
            EditorGUILayout.HelpBox("Preview settings are editor-only and are not saved into this label. Runtime initialization belongs to the project entry/bootstrap.", MessageType.Info);
            if (previewSettings == null || previewSettings.table == null)
            {
                EditorGUILayout.HelpBox("Choose the project's LocalizationSettings to search keys and preview translations.", MessageType.Info);
                return;
            }
            UpdateCatalog(previewSettings.table);
            if (catalog == null)
            {
                EditorGUILayout.HelpBox(error, MessageType.Error);
                return;
            }
            search = EditorGUILayout.TextField("Search Key", search);
            string[] matches = catalog.Keys.Where(value => value.IndexOf(search ?? "", StringComparison.OrdinalIgnoreCase) >= 0).Take(200).ToArray();
            if (matches.Length != 0)
            {
                int selected = Math.Max(0, Array.IndexOf(matches, string.IsNullOrEmpty(chosen) ? key.stringValue : chosen));
                selected = EditorGUILayout.Popup("Key", selected, matches);
                chosen = matches[selected];
                if (GUILayout.Button("Assign Selected Key"))
                {
                    key.stringValue = chosen;
                    serializedObject.ApplyModifiedProperties();
                    glyphResult = null;
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No matching keys.", MessageType.Info);
            }
            EditorGUILayout.LabelField("Search results are limited to the first 200 keys.", EditorStyles.miniLabel);
            var languages = catalog.Languages.ToArray();
            languageIndex = Math.Max(0, Math.Min(languageIndex, languages.Length - 1));
            languageIndex = EditorGUILayout.Popup("Preview Language", languageIndex, languages);
            string language = languages[languageIndex];
            string id = key.stringValue;
            if (!catalog.ContainsKey(id))
            {
                EditorGUILayout.HelpBox("Missing key: " + id, MessageType.Warning);
                return;
            }
            if (!catalog.TryGetExact(id, language, out string text))
            {
                EditorGUILayout.HelpBox("Empty translation: " + id + " / " + language + ". Runtime will use its parent/fallback policy.", MessageType.Warning);
                try
                {
                    text = PreviewTranslation(catalog, id, language, previewSettings.fallbackLanguage);
                }
                catch (ArgumentException failure)
                {
                    EditorGUILayout.HelpBox(failure.Message, MessageType.Error);
                    text = id;
                }
            }
            EditorGUILayout.TextArea(text ?? "", GUILayout.MinHeight(55));
            var selectedFont = FindFont(fonts, language, ((LocalizedText)target).GetComponent<TMP_Text>()?.font);
            string context = language + "\0" + id + "\0" + text + "\0" + (selectedFont != null ? selectedFont.GetInstanceID().ToString() : "none");
            if (glyphContext != context)
            {
                glyphContext = context;
                glyphResult = null;
            }
            if (GUILayout.Button("Check Current Font Assets For These Characters"))
            {
                var font = selectedFont;
                if (font == null)
                {
                    glyphResult = "No TMP font asset is assigned.";
                }
                else
                {
                    string printable = (text ?? "").Replace("\n", "").Replace("\r", "").Replace("\t", "");
                    bool complete = font.HasCharacters(printable, out uint[] missing, true, false);
                    if (!complete && missing == null)
                    {
                        glyphResult = "The font character lookup could not be read. Check this TMP font asset.";
                    }
                    else
                    {
                        glyphResult = complete ? "All source characters are present in this font/fallback assets." :
                            "Missing source characters (UTF-16 values): " + string.Join(", ", missing.Take(32).Select(value => "U+" + value.ToString("X4")));
                    }
                }
            }
            if (!string.IsNullOrEmpty(glyphResult))
            {
                EditorGUILayout.HelpBox(glyphResult + "\nThis read-only TMP scan does not add dynamic glyphs and includes literal format/rich-text characters. TMP 3 scans UTF-16 values; supplementary Unicode, actual layout and shaping need native checks.", MessageType.Info);
            }
        }

        private void UpdateCatalog(LocalizationTableAsset table)
        {
            if (cachedTable == table && cachedVersion == table.ContentVersion)
            {
                return;
            }
            cachedTable = table;
            cachedVersion = table.ContentVersion;
            glyphResult = null;
            try
            {
                catalog = table.CreateCatalog();
                error = null;
            }
            catch (Exception failure)
            {
                catalog = null;
                error = failure.Message;
            }
        }

        private static string PreviewTranslation(LocalizationCatalog source, string key, string language, string fallbackLanguage)
        {
            foreach (string code in new[] { language, fallbackLanguage })
            {
                var culture = CultureInfo.GetCultureInfo(code);
                while (culture.Name.Length != 0)
                {
                    if (source.TryGetExact(key, culture.Name, out string text))
                    {
                        return text;
                    }
                    culture = culture.Parent;
                }
            }
            return key;
        }

        private static TMP_FontAsset FindFont(SerializedProperty fonts, string language, TMP_FontAsset fallback)
        {
            var culture = CultureInfo.GetCultureInfo(language);
            while (culture.Name.Length != 0)
            {
                for (int index = 0; index < fonts.arraySize; index++)
                {
                    var item = fonts.GetArrayElementAtIndex(index);
                    if (string.Equals(item.FindPropertyRelative("language").stringValue, culture.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        var font = item.FindPropertyRelative("font").objectReferenceValue as TMP_FontAsset;
                        if (font != null)
                        {
                            return font;
                        }
                    }
                }
                culture = culture.Parent;
            }
            return fallback;
        }
    }
}

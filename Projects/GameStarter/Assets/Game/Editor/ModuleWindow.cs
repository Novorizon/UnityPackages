using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    public sealed class ModuleWindow : EditorWindow
    {
        private ModuleCatalog catalog;
        private readonly HashSet<string> selected = new HashSet<string>();
        private Vector2 scroll;

        [MenuItem("Tools/Novorizon/Optional Modules")]
        public static void Open() => GetWindow<ModuleWindow>("Novorizon Modules");

        private void OnEnable()
        {
            try { catalog = ModuleInstaller.ReadCatalog(); }
            catch (Exception exception) { Debug.LogException(exception); }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("GameStarter 可选模块", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Framework 已作为基础依赖。选择模块后会自动安装依赖，安装进度可跨脚本重编译继续。", MessageType.Info);
            if (catalog == null) { EditorGUILayout.HelpBox("无法读取模块目录，请检查 OptionalModules.json。", MessageType.Error); return; }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            using (new EditorGUI.DisabledScope(ModuleInstaller.Busy || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                foreach (var module in catalog.modules.Where(m => !m.hidden))
                {
                    var installed = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == module.id);
                    string status = installed == null ? "未安装" : "已安装 " + installed.version;
                    bool enabled = EditorGUILayout.ToggleLeft(module.title + "  " + module.version + "  (" + status + ")", selected.Contains(module.id));
                    if (enabled) selected.Add(module.id); else selected.Remove(module.id);
                    EditorGUILayout.LabelField(module.description, EditorStyles.wordWrappedLabel);
                    EditorGUILayout.Space();
                }
                if (GUILayout.Button("安装所选模块"))
                {
                    try { ModuleInstaller.Install(selected.OrderBy(id => id, StringComparer.Ordinal).ToArray()); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox(ModuleInstaller.Message, MessageType.Info);
            if (ModuleInstaller.Busy) Repaint();
        }
    }
}

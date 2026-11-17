#if UNITY_EDITOR && !ODIN_INSPECTOR
using UnityEditor;
using UnityEngine;

namespace Game.Ability.Editor.Odin
{
    /// <summary>提供技能编辑工作台缺失 Odin的实现。</summary>
    internal sealed class AbilityStudioMissingOdin : EditorWindow
    {
        /// <summary>打开技能编辑工作台缺失 Odin。</summary>
        [MenuItem("Tools/Ability/Skill Studio", priority = 100)]
        private static void Open() => GetWindow<AbilityStudioMissingOdin>("技能工作台");

        /// <summary>绘制当前界面并响应对应的 GUI 事件。</summary>
        private void OnGUI()
        {
            GUILayout.Space(20);
            GUILayout.Label("技能工作台需要 Odin Inspector 3.x", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("请在当前项目安装已有授权的 Odin Inspector，并等待脚本编译。编辑器使用 ODIN_INSPECTOR 标记启用界面；技能运行时不依赖 Odin。", MessageType.Info);
            GUILayout.Space(8);
            GUILayout.Label("安装后入口：Tools / Ability / Skill Studio", EditorStyles.wordWrappedLabel);
        }
    }
}
#endif

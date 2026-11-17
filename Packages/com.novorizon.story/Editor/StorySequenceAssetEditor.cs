using System;
using Game.Framework.Story.Unity;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Story.Editor
{
    /// <summary>剧情序列资源对应的编辑器。提供相应资源或组件的 Unity 编辑界面。</summary>
    [CustomEditor(typeof(StorySequenceAsset))]
    public sealed class StorySequenceAssetEditor : UnityEditor.Editor
    {
        private string validation;
        private MessageType validationType;

        /// <summary>绘制当前对象的检视面板并处理用户编辑。</summary>
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Use stable node IDs and localization keys. Say.ResourceId is an optional voice; portrait/background/video ResourceId resolves through the project asset provider. Empty NextId follows the next ordered node.",
                MessageType.Info);
            DrawDefaultInspector();
            if (GUILayout.Button("Validate Story"))
            {
                try
                {
                    StorySequence sequence = ((StorySequenceAsset)target).Build();
                    validation = "Valid: " + sequence.Id + " / " + sequence.Version;
                    validationType = MessageType.Info;
                }
                catch (Exception error)
                {
                    validation = error.Message;
                    validationType = MessageType.Error;
                }
            }
            if (!string.IsNullOrEmpty(validation))
            {
                EditorGUILayout.HelpBox(validation, validationType);
            }
        }
    }
}

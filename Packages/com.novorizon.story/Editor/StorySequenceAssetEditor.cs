using System;
using Game.Framework.Story.Unity;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Story.Editor
{
    [CustomEditor(typeof(StorySequenceAsset))]
    public sealed class StorySequenceAssetEditor : UnityEditor.Editor
    {
        private string validation;
        private MessageType validationType;

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

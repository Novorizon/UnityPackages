#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Framework.Story.Samples.Editor
{
    public static class BasicStorySceneMenu
    {
        [MenuItem("Tools/Novorizon/Demos/Create Basic Story Scene")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Create the story sample outside Play mode.");
            }
            if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
            {
                throw new InvalidOperationException("Import Window > TextMeshPro > Import TMP Essential Resources, and assign a TMP default font before creating the story scene.");
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/NovorizonDemos"))
            {
                AssetDatabase.CreateFolder("Assets", "NovorizonDemos");
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Basic Story Sample");
            root.AddComponent<BasicStorySample>();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/NovorizonDemos/BasicStory.unity");
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new InvalidOperationException("The story sample scene could not be saved.");
            }
            Selection.activeGameObject = root;
            Debug.Log("Created " + path + ". Enter Play mode; optional voice/video clips can be assigned on BasicStorySample before playing.");
        }
    }
}
#endif

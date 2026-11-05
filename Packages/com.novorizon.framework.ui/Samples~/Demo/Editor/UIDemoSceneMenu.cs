#if UNITY_EDITOR
#nullable enable
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace UI.Demo.Editor
{
    public static class UIDemoSceneMenu
    {
        [MenuItem("Tools/Novorizon/Demos/Create UI Demo Scene")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Create the demo scene outside Play mode.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!AssetDatabase.IsValidFolder("Assets/NovorizonDemos"))
                AssetDatabase.CreateFolder("Assets", "NovorizonDemos");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var launcher = new GameObject("UI Demo Launcher");
            launcher.AddComponent<UIDemoLauncher>();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/NovorizonDemos/UIDemo.unity");
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException("The UI demo scene could not be saved.");
            Selection.activeGameObject = launcher;
            Debug.Log("UI demo scene created: " + path + ". Enter Play mode to build the UI.");
        }
    }
}
#endif

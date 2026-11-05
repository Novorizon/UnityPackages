#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Framework.Samples.Demo.Editor
{
    public static class FrameworkDemoSceneMenu
    {
        [MenuItem("Tools/Novorizon/Demos/Create Core Demo Scene")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Create the demo scene outside Play mode.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject("Novorizon Core Demo");
            root.AddComponent<FrameworkDemo>();
            Undo.RegisterCreatedObjectUndo(root, "Create Novorizon Core Demo");
            Selection.activeObject = root;
            const string folder = "Assets/NovorizonDemos";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets", "NovorizonDemos");
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/CoreDemo.unity");
            if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("The Core Demo scene could not be saved.");
            AssetDatabase.Refresh();
        }
    }
}
#endif

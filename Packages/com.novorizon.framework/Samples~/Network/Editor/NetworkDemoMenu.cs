#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Framework.Network.Samples.Editor
{
    public static class NetworkDemoMenu
    {
        [MenuItem("Tools/Novorizon/Demos/Create Network Demo Scene")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Create the network demo scene in Edit mode, then press Play.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Assets/NovorizonDemos");
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var demo = new GameObject("TCP Network Demo");
            demo.AddComponent<TcpNetworkDemo>();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/NovorizonDemos/NetworkDemo.unity");
            if (!EditorSceneManager.SaveScene(scene, path))
                Debug.LogError("The demo scene could not be saved. Save the current scene manually.");
            Selection.activeGameObject = demo;
        }
    }
}
#endif

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Framework.Samples.Resources.Editor
{
    /// <summary>提供资源示例场景菜单的实现。</summary>
    public static class ResourceDemoSceneMenu
    {
        /// <summary>创建场景。</summary>
        [MenuItem("Tools/Novorizon/Demos/Create Resource Demo Scene")]
        public static void CreateScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Create the demo scene outside Play mode.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            const string folder = "Assets/NovorizonDemos";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                AssetDatabase.CreateFolder("Assets", "NovorizonDemos");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject("Novorizon Resource Demo");
            root.AddComponent<ResourceDemoController>();
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/ResourceDemo.unity");
            if (EditorSceneManager.SaveScene(scene, path))
            {
                Selection.activeGameObject = root;
            }
        }
    }
}
#endif

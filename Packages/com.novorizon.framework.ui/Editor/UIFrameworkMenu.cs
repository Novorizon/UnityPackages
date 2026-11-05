#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace UI.Editor
{
    public static class UIFrameworkMenu
    {
        [MenuItem("Tools/Novorizon/UI/Create UIBootstrap in Scene")]
        public static void CreateBootstrap()
        {
            var go = new GameObject("UIBootstrap");
            Undo.RegisterCreatedObjectUndo(go, "Create UI Bootstrap");
            go.AddComponent<UI.UIBootstrap>();
            Selection.activeObject = go;
        }

        [MenuItem("Tools/Novorizon/UI/Create UISettings Asset")]
        public static void CreateSettings()
        {
            var asset = ScriptableObject.CreateInstance<UI.UISettings>();
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Resources/UISettings.asset");
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
        }
    }
}
#endif

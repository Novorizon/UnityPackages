using System;
using System.Reflection;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string path)
        {
        }
    }

    public static class Selection
    {
        public static UnityEngine.Object activeObject;
    }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path)
            where T : UnityEngine.Object => null;
        public static void CreateAsset(UnityEngine.Object value, string path)
        {
        }

        public static void Refresh()
        {
        }
    }
}

namespace UnityEditor.PackageManager
{
    public sealed class PackageInfo
    {
        public string resolvedPath;
        public static PackageInfo FindForAssembly(Assembly assembly) => new PackageInfo();
    }
}

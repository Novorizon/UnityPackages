#if UNITY_ANDROID && NOVORIZON_NATIVE_AUTH
using System.IO;
using UnityEditor.Android;

namespace Game.Framework.Auth.Editor
{
    public sealed class IdentityAndroidBuild : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string rules = "novorizon-auth.pro";
            File.WriteAllText(Path.Combine(path, rules), "-keep class com.novorizon.auth.GoogleIdentityBridge { *; }\n");
            string gradle = Path.Combine(path, "build.gradle");
            string content = File.ReadAllText(gradle);
            string entry = "consumerProguardFiles '" + rules + "'";
            if (!content.Contains(entry))
            {
                File.AppendAllText(gradle, "\nandroid { defaultConfig { " + entry + " } }\n");
            }
        }
    }
}
#endif

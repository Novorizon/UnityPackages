#if UNITY_ANDROID && NOVORIZON_CSJ
using System.IO;
using UnityEditor.Android;

namespace Game.Framework.Ads.Editor
{
    public sealed class AdsAndroidBuild : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string rules = "novorizon-ads.pro";
            File.WriteAllText(Path.Combine(path, rules), "-keep class com.novorizon.ads.CsjBridge { *; }\n");
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

using UnityEngine;

namespace Game.Framework.Config.Editor
{
    [CreateAssetMenu(menuName = "Novorizon/Luban Generator Settings")]
    public sealed class LubanGeneratorSettings : ScriptableObject
    {
        public string configFile = "Config/Luban/luban.conf";
        public string target = "client";
        public string codeOutput = "Assets/Generated/Config";
        public string dataOutput = "Assets/Resources/Config/Luban";
        public string locationPrefix = "Config/Luban";
        public string generatedAssembly = "Game.Config.Generated";
        public string dotnetExecutable = "dotnet";
        [Tooltip("留空时自动缓存并编译官方 Luban 4.12.0；也可以指定本机的 Luban.dll。")]
        public string lubanDll;
    }
}

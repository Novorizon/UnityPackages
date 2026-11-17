using System;

namespace Game.Framework.GpuAnimation.URP.Editor
{
    /// <summary>提供URPCompatibility的实现。</summary>
    public static class UrpCompatibility
    {
        public const string MinimumVersion = "14.0.1";
        public const string RecommendedVersion = "14.0.12";

        /// <summary>判断是否为受支持。</summary>
        public static bool IsSupported(string version)
        {
            return Version.TryParse(version, out var parsed) && parsed.Major == 14 && parsed >= new Version(14, 0, 1);
        }
    }
}

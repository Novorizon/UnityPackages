using System;

namespace Game.Framework.GpuAnimation.URP.Editor
{
    public static class UrpCompatibility
    {
        public const string MinimumVersion = "14.0.1";
        public const string RecommendedVersion = "14.0.12";

        public static bool IsSupported(string version)
        {
            return Version.TryParse(version, out var parsed) && parsed.Major == 14 && parsed >= new Version(14, 0, 1);
        }
    }
}

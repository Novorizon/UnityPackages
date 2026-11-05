using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Game.Framework.All.Editor
{
    [Serializable]
    internal sealed class PackageCatalog
    {
        public PackageDependency[] packages;
        public string[] obsoletePackages = Array.Empty<string>();
        internal void Validate()
        {
            if (packages == null || packages.Length == 0) throw new InvalidDataException("Empty package catalog.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var package in packages)
            {
                if (string.IsNullOrEmpty(package.name) || !names.Add(package.name)) throw new InvalidDataException("Duplicate or empty package name.");
                if (string.IsNullOrEmpty(package.version) || string.IsNullOrEmpty(package.git))
                    throw new InvalidDataException("Incomplete package dependency: " + package.name);
            }
        }
    }

    [Serializable]
    internal sealed class PackageDependency
    {
        public string name;
        public string version;
        public string git;
        public bool external;
        public bool embed;

        [Serializable]
        private sealed class PackageIdentity
        {
            public string name;
            public string version;
        }

        internal string PrepareSource(string packageRoot, string projectRoot, string release)
        {
            // Third-party sources are always resolved by UPM, never copied into this package.
            if (external) return git;
            string sibling = Path.GetFullPath(Path.Combine(packageRoot, "..", name));
            string definition = Path.Combine(sibling, "package.json");
            if (File.Exists(definition))
            {
                var identity = JsonUtility.FromJson<PackageIdentity>(File.ReadAllText(definition));
                if (identity.name == name && identity.version == version)
                    return "file:" + Path.GetRelativePath(Path.Combine(projectRoot, "Packages"), sibling).Replace('\\', '/');
            }
            return git;
        }
    }
}

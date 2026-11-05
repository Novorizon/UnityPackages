using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace Game.Framework.All.Editor
{
    [Serializable]
    internal sealed class PackageCatalog
    {
        public PackageDependency[] packages;
        internal void Validate()
        {
            if (packages == null || packages.Length == 0) throw new InvalidDataException("Empty package catalog.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var package in packages)
            {
                if (string.IsNullOrEmpty(package.name) || !names.Add(package.name)) throw new InvalidDataException("Duplicate or empty package name.");
                if (string.IsNullOrEmpty(package.version) || string.IsNullOrEmpty(package.git) || string.IsNullOrEmpty(package.sha256))
                    throw new InvalidDataException("Incomplete package identity: " + package.name);
                if (string.IsNullOrEmpty(package.archive) || Path.GetFileName(package.archive) != package.archive)
                    throw new InvalidDataException("Archive must be a file name: " + package.name);
            }
        }

        // Validate the entire payload before submitting any UPM operation.
        internal void ValidatePayload(string packageRoot)
        {
            string payload = Path.Combine(packageRoot, "Payload~");
            if (!Directory.Exists(payload)) return;
            foreach (var package in packages) package.CheckArchive(Path.Combine(payload, package.archive));
        }
    }

    [Serializable]
    internal sealed class PackageDependency
    {
        public string name;
        public string version;
        public string git;
        public string archive;
        public string sha256;
        public bool embed;

        internal static string Hash(string file)
        {
            using var input = File.OpenRead(file);
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
        }

        internal void CheckArchive(string path)
        {
            if (!File.Exists(path) || !string.Equals(Hash(path), sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Missing or corrupt archive: " + archive);
        }

        [Serializable]
        private sealed class PackageIdentity
        {
            public string name;
            public string version;
        }

        internal string PrepareSource(string packageRoot, string projectRoot, string release)
        {
            string sibling = Path.GetFullPath(Path.Combine(packageRoot, "..", name));
            string definition = Path.Combine(sibling, "package.json");
            if (File.Exists(definition))
            {
                var identity = JsonUtility.FromJson<PackageIdentity>(File.ReadAllText(definition));
                if (identity.name == name && identity.version == version)
                    return "file:" + Path.GetRelativePath(Path.Combine(projectRoot, "Packages"), sibling).Replace('\\', '/');
            }
            string payload = Path.Combine(packageRoot, "Payload~");
            if (!Directory.Exists(payload)) return git;
            string relative = "NovorizonFrameworkAll~/" + release + "/" + archive;
            string target = Path.Combine(projectRoot, "Packages", relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            if (!File.Exists(target) || !string.Equals(Hash(target), sha256, StringComparison.OrdinalIgnoreCase))
            {
                string source = Path.Combine(payload, archive);
                CheckArchive(source);
                string temporary = target + ".copying~";
                File.Copy(source, temporary, true);
                CheckArchive(temporary);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
            }
            // Relative to Packages/; never point a project manifest into Library/PackageCache.
            return "file:" + relative;
        }
    }
}

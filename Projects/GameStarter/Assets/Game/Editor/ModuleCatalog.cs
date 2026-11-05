using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Game.Editor
{
    [Serializable]
    internal sealed class ModuleCatalog
    {
        public ModuleSpec[] modules = Array.Empty<ModuleSpec>();

        internal ModuleSpec[] Plan(IEnumerable<string> selection)
        {
            var map = new Dictionary<string, ModuleSpec>(StringComparer.Ordinal);
            foreach (var module in modules)
            {
                if (string.IsNullOrWhiteSpace(module.id) || string.IsNullOrWhiteSpace(module.version) || string.IsNullOrWhiteSpace(module.source))
                    throw new InvalidDataException("Module id, version and source are required.");
                if (!map.TryAdd(module.id, module)) throw new InvalidDataException("Duplicate module: " + module.id);
            }
            var result = new List<ModuleSpec>();
            var visiting = new HashSet<string>();
            var visited = new HashSet<string>();
            void Visit(string id)
            {
                if (visited.Contains(id)) return;
                if (!map.TryGetValue(id, out var module)) throw new InvalidDataException("Unknown dependency: " + id);
                if (!visiting.Add(id)) throw new InvalidDataException("Circular dependency: " + id);
                foreach (string dependency in module.dependencies ?? Array.Empty<string>()) Visit(dependency);
                visiting.Remove(id);
                visited.Add(id);
                result.Add(module);
            }
            foreach (string id in selection) Visit(id);
            return result.ToArray();
        }
    }

    [Serializable]
    internal sealed class ModuleSpec
    {
        public string id;
        public string title;
        public string version;
        public string source;
        public string archive;
        public string sha256;
        public string description;
        public string[] dependencies = Array.Empty<string>();
        public string[] replaces = Array.Empty<string>();
        public bool hidden;
        public bool embed;
        public string setupType;
        public string setupMethod;

        internal string ResolveSource(string projectRoot)
        {
            if (string.IsNullOrEmpty(archive)) return source;
            if (Path.GetFileName(archive) != archive) throw new InvalidDataException("Archive must be a file name: " + id);
            string path = Path.Combine(projectRoot, "Packages", "OptionalArchives~", archive);
            if (!File.Exists(path)) return source;
            using var input = File.OpenRead(path);
            using var hash = SHA256.Create();
            string actual = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Archive checksum mismatch: " + archive);
            return "file:OptionalArchives~/" + archive;
        }
    }
}

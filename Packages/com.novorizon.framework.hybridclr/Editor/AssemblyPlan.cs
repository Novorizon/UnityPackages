using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.Framework.HotUpdate.Editor
{
    // Build-time graph, independent of Unity's assembly representation for deterministic validation.
    public static class AssemblyPlan
    {
        public static string[] Order(IReadOnlyDictionary<string, string[]> dependencies, string entry)
        {
            if (!dependencies.ContainsKey(entry)) throw new InvalidDataException("Entry assembly is outside the hot update folder: " + entry);
            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            var ordered = new List<string>();
            void Visit(string name)
            {
                if (states.TryGetValue(name, out int state))
                {
                    if (state == 1) throw new InvalidDataException("Circular hot update assembly dependency: " + name);
                    return;
                }
                states[name] = 1;
                foreach (string dependency in dependencies[name].Where(dependencies.ContainsKey).OrderBy(n => n, StringComparer.Ordinal)) Visit(dependency);
                states[name] = 2;
                ordered.Add(name);
            }
            foreach (string name in dependencies.Keys.OrderBy(n => n, StringComparer.Ordinal)) Visit(name);
            if (ordered.Count > 16) throw new InvalidDataException("At most 16 hot update assemblies are supported.");
            return ordered.ToArray();
        }

        public static long NextVersion(long baseline, IEnumerable<string> exportedDirectories)
        {
            long maximum = baseline;
            foreach (string name in exportedDirectories)
                if (long.TryParse(Path.GetFileName(name), out long value)) maximum = Math.Max(maximum, value);
            return checked(maximum + 1);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Game.Framework.HotUpdate.Editor
{
    // Build-time graph, independent of Unity's assembly representation for deterministic validation.
    /// <summary>提供程序集计划的实现。</summary>
    public static class AssemblyPlan
    {
        /// <summary>根据传入参数取得指令。</summary>
        public static string[] Order(IReadOnlyDictionary<string, string[]> dependencies, string entry)
        {
            if (!dependencies.ContainsKey(entry)) throw new InvalidDataException("Entry assembly is outside the hot update folder: " + entry);
            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            var ordered = new List<string>();
            // 遍历程序集计划。
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

        /// <summary>根据传入参数取得下一个版本。</summary>
        public static long NextVersion(long baseline, IEnumerable<string> exportedDirectories)
        {
            long maximum = baseline;
            foreach (string name in exportedDirectories)
                if (long.TryParse(Path.GetFileName(name), out long value)) maximum = Math.Max(maximum, value);
            return checked(maximum + 1);
        }
    }
}

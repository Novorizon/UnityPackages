using System;
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet;
using HybridCLR.Commands;
using UnityEditor;

namespace Game.Framework.HotUpdate.Editor
{
    // A failed Unity compilation must never make a previous DLL look like a new patch.
    internal sealed class HotAssemblyCompilation : IDisposable
    {
        public string OutputPath { get; }

        public HotAssemblyCompilation(BuildTarget target, IReadOnlyList<string> assemblies)
        {
            OutputPath = Path.GetFullPath(Path.Combine("Library", "NovorizonHotUpdate", "Compile", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(OutputPath);
            try
            {
                CompileDllCommand.CompileDll(OutputPath, target, false);
                ValidateOutput(OutputPath, assemblies);
            }
            catch { Dispose(); throw; }
        }

        internal static void ClearDefaultOutput(string path)
        {
            string root = Path.GetFullPath("Library") + Path.DirectorySeparatorChar;
            ReleasePolicy.Require(Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase),
                "HybridCLR compilation output must be beneath the project Library folder.");
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }

        internal static void ValidateOutput(string path, IReadOnlyList<string> assemblies)
        {
            foreach (string name in assemblies)
            {
                string file = Path.Combine(path, name + ".dll");
                ReleasePolicy.Require(File.Exists(file), "Compilation did not produce " + name + ".dll. Check Unity compiler errors.");
                using (var module = ModuleDefMD.Load(File.ReadAllBytes(file)))
                    ReleasePolicy.Require(module.Assembly != null && module.Assembly.Name.ToString() == name,
                        "Compiled DLL identity does not match " + name + ".");
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(OutputPath)) Directory.Delete(OutputPath, true);
        }
    }
}

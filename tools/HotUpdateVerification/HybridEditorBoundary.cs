// Only native installation and Unity compilation commands are boundaries. Analyzers use upstream source.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using HybridCLR.Meta;
using HybridCLR.CodeGen.MethodBridge;

namespace HybridCLR.Settings
{
    public class HybridCLRSettings
    {
        public static HybridCLRSettings instance = new HybridCLRSettings();
        public int maxMethodBridgeGenericIteration = 10;
        public bool enable;
        public string hybridclrRepoURL, il2cppPlusRepoURL, outputLinkFile, outputAOTGenericReferenceFile;
        public string[] hotUpdateAssemblies = Array.Empty<string>(), preserveHotUpdateAssemblies, externalHotUpdateAssembliyDirs;
        public UnityEditorInternal.AssemblyDefinitionAsset[] hotUpdateAssemblyDefinitions;
        public static void Save()
        {
        }
    }
}

namespace HybridCLR
{
    public static class SettingsUtil
    {
        public static string GetAssembliesPostIl2CppStripDir(BuildTarget t) => "AOT";
        public static string GetHotUpdateDllsOutputDirByTarget(BuildTarget t) => "Library/HybridCLR/HotDlls";
        public static string GeneratedCppDir => "Generated";
        public static List<string> HotUpdateAssemblyNamesExcludePreserved => Settings.HybridCLRSettings.instance.hotUpdateAssemblies.ToList();
        public static List<string> HotUpdateAssemblyNamesIncludePreserved => HotUpdateAssemblyNamesExcludePreserved;

        public static string HybridCLRDataPathInPackage = "Packages/com.code-philosophy.hybridclr/Data~";
        public static Settings.HybridCLRSettings HybridCLRSettings => Settings.HybridCLRSettings.instance;
        public static string TemplatePathInPackage => "Templates";
    }
}

namespace HybridCLR.Installer
{
    public class InstallerController
    {
        public string PackageVersion => "9.0.3";
        public string InstalledLibil2cppVersion => "9.0.3";

        public bool HasInstalledHybridCLR() => true;
        public void InstallDefaultHybridCLR() => throw new NotSupportedException("Native installation is not covered by headless tests.");
    }
}

namespace HybridCLR.Commands
{
    public static class PrebuildCommand
    {
        public static void GenerateAll() => throw new NotSupportedException();
    }

    public static class CompileDllCommand
    {
        public static Action<string> CompileForTest;
        public static void CompileDll(BuildTarget t, bool d) => throw new NotSupportedException();
        public static void CompileDll(string path, BuildTarget t, bool d)
        {
            if (CompileForTest == null)
            {
                throw new NotSupportedException("Unity compilation is not covered by headless tests.");
            }

            CompileForTest(path);
        }
    }
}

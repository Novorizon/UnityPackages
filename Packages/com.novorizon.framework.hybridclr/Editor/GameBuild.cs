using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Framework.HotUpdate;
using HybridCLR;
using HybridCLR.CodeGen.AOT;
using HybridCLR.CodeGen.MethodBridge;
using HybridCLR.Commands;
using HybridCLR.HotUpdate;
using HybridCLR.Installer;
using HybridCLR.Meta;
using HybridCLR.Mod;
using HybridCLR.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Framework.HotUpdate.Editor
{
    public static class GameBuild
    {
        private const string BuiltinPath = "Assets/StreamingAssets/HotUpdate";
        private static string Root => Directory.GetParent(Application.dataPath).FullName;
        private static BuildTarget Target => EditorUserBuildSettings.activeBuildTarget;
        private static string[] HotAssemblies => DiscoverAssemblies();
        private static string[] Scenes => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

        [MenuItem("Tools/Novorizon/Hot Update/1. Setup Project")]
        public static void Configure()
        {
            const string settingsPath = "Assets/Resources/NovorizonHotUpdateSettings.json";
            if (!File.Exists(settingsPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllText(settingsPath, JsonUtility.ToJson(new UpdateSettings(), true));
            }

            var settings = JsonUtility.FromJson<UpdateSettings>(File.ReadAllText(settingsPath));
            settings.Validate();
            Directory.CreateDirectory(settings.hotUpdateRoot);
            bool hasDefinition = Directory.GetFiles(settings.hotUpdateRoot, "*.asmdef", SearchOption.AllDirectories).Length != 0;
            if (!hasDefinition)
            {
                File.WriteAllText(Path.Combine(settings.hotUpdateRoot, settings.entryAssembly + ".asmdef"), "{\"name\":\"" + settings.entryAssembly + "\",\"references\":[\"Game.Framework.HotUpdate\"]}");
                string template = Path.Combine(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GameBuild).Assembly).resolvedPath, "Editor/Templates/HotGame.cs.txt");
                int split = settings.entryType.LastIndexOf('.');
                string entryPath = Path.Combine(settings.hotUpdateRoot, settings.entryType.Substring(split + 1) + ".cs");
                if (!File.Exists(entryPath))
                {
                    File.WriteAllText(entryPath, File.ReadAllText(template).Replace("{{NAMESPACE}}", settings.entryType.Substring(0, split)).Replace("{{CLASS}}", settings.entryType.Substring(split + 1)));
                }
            }

            var hybrid = HybridCLRSettings.instance;
            hybrid.enable = true;
            hybrid.hybridclrRepoURL = "https://github.com/focus-creative-games/hybridclr";
            hybrid.il2cppPlusRepoURL = "https://github.com/focus-creative-games/il2cpp_plus";
            hybrid.outputLinkFile = "NovorizonHotUpdateGenerated/link.xml";
            // This report is for inspection, not an extra player script assembly.
            hybrid.outputAOTGenericReferenceFile = "../Library/NovorizonHotUpdate/AOTGenericReferences.cs";
            HybridCLRSettings.Save();
            AssetDatabase.Refresh();
            Debug.Log("PROJECT_CONFIGURED. Put game code and asmdefs beneath " + settings.hotUpdateRoot + ". Build New Player automatically configures assemblies and installs the matching native runtime.");
        }

        private static string[] DiscoverAssemblies()
        {
            var settings = UpdateSettings.Load();
            var player = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player);
            string prefix = Path.GetFullPath(settings.hotUpdateRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var selected = player.Where(a =>
            {
                string definition = UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(a.name);
                return !string.IsNullOrEmpty(definition) && Path.GetFullPath(definition).StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }).ToArray();
            var names = new HashSet<string>(selected.Select(a => a.name), StringComparer.Ordinal);
            foreach (var assembly in player.Where(a => !names.Contains(a.name)))
            {
                ReleasePolicy.Require(!assembly.assemblyReferences.Any(a => names.Contains(a.name)), "AOT assembly references hot code: " + assembly.name + ". Move the shared contract outside the hot update folder.");
            }

            return AssemblyPlan.Order(selected.ToDictionary(a => a.name, a => a.assemblyReferences.Select(d => d.name).ToArray(), StringComparer.Ordinal), settings.entryAssembly);
        }

        private static void ConfigureBuild()
        {
            ReleasePolicy.Require(!EditorApplication.isCompiling && !EditorApplication.isUpdating, "Wait for Unity compilation/import to finish, then build again.");
            var group = BuildPipeline.GetBuildTargetGroup(Target);
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(group, ManagedStrippingLevel.Low);
            if (Target == BuildTarget.Android)
            {
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            }

            EditorUserBuildSettings.development = false;
            var hybrid = HybridCLRSettings.instance;
            hybrid.hotUpdateAssemblies = HotAssemblies;
            hybrid.hotUpdateAssemblyDefinitions = Array.Empty<UnityEditorInternal.AssemblyDefinitionAsset>();
            hybrid.preserveHotUpdateAssemblies = Array.Empty<string>();
            HybridCLRSettings.Save();
            var settings = UpdateSettings.Load();
            var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == settings.entryAssembly);
            var entry = assembly?.GetType(settings.entryType, false);
            ReleasePolicy.Require(entry != null && typeof(IGameModule).IsAssignableFrom(entry) && !entry.IsAbstract && entry.IsPublic && entry.GetConstructor(Type.EmptyTypes) != null, "Entry must be a public IGameModule with a public parameterless constructor: " + settings.entryType);
            InstallRuntime();
        }

        [MenuItem("Tools/Novorizon/Hot Update/2. Build New Player")]
        public static void BuildNewPlayer()
        {
            Platform();
            RequireNativeSupport();
            ReleasePolicy.Require(Scenes.Length > 0, "Add a startup scene to Build Settings before building.");
            ConfigureBuild();
            PrepareBaseline();
            BuildPlayer();
        }

        [MenuItem("Tools/Novorizon/Hot Update/Advanced/Install Native Runtime")]
        public static void InstallRuntime()
        {
            var installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR() || installer.InstalledLibil2cppVersion != installer.PackageVersion)
            {
                installer.InstallDefaultHybridCLR();
            }

            Debug.Log("HYBRIDCLR_INSTALLED");
        }

        internal static string Platform()
        {
            switch (Target)
            {
                case BuildTarget.StandaloneWindows64:
                    return "windows-x64";
                case BuildTarget.Android:
                    return "android-arm64";
                case BuildTarget.iOS:
                    return "ios-arm64";
                default:
                    throw new BuildFailedException("Choose Windows x64, Android ARM64 or iOS ARM64.");
            }
        }

        [MenuItem("Tools/Novorizon/Hot Update/Advanced/Prepare Baseline")]
        public static void PrepareBaseline()
        {
            RequireConfigured();
            RequireNativeSupport();
            HotAssemblyCompilation.ClearDefaultOutput(SettingsUtil.GetHotUpdateDllsOutputDirByTarget(Target));
            PrebuildCommand.GenerateAll();
            string aot = SettingsUtil.GetAssembliesPostIl2CppStripDir(Target);
            string hot = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(Target);
            HotAssemblyCompilation.ValidateOutput(hot, HotAssemblies);
            var settings = UpdateSettings.Load();
            string id = Guid.NewGuid().ToString("N");
            // Never overwrite an existing baseline. Runtime reads only this explicitly selected baseline.
            string archive = Path.Combine(Root, "Artifacts", "Baselines", Platform(), id);
            Directory.CreateDirectory(Path.Combine(archive, "AOT"));
            foreach (string path in Directory.GetFiles(aot, "*.dll"))
            {
                File.Copy(path, Path.Combine(archive, "AOT", Path.GetFileName(path)), false);
            }

            File.Copy(Path.Combine(SettingsUtil.GeneratedCppDir, "MethodBridge.inc"), Path.Combine(archive, "MethodBridge.inc"));
            var manifest = new ReleaseManifest
            {
                appId = settings.appId,
                platform = Platform(),
                channel = settings.channel,
                baselineId = id,
                version = 1,
                assemblies = HotAssemblies.Select(n => Describe(Path.Combine(hot, n + ".dll"))).ToArray(),
                aotMetadata = RequiredAot(hot, aot).Select(n => Describe(Path.Combine(aot, n + ".dll"))).ToArray()
            };
            ReleasePolicy.Validate(manifest);
            string archiveJson = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(Path.Combine(archive, "baseline.json"), archiveJson, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(archive, "build-info.json"), JsonUtility.ToJson(new BuildInfo { unity = Application.unityVersion, hybridclr = new InstallerController().PackageVersion, target = Target.ToString(), development = false, entryAssembly = settings.entryAssembly, entryType = settings.entryType, assemblies = HotAssemblies, aotInputs = AotInputsFingerprint() }, true));
            Directory.CreateDirectory(BuiltinPath);
            // Delete only files owned by the previous manifest, never the whole StreamingAssets folder.
            string previous = Path.Combine(BuiltinPath, "builtin.json");
            if (File.Exists(previous))
            {
                var old = JsonUtility.FromJson<ReleaseManifest>(ReleasePolicy.ReadBoundedText(previous));
                ReleasePolicy.Validate(old);
                foreach (var file in old.assemblies.Concat(old.aotMetadata))
                {
                    File.Delete(Path.Combine(BuiltinPath, file.fileName));
                    File.Delete(Path.Combine(BuiltinPath, file.fileName) + ".meta");
                }
            }

            foreach (var file in manifest.assemblies)
            {
                File.Copy(Path.Combine(hot, file.assemblyName + ".dll"), Path.Combine(BuiltinPath, file.fileName), true);
            }

            foreach (var file in manifest.aotMetadata)
            {
                File.Copy(Path.Combine(aot, file.assemblyName + ".dll"), Path.Combine(BuiltinPath, file.fileName), true);
            }

            File.WriteAllText(previous, archiveJson, new UTF8Encoding(false));
            AssetDatabase.Refresh();
            Debug.Log("BASELINE_PREPARED " + archive);
        }

        [MenuItem("Tools/Novorizon/Hot Update/Advanced/Build Prepared Player")]
        public static void BuildPlayer()
        {
            RequireConfigured();
            RequireNativeSupport();
            var baseline = ReadBaseline();
            ValidateBuiltin(baseline);
            string output = Path.Combine(Root, "Builds", baseline.platform, baseline.baselineId);
            Directory.CreateDirectory(output);
            string location = Target == BuildTarget.StandaloneWindows64 ? Path.Combine(output, PlayerSettings.productName + ".exe") : Target == BuildTarget.Android ? Path.Combine(output, PlayerSettings.productName + ".aab") : Path.Combine(output, "Xcode");
            if (Target == BuildTarget.Android)
            {
                EditorUserBuildSettings.buildAppBundle = true;
            }

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = Scenes, locationPathName = location, target = Target, options = BuildOptions.StrictMode });
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException("Player build failed.");
            }

            VerifyBuiltBaseline(true);
            Debug.Log("PLAYER_BUILD_VERIFIED " + output);
        }

        internal static void VerifyBuiltBaseline(bool recordSuccess = false)
        {
            var baseline = ReadBaseline();
            string finalAot = SettingsUtil.GetAssembliesPostIl2CppStripDir(Target);
            string archive = Archive(baseline);
            ReleasePolicy.Require(new HashSet<string>(Directory.GetFiles(Path.Combine(archive, "AOT"), "*.dll").Select(Path.GetFileName)).SetEquals(Directory.GetFiles(finalAot, "*.dll").Select(Path.GetFileName)), "Final AOT assembly set changed; prepare a new baseline.");
            // Prevent distributing a player whose final stripped assemblies differ from the prepared baseline.
            foreach (string path in Directory.GetFiles(Path.Combine(archive, "AOT"), "*.dll"))
            {
                ReleasePolicy.Require(File.Exists(Path.Combine(finalAot, Path.GetFileName(path))) && ReleasePolicy.Sha256File(path) == ReleasePolicy.Sha256File(Path.Combine(finalAot, Path.GetFileName(path))), "Final AOT differs from prepared baseline. Do not ship this build; prepare a new baseline and rebuild.");
            }

            if (recordSuccess)
            {
                File.WriteAllText(Path.Combine(archive, "PLAYER_BUILD_VERIFIED"), "Final AOT matched prepared baseline and BuildPlayer succeeded.");
            }
        }

        [MenuItem("Tools/Novorizon/Hot Update/3. Export Patch")]
        public static void ExportUpdate()
        {
            RequireConfigured();
            var baseline = ReadBaseline();
            string versionRoot = Path.Combine(Root, "Artifacts", "Unsigned", baseline.platform, baseline.baselineId);
            long next = AssemblyPlan.NextVersion(baseline.version, Directory.Exists(versionRoot) ? Directory.GetDirectories(versionRoot) : Array.Empty<string>());
            string requested = Environment.GetEnvironmentVariable("HOTUPDATE_VERSION");
            long version = next;
            ReleasePolicy.Require(string.IsNullOrEmpty(requested) || (long.TryParse(requested, out version) && version >= next), "HOTUPDATE_VERSION must be greater than all previous exports for this baseline.");
            string archive = Archive(baseline);
            ReleasePolicy.Require(File.Exists(Path.Combine(archive, "PLAYER_BUILD_VERIFIED")), "Build and verify the baseline player first.");
            using var compiled = new HotAssemblyCompilation(Target, HotAssemblies);
            string hot = compiled.OutputPath;
            string aot = Path.Combine(archive, "AOT");
            var checker = new MissingMetadataChecker(aot, HotAssemblies);
            foreach (string name in HotAssemblies)
            {
                ReleasePolicy.Require(checker.Check(Path.Combine(hot, name + ".dll")), "Update calls stripped/missing AOT APIs.");
            }

            var includedAot = new HashSet<string>(baseline.aotMetadata.Select(f => f.assemblyName));
            ReleasePolicy.Require(RequiredAot(hot, aot).All(includedAot.Contains), "Update needs AOT metadata not shipped in this baseline. A new player is required.");
            string bridgeCheck = Path.Combine(Root, "Library", "GameBridgeCheck.inc");
            GenerateBridgeCheck(hot, aot, bridgeCheck);
            ReleasePolicy.Require(ReleasePolicy.Sha256File(bridgeCheck) == ReleasePolicy.Sha256File(Path.Combine(archive, "MethodBridge.inc")), "Native bridge requirements changed. A new player is required.");
            baseline.version = version;
            baseline.assemblies = HotAssemblies.Select(n => Describe(Path.Combine(hot, n + ".dll"))).ToArray();
            string output = Path.Combine(Root, "Artifacts", "Unsigned", baseline.platform, baseline.baselineId, version.ToString());
            ReleasePolicy.Require(!Directory.Exists(output), "Version output exists; export a new version.");
            Directory.CreateDirectory(output);
            foreach (var file in baseline.assemblies)
            {
                File.Copy(Path.Combine(hot, file.assemblyName + ".dll"), Path.Combine(output, file.fileName));
            }

            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonUtility.ToJson(baseline, true), new UTF8Encoding(false));
            Debug.Log("UNSIGNED_UPDATE_EXPORTED " + output + ". Sign it with this package's Tools~/ReleaseTool before publishing.");
        }

        private static List<string> RequiredAot(string hot, string aot)
        {
            var resolver = new CombinedAssemblyResolver(new FixedSetAssemblyResolver(hot, HotAssemblies), new PathAssemblyResolver(aot));
            var analyzer = new AOTReferenceAnalyzer(new AOTReferenceAnalyzer.Options { Collector = new AssemblyReferenceDeepCollector(resolver, HotAssemblies.ToList()), MaxIterationCount = 10 });
            analyzer.Run();
            return analyzer.AotGenericTypes.Select(t => t.Type.Module.Name.ToString()).Concat(analyzer.AotGenericMethods.Select(m => m.Method.Module.Name.ToString())).Select(Path.GetFileNameWithoutExtension).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        }

        private static void GenerateBridgeCheck(string hot, string aot, string output)
        {
            var analyzer = new MethodBridgeAnalyzer(new MethodBridgeAnalyzer.Options { Collector = new AssemblyReferenceDeepCollector(new PathAssemblyResolver(aot), Directory.GetFiles(aot, "*.dll").Select(Path.GetFileNameWithoutExtension).ToList()), MaxIterationCount = 10 });
            analyzer.Run();
            var cache = new AssemblyCache(new CombinedAssemblyResolver(new FixedSetAssemblyResolver(hot, HotAssemblies), new PathAssemblyResolver(aot)));
            var callbacks = new MonoPInvokeCallbackAnalyzer(cache, HotAssemblies.ToList());
            callbacks.Run();
            var calli = new CalliAnalyzer(cache, HotAssemblies.ToList());
            calli.Run();
            MethodBridgeGeneratorCommand.GenerateMethodBridgeCppFile(analyzer.GenericMethods, callbacks.ReversePInvokeMethods, calli.CalliMethodSignatures.ToList(), output);
        }

        internal static void RequireConfigured()
        {
            Platform();
            var installer = new InstallerController();
            ReleasePolicy.Require(installer.HasInstalledHybridCLR() && installer.PackageVersion == installer.InstalledLibil2cppVersion, "Build New Player installs the matching HybridCLR native runtime automatically.");
            var group = BuildPipeline.GetBuildTargetGroup(Target);
            ReleasePolicy.Require(PlayerSettings.GetScriptingBackend(group) == ScriptingImplementation.IL2CPP && !EditorUserBuildSettings.development && Scenes.Length > 0, "Configure project and use a release IL2CPP target.");
            ReleasePolicy.Require(Target != BuildTarget.Android || PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64, "This baseline requires Android ARM64 only. Use Build New Player to configure it.");
            ReleasePolicy.Require(SettingsUtil.HotUpdateAssemblyNamesExcludePreserved.SequenceEqual(HotAssemblies), "Unexpected HybridCLR assembly configuration.");
            UpdateSettings.Load().Validate();
        }

        private static void RequireNativeSupport()
        {
            if (Target == BuildTarget.StandaloneWindows64)
            {
                ReleasePolicy.Require(Directory.Exists(Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines/windowsstandalonesupport/Variations/win64_player_nondevelopment_il2cpp")), "Missing Windows Build Support (IL2CPP) for this Unity version. Add the module in Unity Hub before preparing/building a baseline.");
            }
        }

        private static ReleaseManifest ReadBaseline()
        {
            var baseline = JsonUtility.FromJson<ReleaseManifest>(ReleasePolicy.ReadBoundedText(Path.Combine(BuiltinPath, "builtin.json")));
            ReleasePolicy.Validate(baseline);
            var settings = UpdateSettings.Load();
            ReleasePolicy.Require(baseline.platform == Platform() && baseline.appId == settings.appId && baseline.channel == settings.channel, "Prepared baseline does not match build target/configuration.");
            var info = JsonUtility.FromJson<BuildInfo>(File.ReadAllText(Path.Combine(Archive(baseline), "build-info.json")));
            ReleasePolicy.Require(info.unity == Application.unityVersion && info.hybridclr == new InstallerController().PackageVersion && info.entryAssembly == settings.entryAssembly && info.entryType == settings.entryType && info.assemblies != null && info.assemblies.SequenceEqual(HotAssemblies) && info.aotInputs == AotInputsFingerprint(), "Unity, HybridCLR, AOT inputs, entry or assembly topology changed; create a new player baseline.");
            return baseline;
        }

        private static string AotInputsFingerprint()
        {
            var hot = new HashSet<string>(HotAssemblies);
            var player = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player);
            var sources = player.Where(a => !hot.Contains(a.name)).SelectMany(a => a.sourceFiles);
            var references = player.SelectMany(a => a.compiledAssemblyReferences);
            var files = sources.Concat(references).Concat(new[] { "Packages/manifest.json", "ProjectSettings/ProjectSettings.asset" }).Concat(new[] { "Assets/Resources/NovorizonHotUpdateSettings.json" }).Concat(Directory.GetFiles("Assets", "*.xml", SearchOption.AllDirectories)).Where(File.Exists).Distinct().Select(path => new { Name = path.Replace('\\', '/').Replace(Root.Replace('\\', '/') + "/", ""), Hash = ReleasePolicy.Sha256File(path) }).OrderBy(f => f.Name, StringComparer.Ordinal);
            return ReleasePolicy.Sha256(Encoding.UTF8.GetBytes(string.Join("\n", files.Select(f => f.Name + ":" + f.Hash))));
        }

        private static string Archive(ReleaseManifest baseline) => Path.Combine(Root, "Artifacts", "Baselines", baseline.platform, baseline.baselineId);
        private static ReleaseFile Describe(string path) => new ReleaseFile
        {
            assemblyName = Path.GetFileNameWithoutExtension(path),
            fileName = Path.GetFileName(path) + ".bytes",
            size = new FileInfo(path).Length,
            sha256 = ReleasePolicy.Sha256File(path)
        };
        internal static void ValidateBuiltin(ReleaseManifest baseline)
        {
            foreach (var file in baseline.assemblies.Concat(baseline.aotMetadata))
            {
                ReleasePolicy.VerifyFile(Path.Combine(BuiltinPath, file.fileName), file);
            }
        }

        public static void ValidateProject()
        {
            Configure();
            ReleasePolicy.Require(AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == UpdateSettings.Load().entryAssembly), "Missing editor hot assembly.");
            Debug.Log("PROJECT_VALIDATION_OK");
        }

        public static void ValidatePlayerScripts()
        {
            Configure();
            using var compiled = new HotAssemblyCompilation(Target, HotAssemblies);
            string output = compiled.OutputPath;
            foreach (string name in HotAssemblies.Concat(new[] { "Game.Framework.HotUpdate" }))
            {
                ReleasePolicy.Require(File.Exists(Path.Combine(output, name + ".dll")), "Missing player script assembly: " + name);
            }

            Debug.Log("PLAYER_SCRIPTS_COMPILED");
        }

        public static void SmokeEditorStartup()
        {
            EditorSmokeProbe.Begin();
            ReleasePolicy.Require(Scenes.Length > 0, "Add a startup scene to Build Settings.");
            EditorSceneManager.OpenScene(Scenes[0]);
            EditorApplication.isPlaying = true;
        // HotGameBootstrap quits batch mode after either STARTUP_OK or a startup failure.
        }

        [Serializable]
        private sealed class BuildInfo
        {
            public string unity;
            public string hybridclr;
            public string target;
            public bool development;
            public string entryAssembly;
            public string entryType;
            public string[] assemblies;
            public string aotInputs;
        }
    }

    [InitializeOnLoad]
    internal static class EditorSmokeProbe
    {
        private const string Flag = "Game.EditorSmokeRunning";
        private const string Deadline = "Game.EditorSmokeDeadline";
        static EditorSmokeProbe()
        {
            if (SessionState.GetBool(Flag, false))
            {
                EditorApplication.update += Poll;
            }
        }

        internal static void Begin()
        {
            SessionState.SetBool(Flag, true);
            SessionState.SetString(Deadline, DateTime.UtcNow.AddSeconds(45).Ticks.ToString());
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            bool ready = EditorApplication.isPlaying && HotGameBootstrap.Status.StartsWith("Ready", StringComparison.Ordinal);
            bool failed = HotGameBootstrap.Failed;
            bool expired = DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Deadline, "0"));
            if (!ready && !failed && !expired)
            {
                return;
            }

            SessionState.SetBool(Flag, false);
            EditorApplication.update -= Poll;
            if (expired)
            {
                Debug.LogError("Editor startup smoke test timed out.");
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(ready ? 0 : 1);
            }
            else
            {
                EditorApplication.isPlaying = false;
            }
        }
    }

    // Protect builds launched through Unity's default Build Settings as well as our own menu.
    public sealed class ReleaseBuildGuard : IPreprocessBuildWithReport
    {
        public int callbackOrder => -100;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!Resources.Load<TextAsset>(UpdateSettings.ResourceName))
            {
                return;
            }

            // HybridCLR exports a temporary player while generating AOT assemblies.
            if (report.summary.outputPath.Replace('\\', '/').Contains("Temp/HybridCLR/StrippedAOTDllsTempProj/"))
            {
                return;
            }

            var settings = UpdateSettings.Load();
            ReleasePolicy.Require(!report.summary.options.HasFlag(BuildOptions.Development), "Use the release build workflow.");
            GameBuild.RequireConfigured();
            ReleasePolicy.Require(settings.channel != "production" || settings.appId != "com.example.hybridclrgame", "Production channel needs a real application ID.");
            string path = "Assets/StreamingAssets/HotUpdate/builtin.json";
            ReleasePolicy.Require(File.Exists(path), "Prepare a player baseline first.");
            var baseline = JsonUtility.FromJson<ReleaseManifest>(ReleasePolicy.ReadBoundedText(path));
            ReleasePolicy.Validate(baseline);
            ReleasePolicy.Require(baseline.platform == GameBuild.Platform() && baseline.appId == settings.appId && baseline.channel == settings.channel, "Prepared baseline does not match build target/configuration.");
            GameBuild.ValidateBuiltin(baseline);
        }
    }

    public sealed class ReleaseBuildVerifier : IPostprocessBuildWithReport
    {
        public int callbackOrder => 10000;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!Resources.Load<TextAsset>(UpdateSettings.ResourceName))
            {
                return;
            }

            if (report.summary.outputPath.Replace('\\', '/').Contains("Temp/HybridCLR/StrippedAOTDllsTempProj/"))
            {
                return;
            }

            if (report.summary.result == BuildResult.Failed || report.summary.result == BuildResult.Cancelled)
            {
                return;
            }

            GameBuild.VerifyBuiltBaseline();
        }
    }
}

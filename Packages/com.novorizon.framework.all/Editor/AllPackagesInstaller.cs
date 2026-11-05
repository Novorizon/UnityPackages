using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Game.Framework.All.Editor
{
    [InitializeOnLoad]
    public static class AllPackagesInstaller
    {
        internal const string PackageName = "com.novorizon.framework.all";
        private const string SetupType = "Game.Framework.HotUpdate.Editor.GameBuild";
        private static PackageCatalog catalog;
        private static InstallState state;
        private static string packageRoot, projectRoot, release;
        private static ListRequest listing;
        private static AddAndRemoveRequest adding;
        private static EmbedRequest embedding;
        private static bool blocked, batch, verifyAfterAdd;
        private static string StatePath => Path.Combine(projectRoot, "ProjectSettings/NovorizonFrameworkAll.install.json");
        private static string ReportPath => Path.Combine(projectRoot, "Library/NovorizonFrameworkAll/verification.json");

        [Serializable]
        private sealed class InstallState
        {
            public string release;
            public string phase = "Install";
            public string error;
            public bool addSubmitted;
            public bool embedSubmitted;
            public long deadline;
        }

        static AllPackagesInstaller()
        {
            EditorApplication.update += Tick;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            if (state == null || state.phase == "Complete" || state.phase == "Failed" || !messages.Any(m => m.type == CompilerMessageType.Error)) return;
            state.error = "Unity script compilation failed: " + Path.GetFileName(assemblyPath) + ". Fix Console errors before retrying.";
            state.phase = "Failed";
            Save();
            Debug.LogError("[Novorizon All] " + state.error);
            ExitBatch(1);
        }

        private static bool Load()
        {
            if (state != null) return true;
            var owner = PackageInfo.FindForAssembly(typeof(AllPackagesInstaller).Assembly);
            if (owner == null) return false; // Registration can lag assembly reload.
            packageRoot = owner.resolvedPath;
            projectRoot = Directory.GetParent(Application.dataPath).FullName;
            release = owner.version;
            catalog = JsonUtility.FromJson<PackageCatalog>(File.ReadAllText(Path.Combine(packageRoot, "Dependencies.json")));
            catalog.Validate();
            state = File.Exists(StatePath) ? JsonUtility.FromJson<InstallState>(File.ReadAllText(StatePath)) : null;
            if (state == null || state.release != release) { state = new InstallState { release = release }; Save(); }
            return true;
        }

        private static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath));
            string temporary = StatePath + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(state));
            if (File.Exists(StatePath)) File.Replace(temporary, StatePath, null);
            else File.Move(temporary, StatePath);
        }

        private static void Advance(string phase)
        {
            state.phase = phase;
            state.deadline = DateTime.UtcNow.AddMinutes(5).Ticks;
            Save();
            Debug.Log("[Novorizon All] " + phase);
        }

        private static void Tick()
        {
            if (blocked || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                if (!Load()) return;
                if (state.phase == "Failed") { ExitBatch(1); return; }
                if (state.phase == "Complete") { if (batch) VerifyBatch(); return; }
                if (state.phase == "Configure") { Configure(); return; }
                if (adding != null)
                {
                    if (!adding.IsCompleted) return;
                    if (adding.Status == StatusCode.Failure) throw new InvalidOperationException(adding.Error.message);
                    adding = null;
                    verifyAfterAdd = true;
                }
                if (embedding != null)
                {
                    if (!embedding.IsCompleted) return;
                    if (embedding.Status == StatusCode.Failure) throw new InvalidOperationException(embedding.Error.message);
                    embedding = null;
                }
                // A domain reload loses managed request handles. Observe registered packages
                // without issuing a second UPM operation while the native request may still run.
                if ((state.phase == "Install" && state.addSubmitted && !verifyAfterAdd) || (state.phase == "Embed" && state.embedSubmitted))
                {
                    var registered = PackageInfo.GetAllRegisteredPackages();
                    if (state.phase == "Install" && catalog.packages.All(p => registered.Any(i => i.name == p.name && i.version == p.version)) && !registered.Any(i => (catalog.obsoletePackages ?? Array.Empty<string>()).Contains(i.name))) Advance("Embed");
                    else if (state.phase == "Embed" && catalog.packages.Where(p => p.embed).All(p => registered.Any(i => i.name == p.name && i.version == p.version && i.source == PackageSource.Embedded))) Advance("Configure");
                    else if (DateTime.UtcNow.Ticks > state.deadline) throw new TimeoutException("The previous UPM operation did not finish. Check Package Manager and retry installation.");
                    return;
                }
                if (listing == null) { listing = Client.List(true, true); return; }
                if (!listing.IsCompleted) return;
                if (listing.Status == StatusCode.Failure) throw new InvalidOperationException(listing.Error.message);
                var installed = listing.Result.ToArray();
                listing = null;
                if (state.phase == "Install") Install(installed);
                else if (state.phase == "Embed") Embed(installed);
                else throw new InvalidDataException("Unknown install phase: " + state.phase);
            }
            catch (Exception exception)
            {
                if (state == null) { blocked = true; Debug.LogException(exception); ExitBatch(1); return; }
                state.error = exception.GetBaseException().Message;
                state.phase = "Failed";
                adding = null; embedding = null; listing = null;
                Save();
                Debug.LogError("[Novorizon All] 安装中止：" + state.error + "。修复原因后使用 Tools/Novorizon/All Packages/Retry Installation；已安装的包保留。");
                ExitBatch(1);
            }
        }

        private static void Install(PackageInfo[] installed)
        {
            var missing = catalog.packages.Where(p => !installed.Any(i => i.name == p.name && i.version == p.version)).ToArray();
            var obsolete = installed.Where(i => (catalog.obsoletePackages ?? Array.Empty<string>()).Contains(i.name)).ToArray();
            if (missing.Length == 0 && obsolete.Length == 0) { Advance("Embed"); return; }
            if (obsolete.Any(i => i.source == PackageSource.Embedded))
                throw new InvalidOperationException("Legacy embedded package must be moved out before migration: " + string.Join(", ", obsolete.Where(i => i.source == PackageSource.Embedded).Select(i => i.name)));
            if (state.addSubmitted) throw new InvalidOperationException("UPM did not resolve the requested versions: " + string.Join(", ", missing.Select(p => p.name + "@" + p.version)));
            foreach (var package in missing)
                if (installed.Any(i => i.name == package.name && i.source == PackageSource.Embedded))
                    throw new InvalidOperationException("Existing embedded package has a different version: " + package.name + ". Upgrade or move that embedded package before retrying.");
            var sources = missing.Select(p => p.PrepareSource(packageRoot, projectRoot, release)).ToArray();
            // One resolver operation: unpublished package dependencies are present together.
            state.addSubmitted = true;
            state.deadline = DateTime.UtcNow.AddMinutes(10).Ticks;
            Save();
            Debug.Log("[Novorizon All] 正在安装全部模块及其依赖，请等待 UPM 与脚本编译。");
            adding = Client.AddAndRemove(sources, obsolete.Select(i => i.name).ToArray());
        }

        private static void Embed(PackageInfo[] installed)
        {
            foreach (var package in catalog.packages.Where(p => p.embed))
            {
                var info = installed.FirstOrDefault(i => i.name == package.name && i.version == package.version);
                if (info == null) throw new InvalidOperationException("Package disappeared before embed: " + package.name);
                string target = Path.Combine(projectRoot, "Packages", package.name);
                if (info.source == PackageSource.Embedded && Directory.Exists(target)) continue;
                if (state.embedSubmitted)
                {
                    if (DateTime.UtcNow.Ticks > state.deadline) throw new TimeoutException("Embedding did not finish: " + package.name);
                    return;
                }
                if (!PackageInfo.GetAllRegisteredPackages().Any(i => i.name == package.name))
                {
                    if (DateTime.UtcNow.Ticks > state.deadline) throw new TimeoutException("Package is not registered for embedding: " + package.name);
                    return;
                }
                state.embedSubmitted = true;
                Save();
                embedding = Client.Embed(package.name); // Official HybridCLR 9.0.3 requires its physical Packages/.../Data~.
                return;
            }
            Advance("Configure");
        }

        private static void Configure()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(SetupType, false)).FirstOrDefault(t => t != null);
            if (type == null)
            {
                if (DateTime.UtcNow.Ticks > state.deadline) throw new TimeoutException("HybridCLR editor assembly did not load. Check Unity compilation errors.");
                return;
            }
            var configure = type.GetMethod("Configure", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (configure == null) throw new MissingMethodException(SetupType, "Configure");
            configure.Invoke(null, null); // Idempotent: preserves user settings, hot code and scenes.
            Advance("Complete");
            Debug.Log("[Novorizon All] 全部模块安装完成，HybridCLR 默认配置和热更入口已就绪。");
        }

        [MenuItem("Tools/Novorizon/All Packages/Retry Installation")]
        public static void RetryInstallation()
        {
            if ((adding != null && !adding.IsCompleted) || (embedding != null && !embedding.IsCompleted) || (listing != null && !listing.IsCompleted)) { Debug.Log("[Novorizon All] 请等待当前 UPM 操作完成后重试。"); return; }
            if (state != null && state.phase != "Failed" && state.phase != "Complete") { Debug.Log("[Novorizon All] 安装正在进行。"); return; }
            blocked = false;
            if (!Load()) return;
            state = new InstallState { release = release };
            listing = null; adding = null; embedding = null; verifyAfterAdd = false;
            Save();
        }

        // Unity CLI entry point. Do not pass -quit while UPM is running.
        public static void InstallBatch() { batch = true; Tick(); }

        [Serializable]
        private sealed class VerificationReport
        {
            public string release;
            public string phase;
            public PackageDependency[] packages;
            public bool hybridClrEmbedded;
            public bool hotUpdateSettingsCreated;
        }

        private static void VerifyBatch()
        {
            var installed = PackageInfo.GetAllRegisteredPackages();
            bool all = catalog.packages.All(p => installed.Any(i => i.name == p.name && i.version == p.version)) && !installed.Any(i => (catalog.obsoletePackages ?? Array.Empty<string>()).Contains(i.name));
            bool embedded = installed.Any(p => p.name == "com.code-philosophy.hybridclr" && p.source == PackageSource.Embedded);
            bool configured = File.Exists(Path.Combine(projectRoot, "Assets/Resources/NovorizonHotUpdateSettings.json"));
            if (!all || !embedded || !configured) throw new InvalidOperationException("Batch verification failed: packages, HybridCLR embed or settings are missing.");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(new VerificationReport { release = release, phase = state.phase, packages = catalog.packages, hybridClrEmbedded = embedded, hotUpdateSettingsCreated = configured }));
            ExitBatch(0);
        }

        private static void ExitBatch(int code) { if (batch) { batch = false; EditorApplication.Exit(code); } }
    }
}

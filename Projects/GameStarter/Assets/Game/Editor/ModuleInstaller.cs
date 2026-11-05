using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Game.Editor
{
    [InitializeOnLoad]
    internal static class ModuleInstaller
    {
        private const string JobKey = "Novorizon.GameStarter.InstallJob";
        private const string MessageKey = "Novorizon.GameStarter.InstallMessage";
        internal const string CatalogPath = "Assets/Settings/OptionalModules.json";
        private static ListRequest listing;
        private static AddAndRemoveRequest adding;
        private static EmbedRequest embedding;
        private static InstallJob job;
        internal static bool Busy => job != null;
        internal static string Message => SessionState.GetString(MessageKey, "选择需要的模块，然后安装。");

        [Serializable]
        private sealed class InstallJob
        {
            public ModuleSpec[] plan;
            public int index;
            public int setupIndex;
            public bool awaitingAdd;
            public long setupDeadline;
        }

        static ModuleInstaller()
        {
            string saved = SessionState.GetString(JobKey, "");
            if (saved.Length > 0) job = JsonUtility.FromJson<InstallJob>(saved);
            EditorApplication.update += Tick;
        }

        internal static ModuleCatalog ReadCatalog() => JsonUtility.FromJson<ModuleCatalog>(File.ReadAllText(CatalogPath));

        internal static void Install(string[] selection)
        {
            if (Busy) throw new InvalidOperationException("An installation is already running.");
            var plan = ReadCatalog().Plan(selection);
            if (plan.Length == 0) return;
            // Validate all local archives before changing the package manifest.
            foreach (var module in plan) module.ResolveSource(Directory.GetCurrentDirectory());
            job = new InstallJob { plan = plan };
            Save("开始安装所选模块及其依赖。");
        }

        private static void Save(string message)
        {
            SessionState.SetString(JobKey, JsonUtility.ToJson(job));
            SessionState.SetString(MessageKey, message);
        }

        private static void Finish(string message)
        {
            job = null;
            listing = null;
            adding = null;
            embedding = null;
            SessionState.EraseString(JobKey);
            SessionState.SetString(MessageKey, message);
            Debug.Log(message);
        }

        private static void Tick()
        {
            if (job == null || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try
            {
                if (job.index == job.plan.Length) { ConfigureModules(); return; }
                var module = job.plan[job.index];
                if (embedding != null)
                {
                    if (!embedding.IsCompleted) return;
                    if (embedding.Status == StatusCode.Failure) throw new InvalidOperationException(embedding.Error.message);
                    embedding = null;
                    listing = null;
                    return;
                }
                if (adding != null)
                {
                    if (!adding.IsCompleted) return;
                    if (adding.Status == StatusCode.Failure) throw new InvalidOperationException(adding.Error.message);
                    adding = null;
                    listing = null;
                    return; // Re-list to verify the resolved package, including after a domain reload.
                }
                if (listing == null) { listing = Client.List(true, true); return; }
                if (!listing.IsCompleted) return;
                if (listing.Status == StatusCode.Failure) throw new InvalidOperationException(listing.Error.message);
                var packages = listing.Result.ToArray();
                var installed = packages.FirstOrDefault(p => p.name == module.id);
                var obsolete = packages.Where(p => (module.replaces ?? Array.Empty<string>()).Contains(p.name)).Select(p => p.name).ToArray();
                listing = null;
                if (installed == null || installed.version != module.version || obsolete.Length > 0)
                {
                    if (job.awaitingAdd) throw new InvalidOperationException("Resolved version mismatch for " + module.id + "; expected " + module.version);
                    job.awaitingAdd = true;
                    Save("正在安装 " + module.title);
                    adding = Client.AddAndRemove(new[] { module.ResolveSource(Directory.GetCurrentDirectory()) }, obsolete);
                    return;
                }
                if (module.embed && !Directory.Exists(Path.Combine("Packages", module.id)))
                {
                    Save("正在嵌入 " + module.title);
                    embedding = Client.Embed(module.id);
                    return;
                }
                job.index++;
                job.awaitingAdd = false;
                Save("已安装 " + module.title);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish("安装中止：" + exception.Message + "。已安装的包保留；修复后可重新安装。");
            }
        }

        private static void ConfigureModules()
        {
            if (job.setupDeadline == 0)
            {
                job.setupDeadline = DateTime.UtcNow.AddMinutes(5).Ticks;
                Save("等待模块编辑器程序集，然后执行初始化配置。");
            }
            for (; job.setupIndex < job.plan.Length; job.setupIndex++)
            {
                var module = job.plan[job.setupIndex];
                if (string.IsNullOrEmpty(module.setupType)) continue;
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(module.setupType, false)).FirstOrDefault(t => t != null);
                if (type == null)
                {
                    if (DateTime.UtcNow.Ticks > job.setupDeadline) throw new InvalidOperationException("Editor assembly did not load: " + module.setupType);
                    return;
                }
                var method = type.GetMethod(module.setupMethod, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (method == null) throw new MissingMethodException(module.setupType, module.setupMethod);
                method.Invoke(null, null);
                // Setup methods must be idempotent if they trigger a domain reload before this save.
                Save("已配置 " + module.title);
            }
            Finish("模块安装完成。可在 Package Manager 导入对应 Samples。" +
                (job.plan.Any(m => m.id == "com.novorizon.framework.hybridclr") ? "HybridCLR 已生成默认配置和热更入口。" : ""));
        }
    }
}

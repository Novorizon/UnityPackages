using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Game;
using Game.Editor;
using Game.Framework;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UObject = UnityEngine.Object;

internal static class Program
{
    private static int passed;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Main()
    {
        Check("empty startup", () => { using var s = Session(); s.StartAsync(Array.Empty<IStartupModule>()).GetAwaiter().GetResult(); Equal(StartupState.Ready, s.State); });
        Check("ordered async startup and reverse shutdown", Ordered);
        Check("partial initialization failure releases the failing module", StartupFailure);
        Check("shutdown during initialization prevents later startup", CancelStartup);
        Check("reentrant startup does not initialize twice", ReentrantStart);
        Check("tick failure stops updates and cleans all modules", TickFailure);
        Check("shutdown from update skips remaining updates", TickShutdown);
        Check("cleanup continues after a shutdown exception", CleanupFailure);
        Check("duplicate and null modules rejected before side effects", InvalidModules);
        Check("normal updates do not allocate", NoFrameAllocations);
        Check("entry uses actual Framework singleton and rejects duplicates", EntrySingleton);
        Check("entry restarts when scene/domain reload is disabled", EntryRestart);
        Check("catalog resolves dependencies once and before consumers", CatalogOrder);
        Check("catalog detects missing and circular dependencies", InvalidCatalog);
        Check("archive integrity and online fallback", ArchiveIntegrity);
        Check("installer resumes after request handles are lost on reload", InstallerReload);
        Check("installer retains installed packages after an add failure", InstallerFailure);
        Check("installer verifies the resolved version", InstallerVersion);
        Check("HybridCLR embed request completes before installing consumers", InstallerEmbed);
        Check("installer waits for compilation and play mode", InstallerWaits);
        Check("installer configures only after all dependencies exist", InstallerSetup);
        Console.WriteLine($"PASS {passed} lifecycle, package planning and installer scenarios (Unity/UPM API models; not native Unity).");
    }

    private static void Check(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Console.Error.WriteLine("FAIL " + name + ": " + ex); Environment.Exit(1); }
    }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static GameSession Session(List<Exception> errors = null) => new GameSession(e => errors?.Add(e));
    private sealed class Module : IStartupModule
    {
        internal Func<CancellationToken, Task> Init = _ => Task.CompletedTask;
        internal Action Update = () => { }, Stop = () => { };
        public Task InitializeAsync(CancellationToken token) => Init(token);
        public void Tick(float delta, float unscaled) => Update();
        public void Shutdown() => Stop();
    }
    private static void Ordered()
    {
        var events = new List<string>();
        var wait = new TaskCompletionSource<bool>();
        var a = new Module { Init = _ => { events.Add("a+"); return wait.Task; }, Stop = () => events.Add("a-") };
        var b = new Module { Init = _ => { events.Add("b+"); return Task.CompletedTask; }, Stop = () => events.Add("b-") };
        using var s = Session();
        Task start = s.StartAsync(new[] { a, b });
        Equal("a+", string.Join(",", events));
        Equal(StartupState.Starting, s.State);
        wait.SetResult(true);
        start.GetAwaiter().GetResult();
        s.Dispose(); s.Dispose();
        Equal("a+,b+,b-,a-", string.Join(",", events));
    }
    private static void StartupFailure()
    {
        var events = new List<int>();
        bool canceled = false;
        var a = new Module { Init = t => { t.Register(() => canceled = true); return Task.CompletedTask; }, Stop = () => events.Add(1) };
        var b = new Module { Init = _ => Task.FromException(new InvalidOperationException()), Stop = () => events.Add(2) };
        var c = new Module { Init = _ => throw new Exception("must not start") };
        using var s = Session();
        Throws<InvalidOperationException>(() => s.StartAsync(new[] { a, b, c }).GetAwaiter().GetResult());
        Equal(StartupState.Failed, s.State); Equal("2,1", string.Join(",", events)); True(canceled);
    }
    private static void CancelStartup()
    {
        var wait = new TaskCompletionSource<bool>();
        int stopped = 0, later = 0;
        var a = new Module { Init = _ => wait.Task, Stop = () => stopped++ };
        var b = new Module { Init = _ => { later++; return Task.CompletedTask; } };
        using var s = Session();
        Task start = s.StartAsync(new[] { a, b });
        s.Dispose(); wait.SetResult(true);
        Throws<OperationCanceledException>(() => start.GetAwaiter().GetResult());
        Equal(StartupState.Stopped, s.State); Equal(1, stopped); Equal(0, later);
    }
    private static void ReentrantStart()
    {
        using var s = Session(); int count = 0; Task inner = null;
        var a = new Module { Init = _ => { count++; inner = s.StartAsync(Array.Empty<IStartupModule>()); return Task.CompletedTask; } };
        Task outer = s.StartAsync(new[] { a }); outer.GetAwaiter().GetResult(); True(ReferenceEquals(inner, outer)); Equal(1, count);
    }
    private static void TickFailure()
    {
        int stopped = 0, later = 0; var errors = new List<Exception>();
        using var s = Session(errors);
        s.StartAsync(new[] { new Module { Update = () => throw new InvalidOperationException(), Stop = () => stopped++ }, new Module { Update = () => later++, Stop = () => stopped++ } }).GetAwaiter().GetResult();
        s.Tick(1, 1); s.Tick(1, 1);
        Equal(StartupState.Failed, s.State); Equal(2, stopped); Equal(0, later); Equal(1, errors.Count);
    }
    private static void TickShutdown()
    {
        using var s = Session(); int later = 0;
        s.StartAsync(new[] { new Module { Update = s.Dispose }, new Module { Update = () => later++ } }).GetAwaiter().GetResult();
        s.Tick(1, 1); Equal(0, later); Equal(StartupState.Stopped, s.State);
    }
    private static void CleanupFailure()
    {
        int stopped = 0; var errors = new List<Exception>(); using var s = Session(errors);
        s.StartAsync(new[] { new Module { Stop = () => stopped++ }, new Module { Stop = () => throw new Exception("cleanup") } }).GetAwaiter().GetResult();
        s.Dispose(); Equal(1, stopped); Equal(1, errors.Count);
    }
    private static void InvalidModules()
    {
        using var s = Session(); var a = new Module { Init = _ => throw new Exception("must not start") };
        Throws<ArgumentException>(() => s.StartAsync(new[] { a, a }));
        Throws<ArgumentException>(() => s.StartAsync(new Module[] { a, null }));
        Equal(StartupState.NotStarted, s.State);
    }
    private static void NoFrameAllocations()
    {
        using var s = Session(); s.StartAsync(new[] { new Module() }).GetAwaiter().GetResult();
        s.Tick(1, 1); long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) s.Tick(1, 1);
        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
    private static void ResetEntry()
    {
        UObject.ClearForTest();
        typeof(GameEntry).GetMethod("ResetSession", Static).Invoke(null, null);
        var lifecycle = typeof(MonoSingleton<>).Assembly.GetType("Game.Framework.MonoSingletonLifecycle");
        lifecycle.GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        UnityThread.Reset(); UnityThread.InitializeManual();
    }
    private static void EntrySingleton()
    {
        ResetEntry(); var first = new GameObject().AddComponent<GameEntry>();
        Equal(StartupState.Ready, first.State); True(ReferenceEquals(first, GameEntry.Instance));
        var second = new GameObject().AddComponent<GameEntry>();
        True(second == null); True(ReferenceEquals(first, GameEntry.Instance));
        UObject.Destroy(first.gameObject); True(!GameEntry.TryGetInstance(out _)); ResetEntry();
    }
    private static void EntryRestart()
    {
        ResetEntry(); var entry = new GameObject().AddComponent<GameEntry>();
        // Stop sends OnApplicationQuit, which clears sessionOwner before the next play session.
        UObject.Invoke(entry, "OnApplicationQuit");
        typeof(GameEntry).GetMethod("ResetSession", Static).Invoke(null, null);
        typeof(MonoSingleton<>).Assembly.GetType("Game.Framework.MonoSingletonLifecycle").GetMethod("Reset", Static).Invoke(null, null);
        Equal(StartupState.NotStarted, entry.State);
        typeof(GameEntry).GetMethod("ResumeSession", Static).Invoke(null, null);
        True(ReferenceEquals(entry, GameEntry.Instance)); Equal(StartupState.Ready, entry.State); ResetEntry();
    }
    private static ModuleSpec Spec(string id, params string[] dependencies) => new ModuleSpec { id = id, title = id, version = "1.0.0", source = "git:" + id, dependencies = dependencies };
    private static void CatalogOrder()
    {
        var c = ModuleInstaller.ReadCatalog();
        var plan = c.Plan(new[] { "com.novorizon.framework.hybridclr", "com.novorizon.framework.ui" });
        Equal("com.novorizon.framework", plan[0].id);
        True(Array.FindIndex(plan, m => m.id == "com.code-philosophy.hybridclr") < Array.FindIndex(plan, m => m.id == "com.novorizon.framework.hybridclr"));
        Equal(4, plan.Length);
        True(c.Plan(new[] { "com.novorizon.ability" }).All(m => !m.id.Contains("ecs")));
    }
    private static void InvalidCatalog()
    {
        var c = new ModuleCatalog { modules = new[] { Spec("a", "missing") } }; Throws<InvalidDataException>(() => c.Plan(new[] { "a" }));
        c.modules = new[] { Spec("a", "b"), Spec("b", "a") }; Throws<InvalidDataException>(() => c.Plan(new[] { "a" }));
        c.modules = new[] { Spec("a"), Spec("a") }; Throws<InvalidDataException>(() => c.Plan(new[] { "a" }));
    }
    private static void ArchiveIntegrity()
    {
        InProject(() =>
        {
            var spec = Spec("a"); spec.archive = "a.tgz";
            Equal(spec.source, spec.ResolveSource(Directory.GetCurrentDirectory()));
            Directory.CreateDirectory("Packages/OptionalArchives~"); File.WriteAllText("Packages/OptionalArchives~/a.tgz", "payload");
            spec.sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("Packages/OptionalArchives~/a.tgz"))).ToLowerInvariant();
            Equal("file:OptionalArchives~/a.tgz", spec.ResolveSource(Directory.GetCurrentDirectory()));
            File.AppendAllText("Packages/OptionalArchives~/a.tgz", "corrupt"); Throws<InvalidDataException>(() => spec.ResolveSource(Directory.GetCurrentDirectory()));
        });
    }
    private static void InProject(Action action)
    {
        string original = Directory.GetCurrentDirectory(), temp = Path.Combine(Path.GetTempPath(), "starter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "Assets/Settings"));
        SessionState.ClearForTest(); Client.ClearForTest();
        try { Directory.SetCurrentDirectory(temp); action(); }
        finally
        {
            typeof(ModuleInstaller).GetField("job", Static).SetValue(null, null);
            typeof(ModuleInstaller).GetField("adding", Static).SetValue(null, null);
            typeof(ModuleInstaller).GetField("listing", Static).SetValue(null, null);
            typeof(ModuleInstaller).GetField("embedding", Static).SetValue(null, null);
            EditorApplication.isCompiling = EditorApplication.isPlayingOrWillChangePlaymode = false;
            Directory.SetCurrentDirectory(original); Directory.Delete(temp, true);
        }
    }
    private static void WriteCatalog(params ModuleSpec[] specs)
    {
        File.WriteAllText(ModuleInstaller.CatalogPath, JsonUtility.ToJson(new ModuleCatalog { modules = specs }));
        foreach (var spec in specs) Client.Sources[spec.source] = new PackageInfo { name = spec.id, version = spec.version };
    }
    private static void Drain()
    {
        for (int i = 0; i < 60 && ModuleInstaller.Busy; i++) EditorApplication.TickForTest();
        True(!ModuleInstaller.Busy);
    }
    private static void InstallerReload() => InProject(() =>
    {
        WriteCatalog(Spec("base"), Spec("feature", "base")); ModuleInstaller.Install(new[] { "feature" });
        EditorApplication.TickForTest(); EditorApplication.TickForTest(); // Add succeeded; its request handle is about to be lost.
        var job = typeof(ModuleInstaller).GetField("job", Static);
        var reloaded = typeof(JsonUtility).GetMethod(nameof(JsonUtility.FromJson)).MakeGenericMethod(job.FieldType).Invoke(null, new object[] { SessionState.GetString("Novorizon.GameStarter.InstallJob", "") });
        job.SetValue(null, reloaded);
        typeof(ModuleInstaller).GetField("adding", Static).SetValue(null, null);
        typeof(ModuleInstaller).GetField("listing", Static).SetValue(null, null);
        Drain(); Equal(2, Client.Installed.Count); Equal(2, Client.Added.Count);
    });
    private static void InstallerFailure() => InProject(() =>
    {
        WriteCatalog(Spec("base"), Spec("feature", "base")); ModuleInstaller.Install(new[] { "feature" });
        while (!Client.Installed.ContainsKey("base")) EditorApplication.TickForTest();
        Client.FailNext = true; Drain(); Equal(1, Client.Installed.Count); True(ModuleInstaller.Message.Contains("中止"));
        ModuleInstaller.Install(new[] { "feature" }); Drain(); Equal(2, Client.Installed.Count);
    });
    private static void InstallerVersion() => InProject(() =>
    {
        WriteCatalog(Spec("feature")); Client.Sources["git:feature"].version = "0.9.0";
        ModuleInstaller.Install(new[] { "feature" }); Drain(); True(ModuleInstaller.Message.Contains("mismatch")); Equal(1, Client.Added.Count);
    });
    private static void InstallerEmbed() => InProject(() =>
    {
        var spec = Spec("official"); spec.embed = true; WriteCatalog(spec);
        string source = Path.GetFullPath("Cache/official"); Directory.CreateDirectory(Path.Combine(source, "Data~"));
        File.WriteAllText(Path.Combine(source, "Data~/native.txt"), "native"); File.WriteAllText(Path.Combine(source, "native.dll.meta"), "guid: original");
        Client.Sources[spec.source].resolvedPath = source;
        Client.HoldEmbed = true;
        ModuleInstaller.Install(new[] { "official" });
        for (int i = 0; i < 30 && Client.PendingEmbed == null; i++) EditorApplication.TickForTest();
        True(Client.PendingEmbed != null && ModuleInstaller.Busy);
        for (int i = 0; i < 3; i++) EditorApplication.TickForTest();
        True(ModuleInstaller.Busy);
        Client.PendingEmbed.IsCompleted = true; Drain();
        Equal("native", File.ReadAllText("Packages/official/Data~/native.txt")); Equal("guid: original", File.ReadAllText("Packages/official/native.dll.meta"));
    });
    private static void InstallerWaits() => InProject(() =>
    {
        WriteCatalog(Spec("a")); ModuleInstaller.Install(new[] { "a" }); EditorApplication.isCompiling = true;
        EditorApplication.TickForTest(); Equal(0, Client.Added.Count); EditorApplication.isCompiling = false;
        EditorApplication.isPlayingOrWillChangePlaymode = true; EditorApplication.TickForTest(); Equal(0, Client.Added.Count);
        EditorApplication.isPlayingOrWillChangePlaymode = false; Drain(); Equal(1, Client.Added.Count);
    });
    private static void InstallerSetup() => InProject(() =>
    {
        var module = Spec("feature", "base"); module.setupType = typeof(SetupProbe).FullName; module.setupMethod = nameof(SetupProbe.Configure);
        WriteCatalog(Spec("base"), module); SetupProbe.Called = false;
        ModuleInstaller.Install(new[] { "feature" }); Drain(); True(SetupProbe.Called);
    });
    public static class SetupProbe
    {
        internal static bool Called;
        public static void Configure() { True(Client.Installed.ContainsKey("base") && Client.Installed.ContainsKey("feature")); Called = true; }
    }
}

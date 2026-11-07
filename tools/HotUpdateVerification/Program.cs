using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;
using Game.Framework.HotUpdate.Editor;
using UnityEngine;

internal static partial class Program
{
    private static int passed;
    private static async Task Main()
    {
        await Check("resources finish preparation before any compiled game entry is created", ContentBeforeEntry);
        await Check("resource preparation failure prevents game entry construction", ContentFailure);
        await Check("release signatures bind the resource version and package identity", ContentPolicy);
        await Check("failed update resources fall back with packaged DLLs and reject the complete pair", ReleasePairFallback);
        await Check("resource code shares completed downloads and restarts from manifest-only cache", () => RunReleasePair(ResourceCodeCase.Success));
        await Check("packaged-only startup preserves healthy online releases and acceptance receipts", () => RunReleasePair(ResourceCodeCase.PackagedOnly));
        await Check("packaged-only startup ignores remote configuration but validates local settings", PackagedOnlySettings);
        await Check("missing unified resources revert both code and content without raw downloads", () => RunReleasePair(ResourceCodeCase.MissingResources));
        await Check("resource DLL hash mismatch rejects the release before loading", () => RunReleasePair(ResourceCodeCase.CorruptDll));
        await Check("resource AOT metadata stays pinned and fails as a complete pair", () => RunReleasePair(ResourceCodeCase.CorruptMetadata));
        await Check("resource code cancellation never falls back or downloads raw DLLs", () => RunReleasePair(ResourceCodeCase.Cancelled));
        await Check("resource-code baseline refuses silent standalone-download fallback", () => RunReleasePair(ResourceCodeCase.MissingProvider));
        await Check("Runtime and Editor retain separate Unity assembly identities", AssemblyBoundaries);
        await Check("loading starts only after an explicit call", ExplicitStartup);
        await Check("manager initialization only configures; explicit start runs once with the original context", ManagerInitializationOnlyConfigures);
        await Check("manager start requires completed configuration", ManagerStartRequiresInitialization);
        await Check("configured manager can stop without starting or preparing content", ManagerCanStopBeforeStarting);
        await Check("cancellation between configuration and start prevents content preparation", ManagerCancellationBeforeStart);
        await Check("repeated initialization shares one task and one hot entry", InitializeOnce);
        await Check("startup context reaches the optional host without serialization", StartupContext);
        await Check("missing startup acknowledgement fails and closes the entry", MissingAcknowledgement);
        await Check("initialization failure is propagated after entry cleanup", StartupFailure);
        await Check("cancelled call cannot construct a hot entry", AlreadyCancelled);
        await Check("exit cancels pending startup and rejects late acknowledgement", ExitDuringStartup);
        await Check("startup deadline stops an entry that ignores cancellation", StartupTimeout);
        await Check("shutdown failure cannot retain the singleton or block a new instance", ShutdownFailure);
        await Check("entry type must implement the shared AOT contract", InvalidEntry);
        await Check("default settings use the Unity application ID", DefaultSettings);
        await Check("setup uses code settings and does not generate startup JSON", ConfigureWithoutJson);
        Console.WriteLine($"PASS {passed} explicit hot-update startup and build-configuration scenarios (Unity API models; not native Unity).");
    }

    private static async Task Check(string name, Func<Task> test)
    {
        HotUpdateManager.Release();
        Verification.FixtureHotGame.Reset();
        try
        {
            await test();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        finally
        {
            HotUpdateManager.Release();
        }
    }

    private static UpdateSettings Settings() => new UpdateSettings
    {
        entryAssembly = typeof(Verification.FixtureHotGame).Assembly.GetName().Name,
        entryType = typeof(Verification.FixtureHotGame).FullName,
        startupTimeoutSeconds = 5
    };
    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Assertion failed.");
        }
    }

    private static async Task Throws<T>(Task task)
        where T : Exception
    {
        try
        {
            await task;
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private static async Task ContentBeforeEntry()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = Settings();
        settings.content = new ReleaseContent { packageName = "DefaultPackage", version = "tables-2" };
        bool prepared = false;
        settings.prepareContentAsync = async (content, token) =>
        {
            True(content.PackageVersion == "tables-2" && content.PackageName == "DefaultPackage");
            True(Verification.FixtureHotGame.Created == 0);
            prepared = true;
            await pending.Task;
        };
        var manager = HotUpdateManager.Instance;
        var starting = manager.InitializeAsync(settings);
        while (!prepared)
        {
            await Task.Yield();
        }

        True(!manager.IsReady && Verification.FixtureHotGame.Created == 0);
        pending.SetResult(true);
        await starting;
        True(manager.IsReady);
    }

    private static async Task ContentFailure()
    {
        var settings = Settings();
        settings.prepareContentAsync = (content, token) => Task.FromException(new IOException("resource download failed"));
        await Throws<IOException>(HotUpdateManager.Instance.InitializeAsync(settings));
        True(Verification.FixtureHotGame.Created == 0);
    }

    private static Task ContentPolicy()
    {
        var file = new ReleaseFile { assemblyName = "Game", fileName = "Game.dll.bytes", size = 1, sha256 = new string('a', 64) };
        var baseline = new ReleaseManifest
        {
            appId = "app", platform = "windows-x64", channel = "development", baselineId = "baseline", version = 1,
            assemblies = new[] { file }, aotMetadata = Array.Empty<ReleaseFile>(),
            content = new ReleaseContent { packageName = "DefaultPackage", version = "tables-1" }
        };
        var update = UnityEngine.JsonUtility.FromJson<ReleaseManifest>(UnityEngine.JsonUtility.ToJson(baseline));
        update.version = 2;
        update.content.version = "tables-2";
        ReleasePolicy.MatchBaseline(update, baseline);
        update.content.packageName = "OtherPackage";
        bool rejected = false;
        try
        {
            ReleasePolicy.MatchBaseline(update, baseline);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        True(rejected);
        return Task.CompletedTask;
    }

    private static Task AssemblyBoundaries()
    {
        True(typeof(GameBuild).Assembly.GetName().Name == "Game.Framework.HotUpdate.Editor");
        True(typeof(HotUpdateManager).Assembly.GetName().Name == "Game.Framework.HotUpdate");
        True(typeof(Game.Framework.Singleton<>).Assembly.GetName().Name == "Game.Framework.Core");
        True(typeof(GameBuild).Assembly != typeof(HotUpdateManager).Assembly);
        return Task.CompletedTask;
    }

    private static Task ExplicitStartup()
    {
        True(!HotUpdateManager.IsCreated);
        True(!typeof(MonoBehaviour).IsAssignableFrom(typeof(HotUpdateManager)));
        True(!typeof(HotUpdateManager).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Any(method => method.GetCustomAttributes<RuntimeInitializeOnLoadMethodAttribute>().Any()));
        var manager = HotUpdateManager.Instance;
        True(manager.Status == "Not started" && !manager.IsReady);
        True(Verification.FixtureHotGame.Created == 0);
        return Task.CompletedTask;
    }

    private static async Task StartupContext()
    {
        object context = new object();
        Verification.FixtureHotGame.Initialize = (host, token) =>
        {
            True(host is IGameContextHost contextHost && ReferenceEquals(contextHost.StartupContext, context));
            host.ReportReady("context");
            return Task.CompletedTask;
        };
        await HotUpdateManager.Instance.InitializeAsync(Settings(), context);
        True(HotUpdateManager.Instance.IsReady);
    }

    private static async Task InitializeOnce()
    {
        var manager = HotUpdateManager.Instance;
        Task first = manager.InitializeAsync(Settings());
        Task second = manager.InitializeAsync(Settings());
        True(ReferenceEquals(first, second));
        await first;
        True(manager.IsReady && !manager.Failed);
        True(Verification.FixtureHotGame.Created == 1 && Verification.FixtureHotGame.Started == 1);
        manager.Dispose();
        HotUpdateManager.Release();
        True(Verification.FixtureHotGame.Stopped == 1 && !HotUpdateManager.IsCreated);
    }

    private static async Task MissingAcknowledgement()
    {
        Verification.FixtureHotGame.Initialize = (_, __) => Task.CompletedTask;
        var manager = HotUpdateManager.Instance;
        await Throws<InvalidOperationException>(manager.InitializeAsync(Settings()));
        True(manager.Failed && !manager.IsReady && Verification.FixtureHotGame.Stopped == 1);
    }

    private static async Task StartupFailure()
    {
        Verification.FixtureHotGame.Initialize = (_, __) => Task.FromException(new InvalidOperationException("fixture failure"));
        var manager = HotUpdateManager.Instance;
        await Throws<InvalidOperationException>(manager.InitializeAsync(Settings()));
        True(manager.Failed && Verification.FixtureHotGame.Stopped == 1);
    }

    private static Task AlreadyCancelled()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        try
        {
            HotUpdateManager.Instance.InitializeAsync(Settings(), cancel.Token);
        }
        catch (OperationCanceledException)
        {
            True(Verification.FixtureHotGame.Created == 0);
            return Task.CompletedTask;
        }

        throw new InvalidOperationException("Expected cancellation.");
    }

    private static async Task ExitDuringStartup()
    {
        var entered = new TaskCompletionSource<IGameHost>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Verification.FixtureHotGame.Initialize = (host, _) =>
        {
            entered.TrySetResult(host);
            return pending.Task;
        };
        var manager = HotUpdateManager.Instance;
        Task start = manager.InitializeAsync(Settings());
        IGameHost lateHost = await entered.Task;
        manager.Dispose();
        await Throws<OperationCanceledException>(start);
        True(!manager.IsReady && !manager.Failed && Verification.FixtureHotGame.Stopped == 1);
        try
        {
            lateHost.ReportReady("late");
            throw new Exception("An ended startup accepted acknowledgement.");
        }
        catch (InvalidOperationException)
        {
        }

        pending.TrySetException(new InvalidOperationException("late initialization error"));
    }

    private static async Task StartupTimeout()
    {
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Verification.FixtureHotGame.Initialize = (_, __) => pending.Task;
        var manager = HotUpdateManager.Instance;
        await Throws<TimeoutException>(manager.InitializeAsync(Settings()));
        True(manager.Failed && Verification.FixtureHotGame.Stopped == 1);
        pending.TrySetResult(true);
    }

    private static async Task ShutdownFailure()
    {
        var manager = HotUpdateManager.Instance;
        await manager.InitializeAsync(Settings());
        Verification.FixtureHotGame.Stop = () => throw new InvalidOperationException("cleanup failure");
        manager.Dispose();
        True(!HotUpdateManager.IsCreated && !manager.IsReady);
        True(!ReferenceEquals(manager, HotUpdateManager.Instance));
        True(Verification.FixtureHotGame.Stopped == 1);
    }

    private static async Task InvalidEntry()
    {
        var settings = Settings();
        settings.entryType = typeof(Verification.NotHotGame).FullName;
        await Throws<InvalidOperationException>(HotUpdateManager.Instance.InitializeAsync(settings));
        True(Verification.FixtureHotGame.Created == 0);
    }

    private static Task DefaultSettings()
    {
        var settings = new UpdateSettings();
        settings.Validate();
        True(settings.appId == Application.identifier);
        True(settings.entryType == "Game.HotUpdate.HotGame");
        True(typeof(UpdateSettings).GetField("autoStart") == null);
        True(typeof(UpdateSettings).GetMethod("Load") == null);
        return Task.CompletedTask;
    }

    private static Task ConfigureWithoutJson()
    {
        string original = Environment.CurrentDirectory;
        string package = Path.Combine(original, "Packages/com.novorizon.framework.hybridclr");
        string root = Path.Combine(Path.GetTempPath(), "hot-update-code-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Environment.CurrentDirectory = root;
            Application.dataPath = Path.Combine(root, "Assets");
            UnityEditor.PackageManager.PackageInfo.TestPackagePath = package;
            GameBuild.SettingsFactory = () => new UpdateSettings
            {
                hotUpdateRoot = "Assets/CustomHot",
                entryType = "Custom.HotGame"
            };
            // 仅安装了包或存在同名热更目录，不应接管普通项目的构建。
            HybridCLR.Settings.HybridCLRSettings.instance.outputLinkFile = "Other/link.xml";
            Directory.CreateDirectory("Assets/CustomHot");
            File.WriteAllText("Assets/CustomHot/Unrelated.asmdef", "{}");
            True(!GameBuild.IsConfigured);
            new ReleaseBuildGuard().OnPreprocessBuild(new UnityEditor.Build.Reporting.BuildReport());
            new ReleaseBuildVerifier().OnPostprocessBuild(new UnityEditor.Build.Reporting.BuildReport());
            File.Delete("Assets/CustomHot/Unrelated.asmdef");
            GameBuild.Configure();
            True(File.Exists("Assets/CustomHot/HotGame.cs"));
            True(File.ReadAllText("Assets/CustomHot/HotGame.cs").Contains("namespace Custom"));
            True(!Directory.Exists("Assets/Resources"));
            True(GameBuild.IsConfigured);
            const string retained = "// project-owned custom implementation";
            File.WriteAllText("Assets/CustomHot/HotGame.cs", retained);
            GameBuild.Configure();
            True(File.ReadAllText("Assets/CustomHot/HotGame.cs") == retained);
            True(!Directory.GetFiles(root, "NovorizonHotUpdateSettings.json", SearchOption.AllDirectories).Any());
        }
        finally
        {
            GameBuild.SettingsFactory = () => new UpdateSettings();
            Environment.CurrentDirectory = original;
            Directory.Delete(root, true);
        }

        return Task.CompletedTask;
    }
}

namespace Verification
{
    public sealed class NotHotGame
    {
    }

    public sealed class FixtureHotGame : IGameModule
    {
        internal static int Created, Started, Stopped;
        internal static Func<IGameHost, CancellationToken, Task> Initialize;
        internal static Action Stop;
        public FixtureHotGame()
        {
            Created++;
        }

        internal static void Reset()
        {
            Created = Started = Stopped = 0;
            Stop = null;
            Initialize = (host, token) =>
            {
                token.ThrowIfCancellationRequested();
                host.ReportReady("fixture");
                return Task.CompletedTask;
            };
        }

        public Task InitializeAsync(IGameHost host, CancellationToken token)
        {
            Started++;
            return Initialize(host, token);
        }

        public void Shutdown()
        {
            Stopped++;
            Stop?.Invoke();
        }
    }
}

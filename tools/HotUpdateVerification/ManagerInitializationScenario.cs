using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;

internal static partial class Program
{
    private static async Task ManagerInitializationOnlyConfigures()
    {
        int preparations = 0;
        object context = new object();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = Settings();
        settings.prepareContentAsync = async (content, token) =>
        {
            preparations++;
            entered.TrySetResult(true);
            await pending.Task;
            token.ThrowIfCancellationRequested();
        };
        Verification.FixtureHotGame.Initialize = (host, token) =>
        {
            True(host is IGameContextHost contextHost && ReferenceEquals(contextHost.StartupContext, context));
            token.ThrowIfCancellationRequested();
            host.ReportReady("two-phase");
            return Task.CompletedTask;
        };

        var manager = HotUpdateManager.Instance;
        manager.Initialize(settings, context);
        manager.Initialize(settings, context);
        True(manager.Status == "Initialized" && !manager.IsReady);
        True(preparations == 0 && Verification.FixtureHotGame.Created == 0);
        True(HotUpdateManager.GetPlatformId() == "windows-x64");
        RequireSynchronousException<InvalidOperationException>(() => manager.InitializeAsync(null, new object()));

        Task first = manager.StartAsync();
        Task second = manager.StartAsync();
        True(ReferenceEquals(first, second));
        await entered.Task;
        True(preparations == 1 && Verification.FixtureHotGame.Created == 0);
        RequireSynchronousException<InvalidOperationException>(() => manager.Initialize(Settings(), context));
        RequireSynchronousException<InvalidOperationException>(() => manager.Initialize(settings, new object()));
        True(ReferenceEquals(first, manager.InitializeAsync(Settings())));

        pending.SetResult(true);
        await first;
        True(manager.IsReady && !manager.Failed);
        True(preparations == 1 && Verification.FixtureHotGame.Created == 1 && Verification.FixtureHotGame.Started == 1);
        manager.Dispose();
        True(Verification.FixtureHotGame.Stopped == 1);
    }

    private static Task ManagerStartRequiresInitialization()
    {
        var manager = HotUpdateManager.Instance;
        RequireSynchronousException<InvalidOperationException>(() => manager.StartAsync());
        True(manager.Status == "Not started" && !manager.IsReady && Verification.FixtureHotGame.Created == 0);
        return Task.CompletedTask;
    }

    private static Task ManagerCanStopBeforeStarting()
    {
        int preparations = 0;
        var settings = Settings();
        settings.prepareContentAsync = (content, token) =>
        {
            preparations++;
            return Task.CompletedTask;
        };
        var manager = HotUpdateManager.Instance;
        manager.Initialize(settings);
        manager.Dispose();
        manager.Dispose();
        RequireSynchronousException<ObjectDisposedException>(() => manager.StartAsync());
        RequireSynchronousException<ObjectDisposedException>(() => manager.Initialize(settings));
        True(preparations == 0 && Verification.FixtureHotGame.Created == 0 && Verification.FixtureHotGame.Stopped == 0);
        True(!manager.IsReady && !HotUpdateManager.IsCreated);
        return Task.CompletedTask;
    }

    private static Task ManagerCancellationBeforeStart()
    {
        int preparations = 0;
        var settings = Settings();
        settings.prepareContentAsync = (content, token) =>
        {
            preparations++;
            return Task.CompletedTask;
        };
        using var cancellation = new CancellationTokenSource();
        var manager = HotUpdateManager.Instance;
        manager.Initialize(settings, null, cancellation.Token);
        cancellation.Cancel();
        RequireSynchronousException<OperationCanceledException>(() => manager.StartAsync());
        RequireSynchronousException<OperationCanceledException>(() => manager.InitializeAsync());
        True(preparations == 0 && Verification.FixtureHotGame.Created == 0 && !manager.IsReady);
        return Task.CompletedTask;
    }

    private static void RequireSynchronousException<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}

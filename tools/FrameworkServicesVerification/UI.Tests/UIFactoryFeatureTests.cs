using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
public sealed class UIFactoryFeatureTests : IDisposable
{
    readonly List<UIInstanceFactory> factories = new();
    public UIFactoryFeatureTests() => Object.ResetTestState();
    public void Dispose()
    {
        foreach (var factory in factories)
        {
            factory.Dispose();
        }

        Object.ResetTestState();
    }

    UIInstanceFactory Create()
    {
        var layer = new GameObject("Feature layer").transform;
        var prefab = new GameObject("Feature prefab");
        prefab.SetActive(false);
        prefab.AddComponent<ProbePopup>();
        var factory = new UIInstanceFactory(new DelegateUIAssetLoader(_ => Task.FromResult<GameObject?>(prefab), _ =>
        {
        }), Enum.GetValues<UILayer>().ToDictionary(key => key, _ => layer), 1);
        factories.Add(factory);
        return factory;
    }

    static Task<UIHandle> Open(UIInstanceFactory factory, object? args = null, CancellationToken ct = default) => factory.OpenAsync(UIKind.Popup, UILayer.Popup, "feature", args, true, true, null, ct);
    [Fact]
    public async Task SyncCloseCannotBypassAnAsyncGuardAndConfirmedCloseRecordsItsReason()
    {
        var factory = Create();
        var handle = await Open(factory);
        int checks = 0;
        handle.View!.CloseGuard = (_, _) =>
        {
            checks++;
            return Task.FromResult(true);
        };
        Assert.False(factory.TryClose(handle, false, true, UICloseReason.Back));
        Assert.True(handle.IsValid);
        Assert.Equal(0, checks);
        Assert.True(await factory.CloseAsync(handle, false, true, UICloseReason.Back));
        Assert.False(handle.IsValid);
        Assert.Equal(1, checks);
        Assert.Equal(UICloseReason.Back, handle.View.LastCloseReason);
    }

    [Fact]
    public async Task ClosingAViewStopsAnUncooperativeGuardWithoutCancellingTheCaller()
    {
        var factory = Create();
        var handle = await Open(factory);
        var late = new TaskCompletionSource<bool>();
        CancellationToken guardToken = default;
        handle.View!.CloseGuard = (_, ct) =>
        {
            guardToken = ct;
            return late.Task;
        };
        var closing = factory.CanCloseAsync(handle, UICloseReason.Back);
        Assert.False(closing.IsCompleted);
        factory.Dispose();
        Assert.False(await closing.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(guardToken.IsCancellationRequested);
        late.SetResult(true);
        Assert.False(handle.IsValid);
    }

    [Fact]
    public async Task CallerCancellationStopsWaitingForAnUncooperativeGuardAndKeepsTheViewOpen()
    {
        var factory = Create();
        var handle = await Open(factory);
        var late = new TaskCompletionSource<bool>();
        handle.View!.CloseGuard = (_, _) => late.Task;
        using var cancellation = new CancellationTokenSource();
        var closing = factory.CloseAsync(handle, false, true, UICloseReason.Back, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(handle.IsValid);
        Assert.Equal(1, factory.ActiveCount);
        late.SetResult(true);
    }

    [Fact]
    public async Task CancellationInsideOnOpenCleansUpItsNewLeaseEvenWhenCloseWouldBeVetoed()
    {
        var factory = Create();
        using var cancellation = new CancellationTokenSource();
        ProbePopup? view = null;
        Action<ProbePopup> onOpen = popup =>
        {
            view = popup;
            popup.CloseGuard = (_, _) => Task.FromResult(false);
            cancellation.Cancel();
        };
        var handle = await Open(factory, onOpen, cancellation.Token);
        Assert.False(handle.IsValid);
        Assert.Equal(0, factory.ActiveCount);
        Assert.NotNull(view);
        Assert.False(view.IsOpen);
        Assert.Equal(UICloseReason.Cancelled, view.LastCloseReason);
    }

    sealed class ProbePopup : UIPopup
    {
        protected override void OnOpen(object? args) => (args as Action<ProbePopup>)?.Invoke(this);
    }
}
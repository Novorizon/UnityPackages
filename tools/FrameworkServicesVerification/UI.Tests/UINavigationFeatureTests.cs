using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
public sealed class UINavigationFeatureTests : IDisposable
{
    readonly List<UIInstanceFactory> factories = new();
    public UINavigationFeatureTests()
    {
        Object.ResetTestState();
    }

    public void Dispose()
    {
        foreach (UIInstanceFactory factory in factories)
        {
            factory.Dispose();
        }

        Object.ResetTestState();
    }

    (UIInstanceFactory Factory, Loader Loader) Create<T>()
        where T : UIView
    {
        var layer = new GameObject("Navigation feature layer");
        var prefab = new GameObject("Navigation feature prefab");
        prefab.SetActive(false);
        prefab.AddComponent<T>();
        var loader = new Loader();
        loader.Assets.Add("default", prefab);
        var factory = new UIInstanceFactory(loader, Enum.GetValues<UILayer>().ToDictionary(value => value, _ => layer.transform), 1);
        factories.Add(factory);
        return (factory, loader);
    }

    static GameObject Prefab<T>()
        where T : UIView
    {
        var value = new GameObject(typeof(T).Name);
        value.SetActive(false);
        value.AddComponent<T>();
        return value;
    }

    static (UIManager Root, Loader Loader) CreateRoot()
    {
        var root = UIManager.Instance;
        var loader = new Loader();
        loader.Assets.Add("page", Prefab<ProbePage>());
        loader.Assets.Add("popup", Prefab<ProbePopup>());
        loader.Assets.Add("panel", Prefab<ProbePanel>());
        loader.Assets.Add("overlay", Prefab<ProbeOverlay>());
        UIManager.SetAssetLoader(loader);
        return (root, loader);
    }

    [Fact]
    public async Task CallerCanCancelAPageQueuedBehindAnUnfinishedNavigation()
    {
        var(factory, loader) = Create<ProbePage>();
        loader.Delayed.Add("slow");
        var pages = new PageNavigator(factory);
        Task<UIHandle<ProbePage>> first = pages.PushAsync(new UIKey<ProbePage>("slow"));
        using var cancellation = new CancellationTokenSource();
        Task<UIHandle<ProbePage>> queued = pages.PushAsync(new UIKey<ProbePage>("queued"), cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.DoesNotContain(loader.Requests, request => request.Path == "queued");
        loader.Complete(0);
        Assert.True((await first).IsValid);
        Assert.Equal(1, pages.Count);
    }

    [Fact]
    public async Task CancellingPopupLoadReturnsPromptlyAndReleasesTheLateLease()
    {
        var(factory, loader) = Create<ProbePopup>();
        loader.Delayed.Add("popup");
        var popups = new PopupManager(factory);
        using var cancellation = new CancellationTokenSource();
        Task<UIHandle<ProbePopup>> pending = popups.OpenAsync(new UIKey<ProbePopup>("popup"), cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, popups.Count);
        Assert.Equal(0, factory.ActiveCount);
        loader.Complete(0);
        await DrainAsync();
        Assert.Equal(1, loader.Releases);
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task CancellingASingletonRefreshDoesNotCloseTheExistingPopupLease()
    {
        var(factory, _) = Create<ProbePopup>();
        var popups = new PopupManager(factory);
        var options = new PopupOptions
        {
            SingletonByPath = true
        };
        var existing = await popups.OpenAsync(new UIKey<ProbePopup>("popup"), options: options);
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => popups.OpenAsync(new UIKey<ProbePopup>("popup"), (Action)cancellation.Cancel, options, cancellation.Token));
        Assert.True(existing.IsValid);
        Assert.Equal(1, popups.Count);
        Assert.Equal(1, factory.ActiveCount);
    }

    [Fact]
    public async Task CancellingGroupedPanelLoadPreservesTheVisiblePreviousPanel()
    {
        var(factory, loader) = Create<ProbePanel>();
        var panels = new PanelManager(factory);
        var previous = await panels.PushInGroupAsync("inventory", new UIKey<ProbePanel>("first"));
        loader.Delayed.Add("second");
        using var cancellation = new CancellationTokenSource();
        Task<UIHandle<ProbePanel>> pending = panels.PushInGroupAsync("inventory", new UIKey<ProbePanel>("second"), cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(previous.IsValid);
        Assert.True(previous.View!.IsVisible);
        Assert.True(panels.IsShown(new UIKey<ProbePanel>("first")));
        loader.Complete(1);
        await DrainAsync();
        Assert.Equal(1, loader.Releases);
        Assert.True(panels.PopGroup("inventory"));
    }

    [Fact]
    public async Task HiddenPagesKeepTheirOpenScopeAndReceiveShowAgainWhenPoppedBack()
    {
        var(factory, _) = Create<ProbePage>();
        var pages = new PageNavigator(factory);
        var first = await pages.PushAsync(new UIKey<ProbePage>("first"));
        var firstView = Assert.IsType<ProbePage>(first.View);
        CancellationToken openToken = firstView.OpenToken;
        Assert.Equal(1, firstView.ShowCalls);
        Assert.True(firstView.IsVisible);
        var second = await pages.PushAsync(new UIKey<ProbePage>("second"));
        Assert.True(first.IsValid);
        Assert.True(firstView.IsOpen);
        Assert.False(firstView.IsVisible);
        Assert.False(openToken.IsCancellationRequested);
        Assert.Equal(1, firstView.HideCalls);
        Assert.True(await pages.PopAsync());
        Assert.False(second.IsValid);
        Assert.True(firstView.IsVisible);
        Assert.Equal(2, firstView.ShowCalls);
        Assert.True(pages.Pop());
        Assert.True(openToken.IsCancellationRequested);
        Assert.Equal(UICloseReason.Back, firstView.LastCloseReason);
    }

    [Fact]
    public void ATopLayerVetoConsumesBackWithoutClosingAnyLowerLayer() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var(root, _) = CreateRoot();
            await UIManager.Pages.PushAsync(new UIKey<ProbePage>("page"));
            await UIManager.Pages.PushAsync(new UIKey<ProbePage>("page"));
            var panel = await UIManager.Panels.ShowAsync(new UIKey<ProbePanel>("panel"));
            var panelView = Assert.IsType<ProbePanel>(panel.View);
            panelView.AllowClose = false;
            var popup = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            var popupView = Assert.IsType<ProbePopup>(popup.View);
            popupView.AllowClose = false;
            Assert.True(UIManager.HandleBack());
            Assert.True(popup.IsValid);
            Assert.True(panel.IsValid);
            Assert.Equal(2, UIManager.Pages.Count);
            popupView.AllowClose = true;
            Assert.True(UIManager.HandleBack());
            Assert.False(popup.IsValid);
            Assert.True(UIManager.HandleBack());
            Assert.True(panel.IsValid);
            Assert.Equal(2, UIManager.Pages.Count);
            panelView.AllowClose = true;
            Assert.True(UIManager.HandleBack());
            Assert.False(panel.IsValid);
            Assert.True(UIManager.HandleBack());
            Assert.Equal(1, UIManager.Pages.Count);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedAsynchronousBackSharesOneConfirmationAndNeverClosesTheNextPage(bool approved) =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var(root, _) = CreateRoot();
            await UIManager.Pages.PushAsync(new UIKey<ProbePage>("page"));
            await UIManager.Pages.PushAsync(new UIKey<ProbePage>("page"));
            var popup = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            var view = Assert.IsType<ProbePopup>(popup.View);
            var confirmation = new TaskCompletionSource<bool>();
            int calls = 0;
            view.CloseGuard = (_, _) =>
            {
                calls++;
                return confirmation.Task;
            };
            Assert.True(UIManager.HandleBack());
            Assert.Equal(1, calls);
            Assert.True(popup.IsValid);
            Task<bool> first = UIManager.HandleBackAsync();
            Task<bool> second = UIManager.HandleBackAsync();
            Assert.Equal(1, calls);
            Assert.False(first.IsCompleted);
            Assert.False(second.IsCompleted);
            confirmation.SetResult(approved);
            Assert.True(await first);
            Assert.True(await second);
            Assert.Equal(2, UIManager.Pages.Count);
            Assert.Equal(!approved, popup.IsValid);
            if (approved)
            {
                Assert.Equal(UICloseReason.Back, view.LastCloseReason);
            }
        });

    [Fact]
    public void AGuardReenteringBackSynchronouslyReusesThePublishedAttempt() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var(root, _) = CreateRoot();
            var handle = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            int calls = 0;
            Task<bool>? reentrant = null;
            handle.View!.CloseGuard = (_, _) =>
            {
                calls++;
                reentrant = UIManager.HandleBackAsync();
                Assert.True(UIManager.HandleBack());
                return Task.FromResult(true);
            };
            Task<bool> closing = UIManager.HandleBackAsync();
            Assert.NotNull(reentrant);
            Assert.True(await closing);
            Assert.True(await reentrant);
            Assert.Equal(1, calls);
            Assert.False(handle.IsValid);
        });

    [Fact]
    public void CancellingTheFirstBackCallerDoesNotCancelAnotherCallersConfirmation() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var(root, _) = CreateRoot();
            var handle = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            var approval = new TaskCompletionSource<bool>();
            int calls = 0;
            CancellationToken guardToken = default;
            handle.View!.CloseGuard = async (_, token) =>
            {
                calls++;
                guardToken = token;
                return await approval.Task.WaitAsync(token);
            };
            using var cancellation = new CancellationTokenSource();
            Task<bool> first = UIManager.HandleBackAsync(cancellation.Token);
            Task<bool> second = UIManager.HandleBackAsync();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(1, calls);
            Assert.False(guardToken.IsCancellationRequested);
            Assert.False(second.IsCompleted);
            approval.SetResult(true);
            Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(handle.IsValid);
        });

    [Fact]
    public void CancellingEveryBackCallerClosesItsConfirmationAndAllowsANewAttempt() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var(root, loader) = CreateRoot();
            loader.Assets.Add("confirmation", Prefab<ConfirmationPopup>());
            var handle = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            CancellationToken guardToken = default;
            int calls = 0;
            Task<UIDialogResult<bool>>? confirmation = null;
            handle.View!.CloseGuard = async (_, token) =>
            {
                guardToken = token;
                calls++;
                confirmation = UIManager.Dialogs.ShowAsync<ConfirmationPopup, bool>(new UIKey<ConfirmationPopup>("confirmation"), cancellationToken: token);
                UIDialogResult<bool> result = await confirmation;
                return result.HasValue && result.Value;
            };
            using var firstCancellation = new CancellationTokenSource();
            using var secondCancellation = new CancellationTokenSource();
            Task<bool> first = UIManager.HandleBackAsync(firstCancellation.Token);
            Task<bool> second = UIManager.HandleBackAsync(secondCancellation.Token);
            Assert.Equal(1, calls);
            Assert.Equal(2, UIManager.Popups.Count);
            firstCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(guardToken.IsCancellationRequested);
            Assert.Equal(2, UIManager.Popups.Count);
            secondCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(3)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => confirmation!.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(guardToken.IsCancellationRequested);
            Assert.Equal(1, UIManager.Popups.Count);
            Assert.True(handle.IsValid);
            handle.View.CloseGuard = null;
            Assert.True(await UIManager.HandleBackAsync().WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(handle.IsValid);
            Assert.Equal(0, UIManager.Popups.Count);
        });

    [Fact]
    public void BackImmediatelyAfterLastCallerCancellationTargetsTheOriginalWindowAfterConfirmationCleanup() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var(root, loader) = CreateRoot();
            loader.Assets.Add("confirmation", Prefab<ConfirmationPopup>());
            var handle = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            Task<UIDialogResult<bool>>? confirmation = null;
            handle.View!.CloseGuard = async (_, token) =>
            {
                confirmation = UIManager.Dialogs.ShowAsync<ConfirmationPopup, bool>(new UIKey<ConfirmationPopup>("confirmation"), cancellationToken: token);
                UIDialogResult<bool> result = await confirmation;
                return result.HasValue && result.Value;
            };
            using var cancellation = new CancellationTokenSource();
            Task<bool> original = UIManager.HandleBackAsync(cancellation.Token);
            Assert.Equal(2, UIManager.Popups.Count);
            cancellation.Cancel();
            handle.View.CloseGuard = null;
            Task<bool> retry = UIManager.HandleBackAsync(); // Intentionally before awaiting cancellation or UI cleanup.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => original.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(await retry.WaitAsync(TimeSpan.FromSeconds(3)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => confirmation!.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(handle.IsValid);
            Assert.Equal(0, UIManager.Popups.Count);
        });

    [Fact]
    public void AGuardIgnoringCancellationCannotKeepBackPermanentlyBlocked() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var(root, _) = CreateRoot();
            var handle = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            var ignoredCancellation = new TaskCompletionSource<bool>();
            handle.View!.CloseGuard = (_, _) => ignoredCancellation.Task;
            using var cancellation = new CancellationTokenSource();
            Task<bool> cancelled = UIManager.HandleBackAsync(cancellation.Token);
            cancellation.Cancel();
            handle.View.CloseGuard = null;
            Task<bool> retry = UIManager.HandleBackAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.True(await retry.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(handle.IsValid);
            Assert.False(ignoredCancellation.Task.IsCompleted);
            ignoredCancellation.SetResult(true);
        });

    [Fact]
    public void ResetCancelsAPendingBackConfirmationWithoutAffectingNewManagers() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var(root, loader) = CreateRoot();
            loader.Assets.Add("confirmation", Prefab<ConfirmationPopup>());
            var handle = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            handle.View!.CloseGuard = async (_, token) =>
            {
                UIDialogResult<bool> result = await UIManager.Dialogs.ShowAsync<ConfirmationPopup, bool>(new UIKey<ConfirmationPopup>("confirmation"), cancellationToken: token);
                return result.HasValue && result.Value;
            };
            Task<bool> pending = UIManager.HandleBackAsync();
            Assert.Equal(2, UIManager.Popups.Count);
            UIManager.ResetAll();
            Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.Equal(0, UIManager.Popups.Count);
            Assert.False(handle.IsValid);
            var fresh = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            Assert.True(await UIManager.HandleBackAsync());
            Assert.False(fresh.IsValid);
        });

    [Fact]
    public async Task ANewTopPopupDuringConfirmationPreventsClosingTheOldTop()
    {
        var(factory, _) = Create<ProbePopup>();
        var popups = new PopupManager(factory);
        var first = await popups.OpenAsync(new UIKey<ProbePopup>("first"));
        var confirmation = new TaskCompletionSource<bool>();
        first.View!.CloseGuard = (_, _) => confirmation.Task;
        Task<bool> close = popups.CloseTopAsync();
        var second = await popups.OpenAsync(new UIKey<ProbePopup>("second"));
        confirmation.SetResult(true);
        Assert.False(await close);
        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Equal(2, popups.Count);
    }

    [Fact]
    public async Task CancellingCloseConfirmationLeavesTheCurrentLeaseOpen()
    {
        var(factory, _) = Create<ProbePopup>();
        var popups = new PopupManager(factory);
        var handle = await popups.OpenAsync(new UIKey<ProbePopup>("popup"));
        handle.View!.CloseGuard = async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return true;
        };
        using var cancellation = new CancellationTokenSource();
        Task<bool> closing = popups.CloseAsync(handle, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(handle.IsValid);
        Assert.Equal(1, popups.Count);
    }

    [Fact]
    public void PendingOverlayBlocksBackAndItsCancellationDoesNotLeaveAView() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var(root, loader) = CreateRoot();
            var popup = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
            loader.Delayed.Add("overlay");
            using var cancellation = new CancellationTokenSource();
            Task<UIOverlayHandle<ProbeOverlay>> showing = UIManager.Overlays.ShowBlockingAsync(new UIKey<ProbeOverlay>("overlay"), cancellationToken: cancellation.Token);
            Assert.True(UIManager.Overlays.IsBlocking);
            Assert.True(UIManager.HandleBack());
            Assert.True(await UIManager.HandleBackAsync());
            Assert.True(popup.IsValid);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => showing.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(UIManager.Overlays.IsBlocking);
            loader.Complete(1);
            await DrainAsync();
            Assert.Equal(1, loader.Releases);
            Assert.True(popup.IsValid);
            Assert.True(UIManager.HandleBack());
            Assert.False(popup.IsValid);
        });

    [Fact]
    public async Task CancellingOneSharedOverlayWaiterPreservesTheOtherOwnersLoadAndForcedRelease()
    {
        var(factory, loader) = Create<ProbeOverlay>();
        loader.Delayed.Add("overlay");
        var overlays = new OverlayManager(factory);
        using var cancellation = new CancellationTokenSource();
        Task<UIOverlayHandle<ProbeOverlay>> first = overlays.ShowBlockingAsync(new UIKey<ProbeOverlay>("overlay"), cancellationToken: cancellation.Token);
        Task<UIOverlayHandle<ProbeOverlay>> second = overlays.ShowBlockingAsync(new UIKey<ProbeOverlay>("overlay"));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(overlays.IsBlocking);
        Assert.Single(loader.Requests);
        loader.Complete(0);
        IDisposable owner = await second;
        Assert.True(overlays.IsBlocking);
        Assert.Equal(1, factory.ActiveCount);
        owner.Dispose();
        Assert.False(overlays.IsBlocking);
        Assert.Equal(0, factory.ActiveCount);
        factory.Dispose();
        Assert.Equal(1, loader.Releases);
    }

    [Fact]
    public async Task ACancelledOverlayGenerationCannotCloseItsReplacement()
    {
        var(factory, loader) = Create<ProbeOverlay>();
        loader.Delayed.Add("overlay");
        var overlays = new OverlayManager(factory);
        using var cancellation = new CancellationTokenSource();
        Task<UIOverlayHandle<ProbeOverlay>> obsolete = overlays.ShowBlockingAsync(new UIKey<ProbeOverlay>("overlay"), cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => obsolete.WaitAsync(TimeSpan.FromSeconds(3)));
        Task<UIOverlayHandle<ProbeOverlay>> replacement = overlays.ShowBlockingAsync(new UIKey<ProbeOverlay>("overlay"));
        loader.Complete(0);
        await DrainAsync();
        Assert.Equal(1, loader.Releases);
        Assert.True(overlays.IsBlocking);
        loader.Complete(1);
        IDisposable owner = await replacement;
        Assert.Equal(1, factory.ActiveCount);
        owner.Dispose();
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task ResetBypassesTheCloseGuardAndRecordsTheResetReason()
    {
        var(root, _) = CreateRoot();
        var popup = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("popup"));
        var view = Assert.IsType<ProbePopup>(popup.View);
        view.AllowClose = false;
        view.CloseGuard = (_, _) => Task.FromResult(false);
        UIManager.ResetAll();
        Assert.False(popup.IsValid);
        Assert.Equal(UICloseReason.Reset, view.LastCloseReason);
    }

    static async Task DrainAsync()
    {
        for (int i = 0; i < 32; i++)
        {
            await Task.Yield();
        }
    }

    sealed class Loader : IUIAssetLoader
    {
        public readonly Dictionary<string, GameObject> Assets = new();
        public readonly HashSet<string> Delayed = new();
        public readonly List<Request> Requests = new();
        public int Releases;
        public Task<GameObject?> LoadPrefabAsync(string path)
        {
            var source = new TaskCompletionSource<GameObject?>();
            var request = new Request(path, source);
            Requests.Add(request);
            if (!Delayed.Contains(path))
            {
                source.SetResult(Assets.TryGetValue(path, out GameObject? asset) ? asset : Assets["default"]);
            }

            return source.Task;
        }

        public void Complete(int index)
        {
            Request request = Requests[index];
            request.Source.SetResult(Assets.TryGetValue(request.Path, out GameObject? value) ? value : Assets["default"]);
        }

        public void ReleasePrefab(GameObject value)
        {
            Releases++;
        }

        public sealed record Request(string Path, TaskCompletionSource<GameObject?> Source);
    }

    sealed class ProbePage : UIPage
    {
        public int ShowCalls, HideCalls;
        protected override void OnShow()
        {
            ShowCalls++;
        }

        protected override void OnHide()
        {
            HideCalls++;
        }
    }

    sealed class ProbePopup : UIPopup
    {
        public bool AllowClose = true;
        protected override void OnOpen(object? args) => (args as Action)?.Invoke();
        protected override bool CanClose(UICloseReason reason) => AllowClose;
    }

    sealed class ProbePanel : UIPanel
    {
        public bool AllowClose = true;
        protected override bool CanClose(UICloseReason reason) => AllowClose;
    }

    sealed class ConfirmationPopup : UIResultPopup<bool>
    {
    }

    sealed class ProbeOverlay : UIOverlay
    {
        protected override bool CanClose(UICloseReason reason) => false;
    }
}
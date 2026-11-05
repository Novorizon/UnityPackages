using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Game.Framework.UI.Tests;
// Exercises the production UI sources against a finite, deterministic Unity API
// contract. These are lifecycle regressions, not Unity/renderer/IL2CPP tests.
public sealed class UILifecycleTests : IDisposable
{
    readonly List<UIInstanceFactory> factories = new();
    public UILifecycleTests()
    {
        Object.ResetTestState();
        ProbeToast.Opened = 0;
        ProbeToast.CloseCount = 0;
        ImmediateToast.Opened = 0;
        ImmediateToast.CloseCount = 0;
    }

    public void Dispose()
    {
        foreach (var factory in factories)
        {
            factory.Dispose();
        }

        Object.ResetTestState();
    }

    (UIInstanceFactory factory, RecordingLoader loader, Transform root) CreateFactory(int maxCachedInstances = 32, int maxCachedPerPath = 2, Type? viewType = null)
    {
        var root = new GameObject("TestLayer").transform;
        var prefab = new GameObject("Prefab");
        prefab.SetActive(false);
        prefab.AddComponent(viewType ?? typeof(ProbePopup));
        var loader = new RecordingLoader(prefab);
        var roots = Enum.GetValues<UILayer>().ToDictionary(layer => layer, _ => root);
        var factory = new UIInstanceFactory(loader, roots, 1, maxCachedInstances, maxCachedPerPath);
        factories.Add(factory);
        return (factory, loader, root);
    }

    static Task<UIHandle> Open(UIInstanceFactory factory, string path = "popup", bool multiple = true, bool cache = true) => factory.OpenAsync(UIKind.Popup, UILayer.Popup, path, null, multiple, cache, null);
    [Fact]
    public async Task SamePathPopupsRetainOnlyBoundedRegisteredInstances()
    {
        var(factory, loader, root) = CreateFactory();
        var handles = new List<UIHandle>();
        for (int i = 0; i < 12; i++)
        {
            handles.Add(await Open(factory));
        }

        foreach (var handle in handles)
        {
            factory.Close(handle, false, true);
        }

        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(2, factory.CachedCount);
        Assert.Equal(factory.CachedCount, root.childCount);
        Assert.All(root.gameObject.GetComponentsInChildren<ProbePopup>(true), view => Assert.False(view.IsOpen));
        Assert.True(loader.ReleaseCount > 0);
        factory.ClearCache();
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnDisableClearingOrDisposingFactoryCannotOrphanClosingView(bool dispose)
    {
        var(factory, loader, root) = CreateFactory(viewType: typeof(ReentrantDisable));
        ReentrantDisable.Factory = factory;
        ReentrantDisable.DisposeFactory = dispose;
        ReentrantDisable.DisableCalls = 0;
        var handle = await Open(factory);
        factory.Close(handle, false, true);
        Assert.Equal(1, ReentrantDisable.DisableCalls);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(factory.CachedCount, root.childCount);
        factory.ClearCache();
        Assert.Equal(0, factory.CachedCount);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
        Assert.Equal(dispose, factory.IsDisposed);
    }

    [Fact]
    public async Task TotalCacheLimitBoundsDifferentPrefabPaths()
    {
        var(factory, loader, root) = CreateFactory(maxCachedInstances: 3, maxCachedPerPath: 2);
        for (int i = 0; i < 10; i++)
        {
            var handle = await Open(factory, "path-" + i);
            factory.Close(handle, false, true);
        }

        Assert.Equal(3, factory.CachedCount);
        Assert.Equal(3, root.childCount);
        factory.Dispose();
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task DefaultTotalCacheLimitIsThirtyTwo()
    {
        var(factory, _, root) = CreateFactory();
        for (int i = 0; i < 40; i++)
        {
            var handle = await Open(factory, "path-" + i);
            factory.Close(handle, false, true);
        }

        Assert.Equal(32, factory.CachedCount);
        Assert.Equal(32, root.childCount);
    }

    [Fact]
    public async Task RepeatedCloseIsIdempotentAndOldHandleCannotCloseReopenedView()
    {
        var(factory, _, _) = CreateFactory();
        var original = await Open(factory);
        var view = Assert.IsType<ProbePopup>(original.View);
        factory.Close(original, false, true);
        factory.Close(original, false, true);
        Assert.Equal(1, view.Closes);
        Assert.False(original.IsValid);
        var reopened = await Open(factory);
        Assert.Same(view, reopened.View);
        Assert.NotEqual(original.Id, reopened.Id);
        factory.Close(original, true, false);
        Assert.True(reopened.IsValid);
        Assert.Equal(1, view.Closes);
        Assert.Equal(1, factory.ActiveCount);
    }

    [Fact]
    public async Task CacheDisabledOnOpenIsRespectedAtClose()
    {
        var(factory, loader, root) = CreateFactory();
        var manager = new PopupManager(factory);
        var handle = await manager.OpenAsync("popup", options: new PopupOptions { CacheOnClose = false });
        manager.Close(handle);
        Assert.False(handle.IsValid);
        Assert.Equal(0, factory.CachedCount);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task CachedModalPopupReceivesNewBlockerAndClickClosesCorrectLease()
    {
        var(factory, _, root) = CreateFactory();
        var manager = new PopupManager(factory);
        var original = await manager.OpenAsync("popup");
        var oldBlocker = Assert.Single(root.gameObject.GetComponentsInChildren<UIBlocker>(true));
        manager.Close(original);
        Assert.True(oldBlocker.DestroyedForTest);
        Assert.Empty(root.gameObject.GetComponentsInChildren<UIBlocker>(true));
        var reopened = await manager.OpenAsync("popup");
        Assert.Same(original.View, reopened.View);
        var blocker = Assert.Single(root.gameObject.GetComponentsInChildren<UIBlocker>(true));
        Assert.NotSame(oldBlocker, blocker);
        blocker.OnPointerClick(new UnityEngine.EventSystems.PointerEventData());
        Assert.False(reopened.IsValid);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public async Task DisposeClosesActiveViewsClearsCachedViewsAndReleasesEveryLeaseOnce()
    {
        var(factory, loader, root) = CreateFactory();
        var active = await Open(factory, "active");
        var cached = await Open(factory, "cached");
        factory.Close(cached, false, true);
        Assert.Equal(0, loader.ReleaseCount);
        factory.Dispose();
        factory.Dispose();
        Assert.True(factory.IsDisposed);
        Assert.False(active.IsValid);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, factory.CachedCount);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
        Assert.Equal(1, ((ProbePopup)active.View!).Closes);
        Assert.Equal(1, ((ProbePopup)active.View!).DestroyedCallbacks);
        Assert.Equal(1, ((ProbePopup)cached.View!).DestroyedCallbacks);
    }

    [Fact]
    public async Task DisposeDuringLoadDoesNotCreateViewAndReleasesLatePrefab()
    {
        var(factory, loader, root) = CreateFactory();
        loader.Pending = new TaskCompletionSource<GameObject?>();
        var opening = Open(factory);
        Assert.False(opening.IsCompleted);
        factory.Dispose();
        loader.Pending.SetResult(loader.Prefab);
        var result = await opening.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(result.IsValid);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task ConcurrentSingletonOpensConvergeOnOneLiveView()
    {
        var(factory, loader, root) = CreateFactory();
        loader.Pending = new TaskCompletionSource<GameObject?>();
        var first = Open(factory, multiple: false);
        var second = Open(factory, multiple: false);
        loader.Pending.SetResult(loader.Prefab);
        var handles = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(handles[0].IsValid);
        Assert.Same(handles[0].View, handles[1].View);
        Assert.Equal(handles[0].Id, handles[1].Id);
        Assert.Equal(1, factory.ActiveCount);
        Assert.Equal(1, root.childCount);
        factory.Dispose();
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task NativeViewDestructionReleasesItsLeaseAndRemovesActiveRegistration()
    {
        var(factory, loader, root) = CreateFactory();
        var handle = await Open(factory);
        Object.Destroy(handle.View!.gameObject);
        Assert.False(handle.IsValid);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
        factory.Dispose();
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public void SynchronousToastCompletionDuringOnOpenDoesNotLoseQueueProgress()
    {
        var(factory, _, _) = CreateFactory(viewType: typeof(ImmediateToast));
        using var manager = new ToastManager(factory);
        manager.Enqueue("toast", "first");
        manager.Enqueue("toast", "second");
        Assert.Equal(2, ImmediateToast.Opened);
        Assert.Equal(2, ImmediateToast.CloseCount);
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public void ClearToastQueueClosesCurrentAndAllowsLaterEnqueue()
    {
        var(factory, _, _) = CreateFactory(viewType: typeof(ProbeToast));
        using var manager = new ToastManager(factory);
        manager.Enqueue("toast", "first");
        manager.Enqueue("toast", "queued");
        Assert.Equal(1, ProbeToast.Opened);
        manager.Clear();
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(1, ProbeToast.CloseCount);
        manager.Enqueue("toast", "later");
        Assert.Equal(2, ProbeToast.Opened);
    }

    [Fact]
    public async Task ClearDuringToastLoadDiscardsObsoleteOpening()
    {
        var(factory, loader, _) = CreateFactory(viewType: typeof(ProbeToast));
        using var manager = new ToastManager(factory);
        loader.Pending = new TaskCompletionSource<GameObject?>();
        manager.Enqueue("toast");
        manager.Clear();
        loader.Pending.SetResult(loader.Prefab);
        await DrainAsync();
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, ProbeToast.Opened);
        manager.Enqueue("toast");
        Assert.Equal(1, factory.ActiveCount);
    }

    [Fact]
    public async Task ExternallyDestroyedToastAdvancesTheQueue()
    {
        var(factory, _, root) = CreateFactory(viewType: typeof(ProbeToast));
        using var manager = new ToastManager(factory);
        manager.Enqueue("toast", "first");
        manager.Enqueue("toast", "second");
        var current = Assert.Single(root.gameObject.GetComponentsInChildren<ProbeToast>(true));
        Object.Destroy(current.gameObject);
        await DrainAsync();
        Assert.Equal(2, ProbeToast.Opened);
        Assert.Equal(1, factory.ActiveCount);
    }

    [Fact]
    public async Task DisposeToastManagerCancelsCurrentAndQueuedWork()
    {
        var(factory, loader, root) = CreateFactory(viewType: typeof(ProbeToast));
        var manager = new ToastManager(factory);
        loader.Pending = new TaskCompletionSource<GameObject?>();
        manager.Enqueue("toast");
        manager.Enqueue("toast");
        manager.Dispose();
        loader.Pending.SetResult(loader.Prefab);
        await DrainAsync();
        Assert.Equal(0, factory.ActiveCount);
        Assert.DoesNotContain(root.gameObject.GetComponentsInChildren<ProbeToast>(true), toast => toast.IsOpen);
    }

    [Fact]
    public async Task CancellingPendingOpenFinishesBeforeLoaderAndReleasesLateLease()
    {
        var(factory, loader, root) = CreateFactory();
        loader.Pending = new TaskCompletionSource<GameObject?>();
        using var cancellation = new CancellationTokenSource();
        var pending = factory.OpenAsync(UIKind.Popup, UILayer.Popup, "popup", null, true, true, null, cancellation.Token);
        cancellation.Cancel();
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(2))).IsValid);
        Assert.Equal(0, root.childCount);
        Assert.Equal(0, loader.ReleaseCount);
        loader.Pending.SetResult(loader.Prefab);
        await DrainAsync();
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task AlreadyCancelledOpenDoesNotLoadPrefab()
    {
        var(factory, loader, _) = CreateFactory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handle = await factory.OpenAsync(UIKind.Popup, UILayer.Popup, "popup", null, true, true, null, cancellation.Token);
        Assert.False(handle.IsValid);
        Assert.Equal(0, loader.LoadCount);
    }

    [Fact]
    public async Task ResetAllInvalidatesOldManagersClosesViewsClearsBusAndKeepsRootReusable()
    {
        var root = UIManager.Instance;
        var prefab = new GameObject("RootPopup");
        prefab.AddComponent<ProbePopup>();
        var loader = new RecordingLoader(prefab);
        root.SetAssetLoader(loader);
        int received = 0;
        root.Bus.Subscribe<int>(_ => received++);
        var oldManager = root.Popups;
        var handle = await oldManager.OpenAsync("popup");
        root.ResetAll();
        Assert.False(handle.IsValid);
        Assert.Equal(0, oldManager.Count);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
        Assert.False((await oldManager.OpenAsync("obsolete")).IsValid);
        root.Bus.Publish(42);
        Assert.Equal(0, received);
        var fresh = await root.Popups.OpenAsync("popup");
        Assert.True(fresh.IsValid);
        Assert.NotEqual(handle.Id, fresh.Id);
    }

    [Fact]
    public async Task ChangingRootCacheSettingsClearsOldCacheAndAppliesNewCapacity()
    {
        var root = UIManager.Instance;
        var prefab = new GameObject("RootPopup");
        prefab.AddComponent<ProbePopup>();
        var loader = new RecordingLoader(prefab);
        root.SetAssetLoader(loader);
        var first = await root.Popups.OpenAsync("first");
        root.Popups.Close(first);
        Assert.Equal(0, loader.ReleaseCount);
        var settings = ScriptableObject.CreateInstance<UISettings>();
        settings.maxCachedInstances = 0;
        settings.maxCachedPerPath = 0;
        root.SetSettings(settings);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
        var second = await root.Popups.OpenAsync("second");
        root.Popups.Close(second);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task OnCreateExceptionFromInactivePrefabReleasesLease()
    {
        var(factory, loader, root) = CreateFactory(viewType: typeof(ThrowOnCreate));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Open(factory));
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task OnOpenExceptionReleasesLeaseAndDoesNotLeaveActiveRegistration()
    {
        var(factory, loader, root) = CreateFactory(viewType: typeof(ThrowOnOpen));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Open(factory));
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task ViewDisposingFactoryFromOnOpenReturnsInvalidHandle()
    {
        var(factory, loader, root) = CreateFactory(viewType: typeof(DisposeOnOpen));
        DisposeOnOpen.Factory = factory;
        var handle = await Open(factory);
        Assert.False(handle.IsValid);
        Assert.True(factory.IsDisposed);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task DisposingFromBlockerFactoryDestroysLateBlockerAndDoesNotOpenView()
    {
        var(factory, loader, root) = CreateFactory();
        var handle = await factory.OpenAsync(UIKind.Popup, UILayer.Popup, "popup", null, true, true, parent =>
        {
            factory.Dispose();
            return UIBlocker.Create(parent);
        });
        Assert.False(handle.IsValid);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task DisposingFromOnEnablePreventsCreateAndOpenCallbacks()
    {
        var(factory, loader, root) = CreateFactory(viewType: typeof(DisposeOnEnable));
        DisposeOnEnable.Factory = factory;
        DisposeOnEnable.Created = 0;
        DisposeOnEnable.Opened = 0;
        var handle = await Open(factory);
        Assert.False(handle.IsValid);
        Assert.Equal(0, DisposeOnEnable.Created);
        Assert.Equal(0, DisposeOnEnable.Opened);
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task RebuildingRootDuringCleanupIsRejectedWithoutLeakingReplacementFactory()
    {
        var root = UIManager.Instance;
        var prefab = new GameObject("ReentrantPopup");
        prefab.AddComponent<ReentrantClose>();
        var originalLoader = new RecordingLoader(prefab);
        root.SetAssetLoader(originalLoader);
        var handle = await root.Popups.OpenAsync("popup", (Action)(() => root.ResetAll()));
        var replacement = new RecordingLoader(prefab);
        root.SetAssetLoader(replacement);
        Assert.False(handle.IsValid);
        Assert.Equal(originalLoader.LoadCount, originalLoader.ReleaseCount);
        Assert.Contains(Debug.Exceptions, exception => exception is InvalidOperationException);
        Assert.True((await root.Popups.OpenAsync("popup")).IsValid);
        Object.Destroy(root.gameObject);
        Assert.Equal(replacement.LoadCount, replacement.ReleaseCount);
    }

    [Fact]
    public async Task DeferredNativeDestructionKeepsPrefabLeaseUntilDestructionCallback()
    {
        var(factory, loader, root) = CreateFactory();
        var handle = await Open(factory);
        Object.DeferGameObjectDestructionForTest = true;
        factory.Close(handle, true, false);
        Assert.False(handle.IsValid);
        Assert.Equal(0, loader.ReleaseCount);
        Assert.Equal(1, root.childCount);
        Object.FlushDestroyQueueForTest();
        Assert.Equal(0, root.childCount);
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task ToastNativeDestructionDoesNotReuseDyingViewForNextQueueItem()
    {
        var(factory, _, root) = CreateFactory(viewType: typeof(ProbeToast));
        using var manager = new ToastManager(factory);
        manager.Enqueue("toast", "first");
        manager.Enqueue("toast", "second");
        var dying = Assert.Single(root.gameObject.GetComponentsInChildren<ProbeToast>(true));
        Object.DeferGameObjectDestructionForTest = true;
        Object.Destroy(dying.gameObject);
        Object.FlushDestroyQueueForTest();
        await DrainAsync();
        var next = Assert.Single(root.gameObject.GetComponentsInChildren<ProbeToast>(true));
        Assert.NotSame(dying, next);
        Assert.True(next.IsOpen);
        Assert.Equal(1, factory.ActiveCount);
    }

    [Fact]
    public async Task ResourcesLoaderUsesRealAsyncRequestAndHandlesAlreadyCompletedRequests()
    {
        var prefab = new GameObject("ResourcePrefab");
        Resources.RegisterAssetForTest("ui/test", prefab);
        var loader = new ResourcesAssetLoader();
        var pending = loader.LoadPrefabAsync("ui/test");
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, Resources.SynchronousLoadCount);
        Assert.Equal(1, Resources.AsyncLoadCount);
        Resources.CompletePendingForTest();
        Assert.Same(prefab, await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Resources.CompleteSynchronouslyForTest = true;
        Assert.Same(prefab, await loader.LoadPrefabAsync("ui/test").WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task SwitchingRootLoaderDisposesOldViewsAndManagers()
    {
        var root = UIManager.Instance;
        var prefab = new GameObject("RootPopup");
        prefab.AddComponent<ProbePopup>();
        var firstLoader = new RecordingLoader(prefab);
        root.SetAssetLoader(firstLoader);
        var handle = await root.Popups.OpenAsync("popup");
        var oldPopups = root.Popups;
        var secondLoader = new RecordingLoader(prefab);
        root.SetAssetLoader(secondLoader);
        Assert.False(handle.IsValid);
        Assert.Equal(firstLoader.LoadCount, firstLoader.ReleaseCount);
        Assert.Equal(0, oldPopups.Count);
        var next = await root.Popups.OpenAsync("popup");
        Assert.True(next.IsValid);
        Assert.NotEqual(handle.Id, next.Id);
        Object.Destroy(root.gameObject);
        Assert.Equal(secondLoader.LoadCount, secondLoader.ReleaseCount);
    }

    [Fact]
    public async Task SamePathPagePushesHaveIndependentHistoryEntries()
    {
        var(factory, _, _) = CreateFactory(viewType: typeof(ProbePage));
        var pages = new PageNavigator(factory);
        var first = await pages.PushAsync("page");
        var second = await pages.PushAsync("page");
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotSame(first.View, second.View);
        Assert.Equal(2, pages.Count);
        Assert.False(first.View!.gameObject.activeSelf);
        Assert.True(pages.Pop());
        Assert.Equal(first.Id, pages.Peek()!.Value.Id);
        Assert.True(first.View.gameObject.activeSelf);
    }

    [Fact]
    public async Task ExternallyDestroyedTopPageImmediatelyRestoresPreviousPage()
    {
        var(factory, loader, root) = CreateFactory(viewType: typeof(ProbePage));
        var pages = new PageNavigator(factory);
        var first = await pages.PushAsync("first");
        var second = await pages.PushAsync("second");
        Assert.False(first.View!.gameObject.activeSelf);
        Object.Destroy(second.View!.gameObject);
        // Check before Count/Peek to prove the destruction event restores visibility.
        Assert.True(first.View.gameObject.activeSelf);
        Assert.Equal(1, pages.Count);
        Assert.Equal(first.Id, pages.Peek()!.Value.Id);
        Assert.Equal(1, root.childCount);
        Assert.False(second.IsValid);
        factory.Dispose();
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task ExternallyDestroyedPanelGroupTopRestoresPreviousAndRemovesStaleEntry()
    {
        var(factory, loader, _) = CreateFactory(viewType: typeof(ProbePanel));
        var panels = new PanelManager(factory);
        var first = await panels.PushInGroupAsync("group", "first");
        var second = await panels.PushInGroupAsync("group", "second");
        Object.Destroy(second.View!.gameObject);
        Assert.True(first.View!.gameObject.activeSelf);
        Assert.False(panels.IsShown("second"));
        Assert.True(panels.IsShown("first"));
        Assert.False(panels.Hide(second));
        Assert.True(panels.HideAnyBackClosablePanel());
        Assert.False(first.IsValid);
        factory.Dispose();
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task DestroyedOverlayAllowsDifferentPathAndOldTokenCannotCloseReplacement()
    {
        var(factory, loader, root) = CreateFactory(viewType: typeof(ProbeOverlay));
        var overlays = new OverlayManager(factory);
        using var oldToken = await overlays.ShowBlockingAsync("first");
        var oldView = Assert.Single(root.gameObject.GetComponentsInChildren<ProbeOverlay>(true));
        Object.Destroy(oldView.gameObject);
        using var newToken = await overlays.ShowBlockingAsync("replacement");
        var replacement = Assert.Single(root.gameObject.GetComponentsInChildren<ProbeOverlay>(true));
        Assert.NotSame(oldView, replacement);
        oldToken.Dispose();
        Assert.True(replacement.IsOpen);
        Assert.Equal(1, factory.ActiveCount);
        newToken.Dispose();
        Assert.False(replacement.IsOpen);
        Assert.Equal(0, factory.ActiveCount);
        factory.Dispose();
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Theory]
    [InlineData("Push")]
    [InlineData("Replace")]
    [InlineData("Reset")]
    public async Task FailedPageNavigationPreservesHistoryAndVisiblePage(string operation)
    {
        var(factory, loader, _) = CreateFactory(viewType: typeof(ProbePage));
        var pages = new PageNavigator(factory);
        var original = await pages.PushAsync("page");
        loader.LoadOverride = _ => Task.FromResult<GameObject?>(null);
        var failed = operation switch
        {
            "Push" => await pages.PushAsync("missing"),
            "Replace" => await pages.ReplaceAsync("missing"),
            _ => await pages.ResetToAsync("missing")};
        Assert.False(failed.IsValid);
        Assert.Equal(1, pages.Count);
        Assert.Equal(original.Id, pages.Peek()!.Value.Id);
        Assert.True(original.IsValid);
        Assert.True(original.View!.gameObject.activeSelf);
    }

    [Fact]
    public async Task ConcurrentPagePushesPreserveCallOrder()
    {
        var(factory, loader, _) = CreateFactory(viewType: typeof(ProbePage));
        var pages = new PageNavigator(factory);
        var gate = new TaskCompletionSource<GameObject?>();
        loader.LoadOverride = path => path == "first" ? gate.Task : Task.FromResult<GameObject?>(loader.Prefab);
        var first = pages.PushAsync("first");
        var second = pages.PushAsync("second");
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        gate.SetResult(loader.Prefab);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, pages.Count);
        Assert.Equal("second", pages.Peek()!.Value.PrefabPath);
        Assert.Equal(new[] { "first", "second" }, loader.Paths);
    }

    [Fact]
    public async Task DisposedFactoryDoesNotAllowPendingPageToEnterHistory()
    {
        var(factory, loader, _) = CreateFactory(viewType: typeof(ProbePage));
        var pages = new PageNavigator(factory);
        loader.Pending = new TaskCompletionSource<GameObject?>();
        var pending = pages.PushAsync("page");
        factory.Dispose();
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(2))).IsValid);
        Assert.Equal(0, pages.Count);
        loader.Pending.SetResult(loader.Prefab);
        await DrainAsync();
        Assert.Equal(loader.LoadCount, loader.ReleaseCount);
    }

    [Fact]
    public async Task MultiplePanelsCanBeHiddenByHandleAndPathClosesMostRecent()
    {
        var(factory, _, _) = CreateFactory(viewType: typeof(ProbePanel));
        var panels = new PanelManager(factory);
        var options = new PanelOptions
        {
            AllowMultiple = true
        };
        var first = await panels.ShowAsync("panel", options: options);
        var second = await panels.ShowAsync("panel", options: options);
        Assert.True(panels.IsShown("panel"));
        Assert.True(panels.Hide("panel"));
        Assert.False(second.IsValid);
        Assert.True(first.IsValid);
        Assert.True(panels.IsShown("panel"));
        Assert.True(panels.Hide(first));
        Assert.False(panels.IsShown("panel"));
        Assert.False(panels.Hide(first));
    }

    [Fact]
    public async Task HidingPanelGroupTopRestoresPreviousAndBackIncludesGroups()
    {
        var(factory, _, _) = CreateFactory(viewType: typeof(ProbePanel));
        var panels = new PanelManager(factory);
        var first = await panels.PushInGroupAsync("group", "first");
        var second = await panels.PushInGroupAsync("group", "second");
        Assert.False(first.View!.gameObject.activeSelf);
        Assert.True(panels.Hide(second));
        Assert.True(first.View.gameObject.activeSelf);
        Assert.True(panels.HideAnyBackClosablePanel());
        Assert.False(first.IsValid);
    }

    [Fact]
    public async Task ConcurrentPanelGroupPushesPreserveCallOrder()
    {
        var(factory, loader, _) = CreateFactory(viewType: typeof(ProbePanel));
        var panels = new PanelManager(factory);
        var gate = new TaskCompletionSource<GameObject?>();
        loader.LoadOverride = path => path == "first" ? gate.Task : Task.FromResult<GameObject?>(loader.Prefab);
        var firstTask = panels.PushInGroupAsync("group", "first");
        var secondTask = panels.PushInGroupAsync("group", "second");
        Assert.False(secondTask.IsCompleted);
        gate.SetResult(loader.Prefab);
        var handles = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(handles[0].View!.gameObject.activeSelf);
        Assert.True(handles[1].View!.gameObject.activeSelf);
        Assert.True(panels.PopGroup("group"));
        Assert.True(handles[0].View!.gameObject.activeSelf);
    }

    [Fact]
    public async Task ConcurrentBlockingOverlayTokensKeepSharedOverlayOpenUntilLastRelease()
    {
        var(factory, loader, root) = CreateFactory(viewType: typeof(ProbeOverlay));
        var overlays = new OverlayManager(factory);
        loader.Pending = new TaskCompletionSource<GameObject?>();
        var first = overlays.ShowBlockingAsync("overlay");
        var second = overlays.ShowBlockingAsync("overlay");
        Assert.Equal(1, loader.LoadCount);
        loader.Pending.SetResult(loader.Prefab);
        var tokens = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, factory.ActiveCount);
        Assert.Single(root.gameObject.GetComponentsInChildren<ProbeOverlay>(true));
        tokens[0].Dispose();
        tokens[0].Dispose();
        Assert.Equal(1, factory.ActiveCount);
        tokens[1].Dispose();
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task BlockingOverlayRejectsDifferentPathAndFailedLoadCanBeRetried()
    {
        var(factory, loader, _) = CreateFactory(viewType: typeof(ProbeOverlay));
        var overlays = new OverlayManager(factory);
        using (await overlays.ShowBlockingAsync("first"))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => overlays.ShowBlockingAsync("second"));
        }

        factory.ClearCache();
        loader.LoadOverride = _ => Task.FromResult<GameObject?>(null);
        using (await overlays.ShowBlockingAsync("missing"))
        {
            Assert.Equal(0, factory.ActiveCount);
        }

        loader.LoadOverride = null;
        using (await overlays.ShowBlockingAsync("recovered"))
        {
            Assert.Equal(1, factory.ActiveCount);
        }

        Assert.Equal(0, factory.ActiveCount);
    }

    static async Task DrainAsync()
    {
        // No real engine frame or timer is simulated here; allow already-scheduled
        // continuation chains to run without polling any production UI handle.
        for (int i = 0; i < 6; i++)
        {
            await Task.Yield();
        }
    }

    sealed class RecordingLoader : IUIAssetLoader
    {
        public readonly GameObject Prefab;
        public int LoadCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public TaskCompletionSource<GameObject?>? Pending;
        public Func<string, Task<GameObject?>>? LoadOverride;
        public List<string> Paths { get; } = new();

        public RecordingLoader(GameObject prefab)
        {
            Prefab = prefab;
        }

        public Task<GameObject?> LoadPrefabAsync(string path)
        {
            LoadCount++;
            Paths.Add(path);
            return LoadOverride?.Invoke(path) ?? Pending?.Task ?? Task.FromResult<GameObject?>(Prefab);
        }

        public void ReleasePrefab(GameObject prefab)
        {
            Assert.Same(Prefab, prefab);
            ReleaseCount++;
            Assert.True(ReleaseCount <= LoadCount);
        }
    }

    sealed class ThrowOnCreate : UIPopup
    {
        protected override void OnCreate() => throw new InvalidOperationException("OnCreate failure");
    }

    sealed class ThrowOnOpen : UIPopup
    {
        protected override void OnOpen(object? args) => throw new InvalidOperationException("OnOpen failure");
    }

    sealed class DisposeOnOpen : UIPopup
    {
        public static UIInstanceFactory? Factory;
        protected override void OnOpen(object? args) => Factory!.Dispose();
    }

    sealed class DisposeOnEnable : UIPopup
    {
        public static UIInstanceFactory? Factory;
        public static int Created, Opened;
        void OnEnable() => Factory?.Dispose();
        protected override void OnCreate()
        {
            Created++;
        }

        protected override void OnOpen(object? args)
        {
            Opened++;
        }
    }

    sealed class ReentrantDisable : UIPopup
    {
        public static UIInstanceFactory? Factory;
        public static bool DisposeFactory;
        public static int DisableCalls;
        void OnDisable()
        {
            if (InstanceId == 0 || IsOpen)
            {
                return;
            }

            DisableCalls++;
            if (DisposeFactory)
            {
                Factory!.Dispose();
            }
            else
            {
                Factory!.ClearCache();
            }
        }
    }

    sealed class ReentrantClose : UIPopup
    {
        Action? closing;
        protected override void OnOpen(object? args)
        {
            closing = args as Action;
        }

        protected override void OnClose()
        {
            var callback = closing;
            closing = null;
            callback?.Invoke();
        }
    }

    sealed class ProbePage : UIPage
    {
    }

    sealed class ProbePanel : UIPanel
    {
    }

    sealed class ProbeOverlay : UIOverlay
    {
    }

    sealed class ProbePopup : UIPopup
    {
        public override bool CloseOnBlockerClick => true;

        public int Opens, Closes, DestroyedCallbacks;
        protected override void OnOpen(object? args)
        {
            Opens++;
        }

        protected override void OnClose()
        {
            Closes++;
        }

        protected override void OnDestroyed()
        {
            DestroyedCallbacks++;
        }
    }

    sealed class ProbeToast : UIToast
    {
        public static int Opened, CloseCount;
        public override float Duration => 0;

        protected override void OnOpen(object? args)
        {
            Opened++;
        }

        protected override void OnClose()
        {
            CloseCount++;
        }
    }

    sealed class ImmediateToast : UIToast
    {
        public static int Opened, CloseCount;
        protected override void OnOpen(object? args)
        {
            Opened++;
            Complete();
        }

        protected override void OnClose()
        {
            CloseCount++;
        }
    }
}
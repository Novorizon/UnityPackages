using System.Reflection;
using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
public sealed class UIShutdownAndRepeatTests : IDisposable
{
    private readonly List<UIInstanceFactory> factories = new();
    public UIShutdownAndRepeatTests() => Object.ResetTestState();
    public void Dispose()
    {
        foreach (var factory in factories)
        {
            factory.Dispose();
        }

        Object.ResetTestState();
    }

    private (UIInstanceFactory factory, Loader loader) Create()
    {
        var prefab = new GameObject("repeat prefab");
        prefab.SetActive(false);
        prefab.AddComponent<Probe>();
        var loader = new Loader(prefab);
        var roots = Enum.GetValues<UILayer>().ToDictionary(layer => layer, layer => new GameObject(layer.ToString()).transform);
        var factory = new UIInstanceFactory(loader, roots, 1);
        factories.Add(factory);
        return (factory, loader);
    }

    private static Task<UIHandle> Open(UIInstanceFactory factory, UIKind kind = UIKind.Popup, UILayer layer = UILayer.Popup, UIDuplicatePolicy policy = UIDuplicatePolicy.Refresh, CancellationToken token = default) => factory.OpenAsync(kind, layer, "same", null, false, true, null, token, policy);
    [Fact]
    public async Task SingletonIdentityIncludesKindAndLayer()
    {
        var(factory, loader) = Create();
        var popup = await Open(factory);
        var panel = await Open(factory, UIKind.Panel, UILayer.Panel);
        Assert.NotSame(popup.View, panel.View);
        Assert.Equal(UIKind.Panel, panel.Kind);
        Assert.Equal(2, factory.ActiveCount);
        Assert.Equal(2, loader.Loads);
    }

    [Fact]
    public async Task DuplicateFocusRefreshAndRejectHaveExplicitLifecycleEffects()
    {
        var(factory, loader) = Create();
        var first = await Open(factory);
        var view = Assert.IsType<Probe>(first.View);
        Assert.Equal(1, view.Opens);
        var focused = await Open(factory, policy: UIDuplicatePolicy.Focus);
        Assert.Equal(first.Id, focused.Id);
        Assert.Equal(1, view.Opens);
        await Open(factory, policy: UIDuplicatePolicy.Refresh);
        Assert.Equal(2, view.Opens);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Open(factory, policy: UIDuplicatePolicy.Reject));
        Assert.True(first.IsValid);
        Assert.Equal(1, loader.Loads);
    }

    [Fact]
    public async Task RefocusingSingletonPopupMovesItToBackStackTop()
    {
        var(factory, _) = Create();
        var popups = new PopupManager(factory);
        var options = new PopupOptions
        {
            SingletonByPath = true,
            DuplicatePolicy = UIDuplicatePolicy.Focus
        };
        var first = await popups.OpenAsync("first", options: options);
        var second = await popups.OpenAsync("second", options: options);
        Assert.Equal(second.Id, popups.Peek()!.Value.Id);
        await popups.OpenAsync("first", options: options);
        Assert.Equal(first.Id, popups.Peek()!.Value.Id);
        Assert.Equal(2, popups.Count);
        Assert.True(popups.CloseTop());
        Assert.Equal(second.Id, popups.Peek()!.Value.Id);
    }

    [Fact]
    public async Task PageRepeatPolicyReusesOrRefreshesOnlyTheTopWithoutAddingHistory()
    {
        var(factory, loader) = Create();
        var pages = new PageNavigator(factory);
        var first = await pages.PushAsync("page");
        var focused = await pages.PushAsync("page", repeatPolicy: PageRepeatPolicy.ReuseTop);
        Assert.Equal(first.Id, focused.Id);
        Assert.Equal(1, pages.Count);
        Assert.Equal(1, Assert.IsType<Probe>(focused.View).Opens);
        await pages.PushAsync("page", repeatPolicy: PageRepeatPolicy.RefreshTop);
        Assert.Equal(2, Assert.IsType<Probe>(focused.View).Opens);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pages.PushAsync("page", repeatPolicy: PageRepeatPolicy.RejectTop));
        Assert.Equal(1, loader.Loads);
    }

    [Fact]
    public async Task AsyncDisposalWaitsForNativeDestructionBeforeReleasingPrefab()
    {
        var(factory, loader) = Create();
        await Open(factory);
        Object.DeferGameObjectDestructionForTest = true;
        var closing = factory.DisposeAsync();
        Assert.False(closing.IsCompleted);
        Assert.Equal(0, loader.Releases);
        Object.FlushDestroyQueueForTest();
        await closing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, loader.Releases);
    }

    [Fact]
    public async Task AsyncDisposalWaitsForLateLegacyLoadsAndReleasesTheirLeases()
    {
        var(factory, loader) = Create();
        var late = new TaskCompletionSource<GameObject?>();
        loader.Pending = late.Task;
        var opening = Open(factory);
        var closing = factory.DisposeAsync();
        Assert.False(closing.IsCompleted);
        late.SetResult(loader.Prefab);
        Assert.False((await opening).IsValid);
        await closing.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, loader.Releases);
    }

    [Fact]
    public async Task FactoryCancellationReachesCancellableLoaderWithoutCancellingOtherFactories()
    {
        var loader = new CancellableLoader();
        var root = new GameObject().transform;
        using var factory = new UIInstanceFactory(loader, new() { [UILayer.Popup] = root }, 1);
        var opening = Open(factory);
        factory.Dispose();
        Assert.False((await opening).IsValid);
        Assert.True(loader.Token.IsCancellationRequested);
        await factory.DisposeAsync().WaitAsync(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RootShutdownIncludesRetiredFactoriesAndRepeatedCallsCannotFinishEarly()
    {
        var prefab = new GameObject("root prefab");
        prefab.SetActive(false);
        prefab.AddComponent<Probe>();
        var previous = new Loader(prefab);
        var root = UIManager.Instance;
        root.SetAssetLoader(previous);
        var handle = await root.Popups.OpenAsync("old");
        Object.DeferGameObjectDestructionForTest = true;
        root.SetAssetLoader(new Loader(prefab));
        var first = root.ShutdownAsync();
        var repeated = UIManager.Instance.ShutdownAsync();
        Assert.False(first.IsCompleted);
        Assert.False(repeated.IsCompleted);
        Assert.Equal(0, previous.Releases);
        Object.FlushDestroyQueueForTest();
        await Task.WhenAll(first, repeated).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, previous.Releases);
        Assert.False(root.IsInitialized);
    }

    private sealed class Loader : IUIAssetLoader
    {
        internal readonly GameObject Prefab;
        internal int Loads, Releases;
        internal Task<GameObject?>? Pending;
        internal Loader(GameObject prefab) => Prefab = prefab;
        public Task<GameObject?> LoadPrefabAsync(string path)
        {
            Loads++;
            return Pending ?? Task.FromResult<GameObject?>(Prefab);
        }

        public void ReleasePrefab(GameObject prefab) => Releases++;
    }

    private sealed class CancellableLoader : ICancellableUIAssetLoader
    {
        internal CancellationToken Token;
        public Task<GameObject?> LoadPrefabAsync(string path) => throw new InvalidOperationException("Use cancellable overload.");
        public async Task<GameObject?> LoadPrefabAsync(string path, CancellationToken token)
        {
            Token = token;
            await Task.Delay(Timeout.Infinite, token);
            return null;
        }

        public void ReleasePrefab(GameObject prefab) => throw new InvalidOperationException("No lease was acquired.");
    }

    private sealed class Probe : UIPopup
    {
        internal int Opens;
        protected override void OnOpen(object? args) => Opens++;
    }
}

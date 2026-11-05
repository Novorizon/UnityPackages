using System.Collections;
using System.Reflection;
using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
// Production UI sources, driven by the existing Unity lifecycle contract.
public sealed class UIManagerRegressionTests : IDisposable
{
    readonly List<UIInstanceFactory> factories = new();
    public UIManagerRegressionTests()
    {
        Object.ResetTestState();
        ProbeToast.Opened = 0;
    }

    public void Dispose()
    {
        foreach (var factory in factories)
        {
            factory.Dispose();
        }

        Object.ResetTestState();
    }

    (UIInstanceFactory Factory, Transform Root) Create<T>()
        where T : UIView
    {
        var root = new GameObject("ManagerTestLayer").transform;
        var prefab = new GameObject("ManagerTestPrefab");
        prefab.SetActive(false);
        prefab.AddComponent<T>();
        var loader = new Loader(prefab);
        var factory = new UIInstanceFactory(loader, Enum.GetValues<UILayer>().ToDictionary(layer => layer, _ => root), 1);
        factories.Add(factory);
        return (factory, root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PopRestoresTheCurrentTopWhenCloseThrowsIncludingReentrantPush(bool reenter)
    {
        var(factory, _) = Create<ProbePage>();
        var pages = new PageNavigator(factory);
        var first = await pages.PushAsync("first");
        var second = await pages.PushAsync("second");
        var closing = Assert.IsType<ProbePage>(second.View);
        closing.FailClose = true;
        Task<UIHandle>? replacement = null;
        if (reenter)
        {
            closing.CloseAction = () => replacement = pages.PushAsync("replacement");
        }

        Assert.Throws<InvalidOperationException>(() => pages.Pop());
        Assert.False(second.IsValid);
        if (reenter)
        {
            var current = await replacement!;
            Assert.Equal(2, pages.Count);
            Assert.Equal(current.Id, pages.Peek()!.Value.Id);
            Assert.True(current.View!.gameObject.activeSelf);
            Assert.False(first.View!.gameObject.activeSelf);
            Assert.True(pages.Pop());
        }

        Assert.Equal(1, pages.Count);
        Assert.Equal(first.Id, pages.Peek()!.Value.Id);
        Assert.True(first.IsValid);
        Assert.True(first.View!.gameObject.activeSelf);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HidingGroupTopRestoresTheCurrentTopWhenCloseThrowsIncludingReentrantPush(bool reenter)
    {
        var(factory, _) = Create<ProbePanel>();
        var panels = new PanelManager(factory);
        var first = await panels.PushInGroupAsync("group", "first");
        var second = await panels.PushInGroupAsync("group", "second");
        var closing = Assert.IsType<ProbePanel>(second.View);
        closing.FailClose = true;
        Task<UIHandle>? replacement = null;
        if (reenter)
        {
            closing.CloseAction = () => replacement = panels.PushInGroupAsync("group", "replacement");
        }

        Assert.Throws<InvalidOperationException>(() => panels.Hide(second));
        Assert.False(second.IsValid);
        if (reenter)
        {
            var current = await replacement!;
            Assert.True(panels.IsShown("replacement"));
            Assert.False(first.View!.gameObject.activeSelf);
            Assert.True(panels.Hide(current));
        }

        Assert.True(first.IsValid);
        Assert.True(first.View!.gameObject.activeSelf);
        Assert.True(panels.IsShown("first"));
    }

    [Fact]
    public async Task ToastCloseExceptionDoesNotNeedAnotherEnqueueToAdvanceTheQueue()
    {
        var(factory, root) = Create<ProbeToast>();
        using var toasts = new ToastManager(factory);
        toasts.Enqueue("first");
        toasts.Enqueue("second");
        var first = Assert.Single(root.gameObject.GetComponentsInChildren<ProbeToast>(true));
        first.FailClose = true;
        first.Finish();
        await DrainAsync();
        Assert.Equal(2, ProbeToast.Opened);
        Assert.True(first.DestroyedForTest);
        Assert.Single(Debug.Exceptions);
        Assert.Equal(1, factory.ActiveCount);
        var second = Assert.Single(root.gameObject.GetComponentsInChildren<ProbeToast>(true));
        Assert.NotSame(first, second);
        Assert.True(second.IsOpen);
        second.Finish();
        await DrainAsync();
        Assert.Equal(0, factory.ActiveCount);
        toasts.Enqueue("third");
        Assert.Equal(3, ProbeToast.Opened);
        toasts.Clear();
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task ToastCloseReentryCannotClearTheReplacementGenerationsCurrentHandle()
    {
        var(factory, root) = Create<ProbeToast>();
        using var toasts = new ToastManager(factory);
        toasts.Enqueue("first");
        toasts.Enqueue("obsolete-queued");
        var first = Assert.Single(root.gameObject.GetComponentsInChildren<ProbeToast>(true));
        first.CloseAction = () =>
        {
            toasts.Clear();
            toasts.Enqueue("replacement");
        };
        first.Finish();
        await DrainAsync();
        Assert.Equal(2, ProbeToast.Opened);
        Assert.Equal(1, factory.ActiveCount);
        toasts.Clear(); // Requires the new runner's current handle to survive the old runner's finally.
        Assert.Equal(0, factory.ActiveCount);
        toasts.Enqueue("later");
        Assert.Equal(3, ProbeToast.Opened);
    }

    static async Task DrainAsync()
    {
        for (int i = 0; i < 6; i++)
        {
            await Task.Yield();
        }
    }

    [Fact]
    public async Task ExternallyDestroyedPopupsAreRemovedWithoutCallingCountOrPeek()
    {
        var(factory, _) = Create<ProbePopup>();
        var popups = new PopupManager(factory);
        // Count/Peek themselves prune. Inspect retained registrations so lazy cleanup cannot hide a leak.
        var registrations = Assert.IsAssignableFrom<ICollection>(typeof(PopupManager).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popups));
        for (int i = 0; i < 128; i++)
        {
            var handle = await popups.OpenAsync("popup");
            Object.Destroy(handle.View!.gameObject);
            Assert.Empty(registrations);
        }

        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task ClosingOldPopupUnsubscribesBeforeOnDisableCanReopenItsCachedView()
    {
        var(factory, _) = Create<ProbePopup>();
        var popups = new PopupManager(factory);
        var options = new PopupOptions
        {
            SingletonByPath = true
        };
        var original = await popups.OpenAsync("popup", options: options);
        Task<UIHandle>? replacement = null;
        Assert.IsType<ProbePopup>(original.View).DisableAction = () => replacement = popups.OpenAsync("popup", options: options);
        popups.Close(original);
        var fresh = await replacement!;
        Assert.Same(original.View, fresh.View);
        Assert.NotEqual(original.Id, fresh.Id);
        Assert.True(fresh.IsValid);
        Assert.False(original.IsValid);
        Assert.Equal(1, popups.Count);
        var registrations = Assert.IsAssignableFrom<ICollection>(typeof(PopupManager).GetField("opened", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(popups));
        Object.Destroy(fresh.View!.gameObject);
        Assert.Empty(registrations);
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task SingletonOnOpenCloseAndReopenKeepsOneNewLeaseAndRootDisposalDetachesIt()
    {
        var(factory, _) = Create<ProbePopup>();
        var popups = new PopupManager(factory);
        var options = new PopupOptions
        {
            SingletonByPath = true
        };
        var original = await popups.OpenAsync("popup", options: options);
        Task<UIHandle>? inner = null;
        Assert.IsType<ProbePopup>(original.View).OpenAction = () =>
        {
            popups.Close(original);
            inner = popups.OpenAsync("popup", options: options);
        };
        var outer = await popups.OpenAsync("popup", options: options);
        var fresh = await inner!;
        Assert.Same(original.View, fresh.View);
        Assert.NotEqual(original.Id, fresh.Id);
        Assert.False(original.IsValid);
        Assert.False(outer.IsValid);
        Assert.True(fresh.IsValid);
        Assert.Equal(1, popups.Count);
        factory.Dispose();
        Assert.Equal(0, popups.Count);
        Assert.False(fresh.IsValid);
    }

    sealed class Loader(GameObject prefab) : IUIAssetLoader
    {
        public Task<GameObject?> LoadPrefabAsync(string path) => Task.FromResult<GameObject?>(prefab);
        public void ReleasePrefab(GameObject value)
        {
            Assert.Same(prefab, value);
        }
    }

    sealed class ProbePage : UIPage
    {
        public bool FailClose = false;
        public Action? CloseAction = null;
        protected override void OnClose()
        {
            var action = CloseAction;
            CloseAction = null;
            action?.Invoke();
            if (FailClose)
            {
                throw new InvalidOperationException("Page close failure.");
            }
        }
    }

    sealed class ProbePanel : UIPanel
    {
        public bool FailClose = false;
        public Action? CloseAction = null;
        protected override void OnClose()
        {
            var action = CloseAction;
            CloseAction = null;
            action?.Invoke();
            if (FailClose)
            {
                throw new InvalidOperationException("Panel close failure.");
            }
        }
    }

    sealed class ProbeToast : UIToast
    {
        public static int Opened;
        public bool FailClose = false;
        public Action? CloseAction = null;
        public override float Duration => 0;

        public void Finish() => Complete();
        protected override void OnOpen(object? args)
        {
            Opened++;
        }

        protected override void OnClose()
        {
            var action = CloseAction;
            CloseAction = null;
            action?.Invoke();
            if (FailClose)
            {
                throw new InvalidOperationException("Toast close failure.");
            }
        }
    }

    sealed class ProbePopup : UIPopup
    {
        public Action? DisableAction = null;
        public Action? OpenAction = null;
        void OnDisable()
        {
            var action = DisableAction;
            DisableAction = null;
            action?.Invoke();
        }

        protected override void OnOpen(object? args)
        {
            var action = OpenAction;
            OpenAction = null;
            action?.Invoke();
        }
    }
}
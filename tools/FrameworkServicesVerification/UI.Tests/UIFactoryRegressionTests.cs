using System.Collections;
using System.Reflection;
using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
// Factory ownership regressions against the deterministic Unity lifecycle contract.
public sealed class UIFactoryRegressionTests : IDisposable
{
    readonly List<UIInstanceFactory> factories = new();
    public UIFactoryRegressionTests()
    {
        Object.ResetTestState();
        UIDestructionObserver.ResetForPlay();
        DisposeOnEnable.Factory = null;
        DisposeOnEnable.Created = 0;
        AbortOnParentChanged.Factory = null;
        AbortOnParentChanged.EnabledCalls = 0;
        CloseFromCachedEnable.Factory = null;
    }

    public void Dispose()
    {
        foreach (var factory in factories)
        {
            factory.Dispose();
        }

        Object.FlushDestroyQueueForTest();
        Object.AdvanceFrameForTest();
        Object.ResetTestState();
        UIDestructionObserver.ResetForPlay();
    }

    (UIInstanceFactory Factory, Loader Loader, GameObject Layer) Create(Type? type = null, bool activePrefab = false, bool delayed = false)
    {
        var layer = new GameObject("Factory regression layer");
        var prefab = new GameObject("Factory regression prefab");
        prefab.SetActive(false);
        prefab.AddComponent(type ?? typeof(CallbackPopup));
        if (activePrefab)
        {
            prefab.SetActive(true);
        }

        var loader = new Loader(prefab, delayed);
        var roots = Enum.GetValues<UILayer>().ToDictionary(value => value, _ => layer.transform);
        var factory = new UIInstanceFactory(loader, roots, 1);
        factories.Add(factory);
        return (factory, loader, layer);
    }

    static Task<UIHandle> Open(UIInstanceFactory factory, object? args = null, Func<Transform, UIBlocker?>? blocker = null) => factory.OpenAsync(UIKind.Popup, UILayer.Popup, "popup", args, false, true, blocker);
    static int Tracked(UIInstanceFactory factory) => ((IDictionary)typeof(UIInstanceFactory).GetField("instances", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(factory)!).Count;
    [Fact]
    public async Task ActivePrefabOnEnableDisposalDestroysTheRegisteredCloneAndReleasesItsLeaseAtFrameEnd()
    {
        var(factory, loader, layer) = Create(typeof(DisposeOnEnable), activePrefab: true);
        DisposeOnEnable.Factory = factory;
        Object.DeferGameObjectDestructionForTest = true;
        var handle = await Open(factory);
        Assert.False(handle.IsValid);
        Assert.True(factory.IsDisposed);
        Assert.Equal(0, DisposeOnEnable.Created);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, loader.Releases);
        Assert.All(layer.GetComponentsInChildren<DisposeOnEnable>(true), view => Assert.False(view.gameObject.activeSelf));
        Object.FlushDestroyQueueForTest();
        Object.AdvanceFrameForTest();
        Assert.Equal(0, layer.transform.childCount);
        Assert.Equal(0, Tracked(factory));
        Assert.Equal(loader.Loads, loader.Releases);
    }

    [Fact]
    public async Task InactiveLayerIsRejectedBeforeAnyLoadOrNativeCloneIsCreated()
    {
        var(factory, loader, layer) = Create();
        layer.SetActive(false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Open(factory));
        Assert.Equal(0, loader.Loads);
        Assert.Equal(0, loader.Releases);
        Assert.Equal(0, Tracked(factory));
        Assert.Equal(0, layer.transform.childCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LayerBecomingUnavailableDuringLoadReturnsAcquiredLeaseWithoutCreatingAView(bool destroyed)
    {
        var(factory, loader, layer) = Create(delayed: true);
        var opening = Open(factory);
        Assert.False(opening.IsCompleted);
        if (destroyed)
        {
            Object.Destroy(layer);
        }
        else
        {
            layer.SetActive(false);
        }

        loader.Complete();
        await Assert.ThrowsAsync<InvalidOperationException>(() => opening);
        Assert.Equal(1, loader.Loads);
        Assert.Equal(1, loader.Releases);
        Assert.Equal(0, Tracked(factory));
    }

    [Fact]
    public async Task ParentChangeDisposalBeforeFirstActivationUsesNativeDestructionFallback()
    {
        var(factory, loader, layer) = Create(typeof(AbortOnParentChanged));
        AbortOnParentChanged.Factory = factory;
        Object.DeferGameObjectDestructionForTest = true;
        var handle = await Open(factory);
        Assert.False(handle.IsValid);
        Assert.True(factory.IsDisposed);
        Assert.Equal(0, AbortOnParentChanged.EnabledCalls);
        Assert.Equal(0, loader.Releases);
        Assert.Equal(1, Tracked(factory));
        Object.FlushDestroyQueueForTest();
        Assert.Equal(0, layer.transform.childCount);
        Assert.Equal(0, loader.Releases);
        Object.AdvanceFrameForTest();
        Assert.Equal(1, loader.Releases);
        Assert.Equal(0, Tracked(factory));
        Object.AdvanceFrameForTest();
        factory.Dispose();
        Assert.Equal(1, loader.Releases);
        var observer = Object.FindFirstObjectByType<UIDestructionObserver>();
        Assert.NotNull(observer);
        Assert.False(observer.enabled);
    }

    [Fact]
    public async Task QuitDoesNotReleaseAnAliveNeverActivatedCloneAndNextPlayClearsTheDeadTicket()
    {
        var(factory, loader, _) = Create(typeof(AbortOnParentChanged));
        AbortOnParentChanged.Factory = factory;
        UIDestructionObserver.StopForQuit();
        Object.DeferGameObjectDestructionForTest = true;
        await Open(factory);
        Assert.Equal(0, loader.Releases);
        Assert.Null(Object.FindFirstObjectByType<UIDestructionObserver>());
        Object.FlushDestroyQueueForTest();
        Assert.Equal(0, loader.Releases);
        UIDestructionObserver.ResetForPlay();
        Assert.Equal(1, loader.Releases);
        Assert.Equal(0, Tracked(factory));
        UIDestructionObserver.ResetForPlay();
        Assert.Equal(1, loader.Releases);
    }

    [Fact]
    public async Task CachedOnEnableCanCloseItsCurrentLeaseWithoutOuterOpenRevivingIt()
    {
        var(factory, loader, _) = Create(typeof(CloseFromCachedEnable));
        CloseFromCachedEnable.Factory = factory;
        var first = await Open(factory);
        factory.Close(first, false, true);
        var interrupted = await Open(factory);
        Assert.False(interrupted.IsValid);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(1, factory.CachedCount);
        Assert.False(first.View!.IsOpen);
        Assert.False(first.View.gameObject.activeSelf);
        var fresh = await Open(factory);
        Assert.True(fresh.IsValid);
        Assert.Same(first.View, fresh.View);
        Assert.NotEqual(first.Id, fresh.Id);
        factory.Close(fresh, true, false);
        Assert.Equal(1, loader.Loads);
        Assert.Equal(1, loader.Releases);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReopenCallbackCannotMakeAnOuterCallAdoptOrDestroyTheNewLease(bool throwAfterReopen)
    {
        var(factory, loader, _) = Create();
        var original = await Open(factory);
        UIHandle fresh = default;
        Action callback = () =>
        {
            factory.Close(original, false, true);
            fresh = Open(factory).GetAwaiter().GetResult();
            if (throwAfterReopen)
            {
                throw new InvalidOperationException("original callback failed after reopening");
            }
        };
        if (throwAfterReopen)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Open(factory, callback));
        }
        else
        {
            Assert.False((await Open(factory, callback)).IsValid);
        }

        Assert.True(fresh.IsValid);
        Assert.False(original.IsValid);
        Assert.Equal(1, factory.ActiveCount);
        Assert.Equal(0, factory.CachedCount);
        Assert.Equal(1, loader.Loads);
        Assert.Equal(0, loader.Releases);
        factory.Dispose();
        Assert.Equal(1, loader.Releases);
    }

    [Fact]
    public async Task ABlockerReturnedAfterReentrantReopenCannotOverwriteTheNewLeasesBlocker()
    {
        var(factory, loader, layer) = Create();
        UIHandle fresh = default;
        UIBlocker? innerBlocker = null, lateBlocker = null;
        Object.DeferGameObjectDestructionForTest = true;
        var outer = await Open(factory, blocker: parent =>
        {
            var current = Open(factory).GetAwaiter().GetResult();
            factory.Close(current, false, true);
            fresh = Open(factory, blocker: target => innerBlocker = UIBlocker.Create(target)).GetAwaiter().GetResult();
            return lateBlocker = UIBlocker.Create(parent);
        });
        Assert.False(outer.IsValid);
        Assert.True(fresh.IsValid);
        Assert.Equal(0, loader.Releases);
        Object.FlushDestroyQueueForTest();
        Assert.NotNull(lateBlocker);
        Assert.True(lateBlocker.DestroyedForTest);
        Assert.NotNull(innerBlocker);
        Assert.False(innerBlocker.DestroyedForTest);
        Assert.Same(innerBlocker, Assert.Single(layer.GetComponentsInChildren<UIBlocker>(true)));
        factory.Dispose();
        Assert.Equal(0, loader.Releases);
        Object.FlushDestroyQueueForTest();
        Object.AdvanceFrameForTest();
        Assert.Equal(1, loader.Releases);
    }

    sealed class Loader : IUIAssetLoader
    {
        readonly GameObject prefab;
        readonly TaskCompletionSource<GameObject?>? pending;
        public int Loads, Releases;
        public Loader(GameObject prefab, bool delayed)
        {
            this.prefab = prefab;
            if (delayed)
            {
                pending = new TaskCompletionSource<GameObject?>();
            }
        }

        public Task<GameObject?> LoadPrefabAsync(string path)
        {
            Loads++;
            return pending?.Task ?? Task.FromResult<GameObject?>(prefab);
        }

        public void Complete() => pending!.SetResult(prefab);
        public void ReleasePrefab(GameObject value)
        {
            Assert.Same(prefab, value);
            Releases++;
        }
    }

    sealed class CallbackPopup : UIPopup
    {
        protected override void OnOpen(object? args) => (args as Action)?.Invoke();
    }

    sealed class DisposeOnEnable : UIPopup
    {
        public static UIInstanceFactory? Factory;
        public static int Created;
        void OnEnable() => Factory?.Dispose();
        protected override void OnCreate() => Created++;
    }

    sealed class AbortOnParentChanged : UIPopup
    {
        public static UIInstanceFactory? Factory;
        public static int EnabledCalls;
        void OnTransformParentChanged() => Factory?.Dispose();
        void OnEnable() => EnabledCalls++;
    }

    sealed class CloseFromCachedEnable : UIPopup
    {
        public static UIInstanceFactory? Factory;
        int enables;
        void OnEnable()
        {
            if (++enables != 2)
            {
                return;
            }

            var current = Open(Factory!).GetAwaiter().GetResult();
            Factory!.Close(current, false, true);
        }
    }
}
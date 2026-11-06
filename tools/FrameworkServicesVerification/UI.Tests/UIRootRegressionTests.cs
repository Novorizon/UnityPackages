using System.Reflection;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
// Contract/state regressions. They do not simulate native Canvas raycasts or input devices.
public sealed class UIRootRegressionTests : IDisposable
{
    public UIRootRegressionTests()
    {
        Object.ResetTestState();
    }

    public void Dispose()
    {
        Object.ResetTestState();
    }

    static EventSystem[] ActiveSystems() => Object.FindObjectsOfType<EventSystem>().Where(x => x.isActiveAndEnabled).ToArray();
    static Canvas RootCanvas(UIManager root) => root.GetComponentInChildren<Canvas>(true);
    static Canvas Layer(UIManager root, UILayer layer) => RootCanvas(root).transform.Find("Layer_" + layer).GetComponent<Canvas>();
    [Fact]
    public void RootAndLocalNotificationsUseSeparateFrameworkAssembliesWithoutSharingBusState()
    {
        var root = UIManager.Instance;
        Assert.IsAssignableFrom<Game.Framework.MonoSingleton<UIManager>>(root);
        Assert.NotSame(typeof(UIManager).Assembly, typeof(Game.Framework.MonoSingleton<>).Assembly);
        Assert.NotSame(typeof(UIManager).Assembly, typeof(Game.Framework.Event.MessageBus).Assembly);
        int received = 0;
        root.Bus.Subscribe<int>(value => received += value);
        var otherScope = new Game.Framework.Event.MessageBus();
        otherScope.Publish(100);
        Assert.Equal(0, received);
        root.Bus.Publish(2);
        Assert.Equal(2, received);
    }

    [Fact]
    public void ResourceSettingsCanPreventAutomaticEventSystemCreationBeforeRootAwake()
    {
        var settings = ScriptableObject.CreateInstance<UISettings>();
        settings.autoCreateEventSystem = false;
        Resources.RegisterAssetForTest("UISettings", settings);
        var root = UIManager.Instance;
        Assert.Empty(Object.FindObjectsOfType<EventSystem>(true));
        var project = new GameObject("Project Input").AddComponent<EventSystem>();
        root.SetEventSystem(project);
        Assert.Same(project, EventSystem.current);
        Assert.True(project.isActiveAndEnabled);
        Object.Destroy(root.gameObject);
        Assert.False(project.DestroyedForTest);
    }

    [Fact]
    public void SceneEventSystemLossCreatesAnOwnedFallbackAndUserSystemTakesPriority()
    {
        var original = new GameObject("Scene A Input").AddComponent<EventSystem>();
        var root = UIManager.Instance;
        Assert.Same(original, Assert.Single(ActiveSystems()));
        Object.Destroy(original.gameObject);
        SceneManager.UnloadForTest();
        var fallback = Assert.Single(ActiveSystems());
        Assert.Same(root.transform, fallback.transform.parent);
        var next = new GameObject("Scene B Input").AddComponent<EventSystem>();
        SceneManager.LoadForTest();
        Assert.Same(next, Assert.Single(ActiveSystems()));
        Assert.False(fallback.gameObject.activeSelf);
        Assert.True(next.enabled);
        Assert.Null(next.transform.parent);
        Object.Destroy(next.gameObject);
        SceneManager.UnloadForTest();
        Assert.Same(fallback, Assert.Single(ActiveSystems()));
    }

    [Fact]
    public void LosingAProjectSystemWithoutASceneEventRecoversOnTheNextFrame()
    {
        var project = new GameObject("Runtime Input").AddComponent<EventSystem>();
        var root = UIManager.Instance;
        Object.Destroy(project.gameObject);
        Object.AdvanceFrameForTest();
        Assert.Same(root.transform, Assert.Single(ActiveSystems()).transform.parent);
    }

    [Fact]
    public void ExplicitProjectSystemSelectionNeverDisablesOrDestroysOtherProjectSystems()
    {
        var root = UIManager.Instance;
        var fallback = Assert.Single(ActiveSystems());
        var first = new GameObject("Player One Input").AddComponent<EventSystem>();
        var second = new GameObject("Player Two Input").AddComponent<EventSystem>();
        root.SetEventSystem(second);
        Assert.Same(second, EventSystem.current);
        Assert.False(fallback.gameObject.activeSelf);
        Assert.True(first.isActiveAndEnabled);
        Assert.True(second.isActiveAndEnabled);
        Object.Destroy(root.gameObject);
        Assert.False(first.DestroyedForTest);
        Assert.False(second.DestroyedForTest);
    }

    [Fact]
    public void OverlayIsDefaultAndConfiguredOrExplicitCamerasRefreshWithSceneChanges()
    {
        var first = new GameObject("Camera A").AddComponent<Camera>();
        Camera.main = first;
        var root = UIManager.Instance;
        var canvas = RootCanvas(root);
        Assert.Equal(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
        Assert.Null(canvas.worldCamera);
        var settings = ScriptableObject.CreateInstance<UISettings>();
        settings.useMainCamera = true;
        root.SetSettings(settings);
        Assert.Same(first, canvas.worldCamera);
        var second = new GameObject("Camera B").AddComponent<Camera>();
        Camera.main = second;
        SceneManager.LoadForTest();
        Assert.Same(second, canvas.worldCamera);
        root.SetCamera(first);
        SceneManager.ChangeActiveForTest();
        Assert.Same(first, canvas.worldCamera);
        first.enabled = false;
        Object.AdvanceFrameForTest();
        Assert.Same(second, canvas.worldCamera);
        root.SetCamera(null);
        Assert.Same(second, canvas.worldCamera);
        Object.Destroy(second.gameObject);
        Camera.main = null;
        SceneManager.UnloadForTest();
        Assert.Equal(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
        Assert.Null(canvas.worldCamera);
    }

    [Fact]
    public void PanelsSortBelowModalPopupsWithoutChangingTheSerializedEnumIdentity()
    {
        var root = UIManager.Instance;
        Assert.Equal(25, (int)UILayer.Panel);
        Assert.True(Layer(root, UILayer.Page).sortingOrder < Layer(root, UILayer.Panel).sortingOrder);
        Assert.True(Layer(root, UILayer.Panel).sortingOrder < Layer(root, UILayer.Popup).sortingOrder);
        Assert.True(Layer(root, UILayer.Popup).sortingOrder < Layer(root, UILayer.Overlay).sortingOrder);
        var settings = ScriptableObject.CreateInstance<UISettings>();
        settings.sortingOrderStep = int.MaxValue;
        settings.referenceWidth = 0;
        settings.referenceHeight = 0;
        root.SetSettings(settings);
        Assert.InRange(Layer(root, UILayer.Overlay).sortingOrder, 1, short.MaxValue);
        Assert.Equal(1, RootCanvas(root).GetComponent<CanvasScaler>().referenceResolution.x);
    }

    [Fact]
    public void RootBecomesPersistentAtTheSceneRootAndReleasesPlatformSubscriptions()
    {
        var parent = new GameObject("Scene Managers");
        var rootObject = new GameObject("Authored UI Root");
        rootObject.transform.SetParent(parent.transform, false);
        var root = rootObject.AddComponent<UIManager>();
        Assert.Null(root.transform.parent);
        Assert.True(rootObject.PersistentForTest);
        root.Initialize();
        root.Initialize();
        Assert.Equal(1, SceneManager.SceneLoadedSubscriberCountForTest);
        Assert.Equal(1, Application.QuittingSubscriberCountForTest);
        Object.Destroy(rootObject);
        Assert.Equal(0, SceneManager.SceneLoadedSubscriberCountForTest);
        Assert.Equal(0, Application.QuittingSubscriberCountForTest);
    }

    [Fact]
    public void QuitPreventsRecreationAndNewPlaySessionAllowsAFreshRoot()
    {
        var root = UIManager.Instance;
        Application.QuitForTest();
        Object.Destroy(root.gameObject);
        Assert.Throws<InvalidOperationException>(() => _ = UIManager.Instance);
        Assert.Empty(ActiveSystems());
        Object.ResetTestState();
        var fresh = UIManager.Instance;
        Assert.NotSame(root, fresh);
        Assert.Single(ActiveSystems());
    }

    [Fact]
    public void PlayResetReinitializesASurvivingRootAndClearsItsOldManagersAndBus()
    {
        var root = UIManager.Instance;
        var oldPopups = root.Popups;
        int received = 0;
        root.Bus.Subscribe<int>(_ => received++);
        Object.ResetSingletonsForPlay();
        Assert.Same(root, UIManager.Instance);
        Assert.NotSame(oldPopups, root.Popups);
        root.Bus.Publish(42);
        Assert.Equal(0, received);
        Assert.Equal(1, SceneManager.SceneLoadedSubscriberCountForTest);
        Assert.Equal(1, Application.QuittingSubscriberCountForTest);
    }

    [Fact]
    public void RetainedRootCanStartAgainAfterItsQuitHook()
    {
        var root = UIManager.Instance;
        var oldPopups = root.Popups;
        typeof(UIManager).GetMethod("OnApplicationQuit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(root, null);
        Object.ResetSingletonsForPlay();
        Assert.Same(root, UIManager.Instance);
        Assert.True(root.IsInitialized);
        Assert.NotSame(oldPopups, root.Popups);
        Assert.Equal(1, SceneManager.SceneLoadedSubscriberCountForTest);
    }

    [Fact]
    public void InactiveParentsAndReparentingPropagateActivationToChildComponents()
    {
        var staging = new GameObject("Inactive Staging");
        staging.SetActive(false);
        var child = new GameObject("Child");
        child.transform.SetParent(staging.transform, false);
        var parentProbe = child.AddComponent<ActivationProbe>();
        var grandchild = new GameObject("Grandchild");
        grandchild.transform.SetParent(child.transform, false);
        var childProbe = grandchild.AddComponent<ActivationProbe>();
        Assert.Equal(0, parentProbe.AwakeCalls);
        Assert.Equal(0, childProbe.EnableCalls);
        var activeRoot = new GameObject("Active Parent");
        child.transform.SetParent(activeRoot.transform, false);
        Assert.Equal(1, parentProbe.AwakeCalls);
        Assert.Equal(1, childProbe.AwakeCalls);
        Assert.Equal(1, parentProbe.EnableCalls);
        Assert.Equal(1, childProbe.EnableCalls);
        activeRoot.SetActive(false);
        Assert.Equal(1, parentProbe.DisableCalls);
        Assert.Equal(1, childProbe.DisableCalls);
        activeRoot.SetActive(true);
        Assert.Equal(1, parentProbe.AwakeCalls);
        Assert.Equal(2, childProbe.EnableCalls);
    }

    public sealed class ActivationProbe : MonoBehaviour
    {
        public int AwakeCalls, EnableCalls, DisableCalls;
        void Awake()
        {
            AwakeCalls++;
        }

        void OnEnable()
        {
            EnableCalls++;
        }

        void OnDisable()
        {
            DisableCalls++;
        }
    }
}

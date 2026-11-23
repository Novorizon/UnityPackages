using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
public sealed class UIAssetLoaderFeatureTests : IDisposable
{
    public UIAssetLoaderFeatureTests() => Object.ResetTestState();
    public void Dispose() => Object.ResetTestState();
    [Fact]
    public void DifferentProjectLoadersCanBeSwitchedWithoutSharingOrLeakingOwnership() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var first = Prefab("Project A");
            var second = Prefab("Project B");
            int firstLoads = 0, firstReleases = 0, secondLoads = 0, secondReleases = 0;
            var root = UIManager.Instance;
            UIManager.SetAssetLoader(new DelegateUIAssetLoader(_ =>
            {
                firstLoads++;
                return Task.FromResult<GameObject?>(first);
            }, prefab =>
            {
                Assert.Same(first, prefab);
                firstReleases++;
            }));
            var handle = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("project-specific-key"));
            UIManager.Popups.Close(handle);
            Assert.Equal(1, firstLoads);
            Assert.Equal(0, firstReleases);
            UIManager.SetAssetLoader(new DelegateUIAssetLoader(_ =>
            {
                secondLoads++;
                return Task.FromResult<GameObject?>(second);
            }, prefab =>
            {
                Assert.Same(second, prefab);
                secondReleases++;
            }));
            Assert.Equal(1, firstReleases);
            var replacement = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("another-project-key"));
            Assert.True(replacement.IsValid);
            Assert.False(handle.IsValid);
            Object.Destroy(root.gameObject);
            Assert.Equal(1, firstReleases);
            Assert.Equal(1, secondLoads);
            Assert.Equal(1, secondReleases);
        });

    [Fact]
    public void ALateLoadReturnsToItsOriginalProviderAfterRootSwitchesProviders() =>
        UIDialogTests.QueuedContext.RunOnCurrentThread(async () =>
        {
            var latePrefab = Prefab("Late provider");
            var currentPrefab = Prefab("Current provider");
            var pending = new TaskCompletionSource<GameObject?>();
            int lateReleases = 0;
            var root = UIManager.Instance;
            UIManager.SetAssetLoader(new DelegateUIAssetLoader(_ => pending.Task, prefab =>
            {
                Assert.Same(latePrefab, prefab);
                lateReleases++;
            }));
            var opening = UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("late"));
            UIManager.SetAssetLoader(new DelegateUIAssetLoader(_ => Task.FromResult<GameObject?>(currentPrefab), _ =>
            {
            }));
            Assert.False((await opening).IsValid);
            pending.SetResult(latePrefab);
            for (int i = 0; i < 8; i++)
            {
                await Task.Yield();
            }

            Assert.Equal(1, lateReleases);
            var current = await UIManager.Popups.OpenAsync(new UIKey<ProbePopup>("current"));
            Assert.True(current.IsValid);
            Assert.Equal(1, lateReleases);
        });

    [Fact]
    public async Task MismatchedKeyReleasesThePrefabWithoutActivatingOrCachingIt()
    {
        var prefab = Prefab("Actual popup");
        int releases = 0;
        UIManager.SetAssetLoader(new DelegateUIAssetLoader(_ => Task.FromResult<GameObject?>(prefab), released =>
        {
            Assert.Same(prefab, released);
            releases++;
        }));

        UIHandle<OtherPopup> handle = await UIManager.Popups.OpenAsync(new UIKey<OtherPopup>("popup"));
        Assert.False(handle.IsValid);
        Assert.Null(handle.View);
        Assert.Empty(UIManager.Instance.gameObject.GetComponentsInChildren<ProbePopup>(true));
        Assert.Equal(0, UIManager.ActiveViewCount);
        Assert.Equal(0, UIManager.CachedViewCount);
        Assert.Equal(1, releases);
    }

    [Fact]
    public async Task WrongKeyCannotRefreshAnActivePopupOrTakeItsCachedView()
    {
        var prefab = Prefab("Actual popup");
        var key = new UIKey<ProbePopup>("popup");
        var wrongKey = new UIKey<OtherPopup>(key.Path);
        var options = new PopupOptions { SingletonByPath = true };
        int loads = 0, releases = 0;
        UIManager.SetAssetLoader(new DelegateUIAssetLoader(_ =>
        {
            loads++;
            return Task.FromResult<GameObject?>(prefab);
        }, _ => releases++));

        UIHandle<ProbePopup> original = await UIManager.Popups.OpenAsync(key, options: options);
        ProbePopup view = Assert.IsType<ProbePopup>(original.View);
        Assert.Equal(key.Path, original.Path);
        Assert.Equal(UIKind.Popup, original.Kind);
        Assert.Equal(UILayer.Popup, original.Layer);
        Assert.False((await UIManager.Popups.OpenAsync(wrongKey, options: options)).IsValid);
        Assert.Equal(1, loads);
        Assert.True(original.IsValid);

        UIManager.Popups.Close(original);
        Assert.False((await UIManager.Popups.OpenAsync(wrongKey, options: options)).IsValid);
        Assert.Equal(2, loads);
        Assert.Equal(1, releases);
        Assert.Equal(1, UIManager.CachedViewCount);

        var reopened = await UIManager.Popups.OpenAsync(key, options: options);
        Assert.Same(view, reopened.View);
        Assert.NotEqual(original.Id, reopened.Id);
        Assert.False(UIManager.Popups.TryClose(original));
        Assert.True(reopened.IsValid);
        Assert.Equal(2, loads);
    }

    static GameObject Prefab(string name)
    {
        var prefab = new GameObject(name);
        prefab.SetActive(false);
        prefab.AddComponent<ProbePopup>();
        return prefab;
    }

    sealed class OtherPopup : UIPopup
    {
    }

    sealed class ProbePopup : UIPopup
    {
    }
}
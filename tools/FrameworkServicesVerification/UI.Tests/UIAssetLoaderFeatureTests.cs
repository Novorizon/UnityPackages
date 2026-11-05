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
    public async Task DifferentProjectLoadersCanBeSwitchedWithoutSharingOrLeakingOwnership()
    {
        var first = Prefab("Project A");
        var second = Prefab("Project B");
        int firstLoads = 0, firstReleases = 0, secondLoads = 0, secondReleases = 0;
        var root = UIManager.Instance;
        root.SetAssetLoader(new DelegateUIAssetLoader(_ =>
        {
            firstLoads++;
            return Task.FromResult<GameObject?>(first);
        }, prefab =>
        {
            Assert.Same(first, prefab);
            firstReleases++;
        }));
        var handle = await root.Popups.OpenAsync("project-specific-key");
        root.Popups.Close(handle);
        Assert.Equal(1, firstLoads);
        Assert.Equal(0, firstReleases);
        root.SetAssetLoader(new DelegateUIAssetLoader(_ =>
        {
            secondLoads++;
            return Task.FromResult<GameObject?>(second);
        }, prefab =>
        {
            Assert.Same(second, prefab);
            secondReleases++;
        }));
        Assert.Equal(1, firstReleases);
        var replacement = await root.Popups.OpenAsync("another-project-key");
        Assert.True(replacement.IsValid);
        Assert.False(handle.IsValid);
        Object.Destroy(root.gameObject);
        Assert.Equal(1, firstReleases);
        Assert.Equal(1, secondLoads);
        Assert.Equal(1, secondReleases);
    }

    [Fact]
    public async Task ALateLoadReturnsToItsOriginalProviderAfterRootSwitchesProviders()
    {
        var latePrefab = Prefab("Late provider");
        var currentPrefab = Prefab("Current provider");
        var pending = new TaskCompletionSource<GameObject?>();
        int lateReleases = 0;
        var root = UIManager.Instance;
        root.SetAssetLoader(new DelegateUIAssetLoader(_ => pending.Task, prefab =>
        {
            Assert.Same(latePrefab, prefab);
            lateReleases++;
        }));
        var opening = root.Popups.OpenAsync("late");
        root.SetAssetLoader(new DelegateUIAssetLoader(_ => Task.FromResult<GameObject?>(currentPrefab), _ =>
        {
        }));
        Assert.False((await opening).IsValid);
        pending.SetResult(latePrefab);
        for (int i = 0; i < 8; i++)
        {
            await Task.Yield();
        }

        Assert.Equal(1, lateReleases);
        var current = await root.Popups.OpenAsync("current");
        Assert.True(current.IsValid);
        Assert.Equal(1, lateReleases);
    }

    static GameObject Prefab(string name)
    {
        var prefab = new GameObject(name);
        prefab.SetActive(false);
        prefab.AddComponent<ProbePopup>();
        return prefab;
    }

    sealed class ProbePopup : UIPopup
    {
    }
}
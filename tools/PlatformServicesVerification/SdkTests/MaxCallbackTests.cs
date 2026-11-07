using System;
using System.Collections;
using System.Collections.Generic;
using Game.Framework;
using Game.Framework.Ads;
using Game.Framework.Ads.AppLovin;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class MaxCallbackTests
{
    [UnityTest]
    public IEnumerator LateMaxRewardCannotGrantTheNextShowOrDuplicateItself()
    {
        UnityThread.Initialize();
        var provider = new MaxAdProvider(false, "acceptance-user");
        var client = new AdsClient(provider, true);
        var rewards = new List<AdReward>();
        client.RewardEarned += rewards.Add;
        var initialize = client.InitializeAsync();
        yield return new WaitUntil(() => initialize.IsCompleted);
        Assert.IsFalse(initialize.IsFaulted);
        var placement = new AdPlacement("stable-business-place", "acceptance-rewarded", AdFormat.Rewarded);
        string displayed = null;
        Action<string, MaxSdkBase.AdInfo> onDisplayed = (_, info) => displayed = info.Placement;
        MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent += onDisplayed;
        var load = client.LoadAsync(placement);
        yield return new WaitUntil(() => load.IsCompleted);
        Assert.IsFalse(load.IsFaulted);
        var first = client.ShowAsync(placement);
        yield return new WaitUntil(() => displayed != null || first.IsCompleted);
        Assert.IsFalse(first.IsFaulted, first.Exception?.ToString());
        Assert.IsNotNull(displayed);
        string firstId = displayed;
        Emit("OnRewardedAdHiddenEvent", firstId);
        yield return new WaitUntil(() => first.IsCompleted);
        Assert.IsFalse(first.IsFaulted);
        Assert.AreEqual(0, rewards.Count);
        load = client.LoadAsync(placement);
        yield return new WaitUntil(() => load.IsCompleted);
        displayed = null;
        var second = client.ShowAsync(placement);
        yield return new WaitUntil(() => displayed != null || second.IsCompleted);
        Assert.IsFalse(second.IsFaulted, second.Exception?.ToString());
        Assert.IsNotNull(displayed);
        string secondId = displayed;
        Assert.AreNotEqual(firstId, secondId);
        Emit("OnRewardedAdReceivedRewardEvent", firstId);
        Emit("OnRewardedAdReceivedRewardEvent", firstId);
        yield return null;
        Assert.AreEqual(1, rewards.Count);
        Assert.AreEqual(firstId, rewards[0].ShowId);
        Assert.AreEqual("stable-business-place", rewards[0].PlacementId);
        Assert.IsFalse(second.IsCompleted);
        Emit("OnRewardedAdHiddenEvent", firstId); // Duplicate close must not retire the next fullscreen ad.
        yield return null;
        Assert.IsFalse(second.IsCompleted);
        Emit("OnRewardedAdReceivedRewardEvent", secondId);
        Emit("OnRewardedAdHiddenEvent", secondId);
        yield return new WaitUntil(() => second.IsCompleted);
        Assert.AreEqual(2, rewards.Count);
        Assert.AreEqual(secondId, rewards[1].ShowId);
        MaxSdkCallbacks.Rewarded.OnAdDisplayedEvent -= onDisplayed;
        client.Dispose();
        // The official MAX Editor stub UI isn't a native ad; remove its generated acceptance objects.
        foreach (var canvas in Object.FindObjectsOfType<Canvas>())
        {
            if (canvas.gameObject.name.Contains("MaxSdk"))
            {
                Object.Destroy(canvas.gameObject);
            }
        }
        yield return null;
    }

    private static void Emit(string name, string id)
    {
        MaxSdkCallbacks.ForwardEvent("{\"name\":\"" + name + "\",\"adUnitId\":\"acceptance-rewarded\",\"placement\":\"" + id + "\",\"rewardLabel\":\"coin\",\"rewardAmount\":1}");
    }
}

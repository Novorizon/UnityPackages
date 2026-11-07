using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Game.Framework;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class GpuSchedulerAuditNativeTests
{
    [UnityTest]
    public IEnumerator ManualSchedulerChurnPreservesGenerationOrderAndCleansStoppedCrowds()
    {
        bool ownThread = !UnityThread.IsInitialized;
        if (ownThread)
        {
            UnityThread.Initialize();
        }
        Assert.That(GpuAnimatorManager.TryGetInstance(out _), Is.False);
        var manager = GpuAnimatorManager.Instance;
        manager.AutomaticUpdate = false;
        var root = new GameObject("Native GPU registration audit");
        try
        {
            var first = Player(root, "first");
            var second = Player(root, "second");
            var last = Player(root, "last");
            var started = new List<string>();
            manager.AnimationStarted += (player, clip) => started.Add(player.name);
            first.TryPlay("once", loop: false);
            second.TryPlay("loop");
            last.TryPlay("loop");
            first.AnimationCompleted += (player, clip) =>
            {
                second.Stop();
                second.TryPlay("loop");
            };
            manager.Tick(.4f);
            Assert.That(manager.Count, Is.EqualTo(2));
            Assert.That(second.CurrentFrame, Is.Zero, "An entry re-added from a callback waits for the next Tick.");
            Assert.That(last.CurrentFrame, Is.Zero);
            CollectionAssert.AreEqual(new[] { "first", "second", "last", "second" }, started);
            manager.Tick(.1f);
            Assert.That(second.CurrentFrame, Is.EqualTo(1));
            Assert.That(last.CurrentFrame, Is.EqualTo(1));
            manager.IsPaused = true;
            for (int i = 0; i < 1000; i++)
            {
                second.Stop();
                second.TryPlay("loop");
            }
            second.Stop();
            last.Stop();
            Assert.That(manager.Count, Is.Zero);
            var crowd = new GpuAnimator[256];
            for (int i = 0; i < crowd.Length; i++)
            {
                crowd[i] = Player(root, "crowd");
                crowd[i].TryPlay("loop");
            }
            Assert.That(manager.Count, Is.EqualTo(crowd.Length));
            foreach (GpuAnimator player in crowd)
            {
                player.Stop();
            }
            Assert.That(manager.Count, Is.Zero);
        }
        finally
        {
            Object.Destroy(root);
            GpuAnimatorManager.DestroyInstance();
            if (ownThread)
            {
                UnityThread.Reset();
            }
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator WarmManualSchedulerHasNoManagedAllocationInNativeUnity()
    {
        bool ownThread = !UnityThread.IsInitialized;
        if (ownThread)
        {
            UnityThread.Initialize();
        }
        Assert.That(GpuAnimatorManager.TryGetInstance(out _), Is.False);
        var manager = GpuAnimatorManager.Instance;
        manager.AutomaticUpdate = false;
        var root = new GameObject("Native GPU scheduler allocation audit");
        try
        {
            const int players = 128;
            const int ticks = 256;
            for (int i = 0; i < players; i++)
            {
                Player(root, "crowd").TryPlay("loop");
            }
            for (int i = 0; i < 64; i++)
            {
                manager.Tick(.1f);
            }
            _ = GC.GetAllocatedBytesForCurrentThread();
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < ticks; i++)
            {
                manager.Tick(.1f);
            }
            double milliseconds = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            TestContext.WriteLine("GPU_NATIVE_PERF players=" + players + " ticks=" + ticks + " elapsed_ms=" + milliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " allocated_bytes=" + allocated);
            Assert.That(manager.Count, Is.EqualTo(players));
            Assert.That(allocated, Is.Zero, "Warmed native MaterialPropertyBlock scheduler ticks must not allocate managed memory.");
        }
        finally
        {
            Object.Destroy(root);
            GpuAnimatorManager.DestroyInstance();
            if (ownThread)
            {
                UnityThread.Reset();
            }
        }
        yield return null;
    }

    private static GpuAnimator Player(GameObject root, string name)
    {
        var actor = new GameObject(name);
        actor.transform.SetParent(root.transform, false);
        var renderer = actor.AddComponent<MeshRenderer>();
        var player = actor.AddComponent<GpuAnimator>();
        var data = new AnimationData
        {
            totalFrame = 8,
            clips = new[]
            {
                new AnimationClipData("loop", 0, 3, true, 10),
                new AnimationClipData("once", 4, 7, false, 10)
            }
        };
        Assert.That(player.Initialize(renderer, data), Is.True);
        return player;
    }
}

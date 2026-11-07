using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class UiAudioInputAuditTests
{
    [Serializable]
    private sealed class PerformanceMetric
    {
        public string name;
        public string scope;
        public int samples;
        public int operationsPerSample;
        public double medianMicrosecondsPerOperation;
        public double p95MicrosecondsPerOperation;
        public long allocatedBytes;
        public double allocatedBytesPerOperation;
    }

    private sealed class Host
    {
        internal GameObject Root;
        internal ScrollRect Scroll;
        internal RectTransform Content;
        internal GameObject Prefab;
    }

    [UnityTest]
    public IEnumerator RefreshFromAnUnbindCallbackKeepsEveryNewGridBinding()
    {
        var host = CreateHost(true);
        try
        {
            var grid = host.Root.AddComponent<UIVirtualGrid>();
            grid.Configure(host.Scroll, host.Prefab, new Vector2(50, 20), 4);
            var bound = new HashSet<GameObject>();
            bool reentered = false;
            grid.SetItemCount(
                100,
                (cell, _) =>
                {
                    bound.Add(cell);
                    cell.GetComponent<Button>().interactable = true;
                },
                cell =>
                {
                    bound.Remove(cell);
                    cell.GetComponent<Button>().interactable = false;
                    if (!reentered)
                    {
                        reentered = true;
                        grid.RefreshItems();
                    }
                });
            grid.RefreshItems();
            yield return null;
            var active = host.Content.GetComponentsInChildren<Button>();
            Assert.That(active.Length, Is.GreaterThan(0));
            Assert.That(bound.Count, Is.EqualTo(active.Length));
            foreach (var cell in active)
            {
                Assert.That(bound.Contains(cell.gameObject), Is.True, "An old Unbind must not release the replacement row binding.");
                Assert.That(cell.interactable, Is.True);
            }
            grid.Clear();
            Assert.That(bound, Is.Empty, "Old and replacement bindings must each be released exactly once.");
        }
        finally
        {
            Object.Destroy(host.Root);
            Object.Destroy(host.Prefab);
        }
        yield return null;
    }

    [UnityTest]
    public IEnumerator FixedListAndGridScrollingStayBoundedWithoutManagedAllocations()
    {
        var listHost = CreateHost(false);
        var gridHost = CreateHost(false);
        try
        {
            var list = listHost.Root.AddComponent<UIVirtualList>();
            list.Configure(listHost.Scroll, listHost.Prefab, 20);
            list.SetItemCount(10000, BindEmptyCell);
            var grid = gridHost.Root.AddComponent<UIVirtualGrid>();
            grid.Configure(gridHost.Scroll, gridHost.Prefab, new Vector2(50, 20), 4);
            grid.SetItemCount(10000, BindEmptyCell);
            yield return null;
            int listCount = list.InstantiatedItemCount;
            int gridCount = grid.InstantiatedItemCount;
            var listMetric = Measure("ui.fixed-list.scroll-to", index => list.ScrollTo(1000 + index * 79 % 5000));
            var gridMetric = Measure("ui.fixed-grid.scroll-to", index => grid.ScrollTo(1000 + index * 79 % 5000));
            Assert.That(listMetric.allocatedBytes, Is.Zero);
            Assert.That(gridMetric.allocatedBytes, Is.Zero);
            Assert.That(list.InstantiatedItemCount, Is.EqualTo(listCount));
            Assert.That(grid.InstantiatedItemCount, Is.EqualTo(gridCount));
            Assert.That(listCount, Is.LessThan(32));
            Assert.That(gridCount, Is.LessThan(128));
        }
        finally
        {
            Object.Destroy(listHost.Root);
            Object.Destroy(listHost.Prefab);
            Object.Destroy(gridHost.Root);
            Object.Destroy(gridHost.Prefab);
        }
        yield return null;
    }

    private static PerformanceMetric Measure(string name, Action<int> operation)
    {
        const int samples = 31;
        const int operations = 128;
        for (int i = 0; i < 1024; i++)
        {
            operation(i);
        }
        var timings = new double[samples];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < samples; sample++)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < operations; i++)
            {
                operation(i);
            }
            long ticks = System.Diagnostics.Stopwatch.GetTimestamp() - started;
            timings[sample] = ticks * 1000000d / System.Diagnostics.Stopwatch.Frequency / operations;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Array.Sort(timings);
        var metric = new PerformanceMetric
        {
            name = name,
            scope = "Unity Editor Mono; 10000 items; empty RectTransform cells; no TMP/layout work",
            samples = samples,
            operationsPerSample = operations,
            medianMicrosecondsPerOperation = timings[samples / 2],
            p95MicrosecondsPerOperation = timings[(int)Math.Ceiling(samples * .95) - 1],
            allocatedBytes = allocated,
            allocatedBytesPerOperation = allocated / (double)(samples * operations)
        };
        TestContext.Out.WriteLine("NOVORIZON_PERF " + JsonUtility.ToJson(metric));
        return metric;
    }

    private static void BindEmptyCell(GameObject cell, int index)
    {
    }

    private static Host CreateHost(bool withButton)
    {
        var root = new GameObject("Native UI audit", typeof(RectTransform));
        var viewport = (RectTransform)new GameObject("Viewport", typeof(RectTransform)).transform;
        viewport.SetParent(root.transform, false);
        viewport.anchorMin = viewport.anchorMax = Vector2.zero;
        viewport.sizeDelta = new Vector2(200, 200);
        var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
        content.SetParent(viewport, false);
        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        var prefab = new GameObject("Audit Cell", typeof(RectTransform));
        if (withButton)
        {
            prefab.AddComponent<Button>();
        }
        prefab.SetActive(false);
        return new Host { Root = root, Scroll = scroll, Content = content, Prefab = prefab };
    }
}

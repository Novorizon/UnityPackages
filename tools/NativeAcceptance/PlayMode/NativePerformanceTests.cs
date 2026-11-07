using System;
using System.Collections;
using System.Diagnostics;
using Game.Framework;
using Game.Framework.Event;
using Game.Framework.Localization;
using Game.Framework.Timing;
using NUnit.Framework;
using UI;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>CPU operation probes in the real Editor Mono runtime; these do not measure complete frames.</summary>
public sealed class NativePerformanceTests
{
    private enum Topic
    {
        Updated
    }

    private readonly struct UpdateMessage : IMessage
    {
        internal readonly int Value;

        internal UpdateMessage(int value)
        {
            Value = value;
        }
    }

    [Serializable]
    private sealed class Metric
    {
        public string name;
        public string scope = "Unity Editor Mono synchronous CPU operations, prewarmed";
        public int samples;
        public int operationsPerSample;
        public double medianMicrosecondsPerOperation;
        public double p95MicrosecondsPerOperation;
        public long allocatedBytes;
        public double allocatedBytesPerOperation;
    }

    private bool ownsThreadHost;

    [SetUp]
    public void SetUp()
    {
        ownsThreadHost = !UnityThread.IsInitialized;
        if (ownsThreadHost)
        {
            UnityThread.Initialize();
        }
    }

    [TearDown]
    public void TearDown()
    {
        if (ownsThreadHost)
        {
            UnityThread.Reset();
        }
    }

    [Test]
    public void TypedEventPublishKeepsItsWarmedAllocationFreePathInMono()
    {
        var bus = new EventManager();
        int notifications = 0;
        using (bus.Subscribe(Topic.Updated, () => notifications++))
        {
            Metric result = Measure("EventManager typed Notify, one listener", () => bus.Notify(Topic.Updated), 1024);
            Assert.That(result.allocatedBytes, Is.Zero);
            Assert.That(notifications, Is.GreaterThan(30000));
        }
        bus.Dispose();
    }

    [Test]
    public void LocaleLookupKeepsItsWarmedAllocationFreePathInMono()
    {
        var rows = new string[10000][];
        for (int i = 0; i < rows.Length; i++)
        {
            rows[i] = new[] { "key." + i, "Value " + i };
        }
        var service = new LocalizationManager();
        try
        {
            service.Initialize(
                LocalizationCatalog.FromRows(
                    rows,
                    row => row[0],
                    new TranslationColumn<string[]>("en", row => row[1])),
                "en");
            string value = null;
            Metric result = Measure("Localization Get, 10000 keys, exact locale", () => value = service.Get("key.5000"), 1024);
            Assert.That(value, Is.EqualTo("Value 5000"));
            Assert.That(result.allocatedBytes, Is.Zero);
        }
        finally
        {
            service.Dispose();
        }
    }

    [Test]
    public void TypedStructEventPublishDoesNotBoxItsMessageInMono()
    {
        var bus = new EventManager();
        int notifications = 0;
        using (bus.Subscribe<Topic, UpdateMessage>(Topic.Updated, message => notifications += message.Value))
        {
            var message = new UpdateMessage(1);
            Metric result = Measure("EventManager typed struct Notify, one listener", () => bus.Notify(Topic.Updated, message), 1024);
            Assert.That(result.allocatedBytes, Is.Zero);
            Assert.That(notifications, Is.GreaterThan(30000));
        }
        bus.Dispose();
    }

    [Test]
    public void TimerUpdateWithTenThousandFutureEntriesKeepsItsWarmedAllocationFreePathInMono()
    {
        var timers = new TimerManager();
        try
        {
            Action callback = () => Assert.Fail("Future timers must not execute during this idle probe.");
            for (int i = 0; i < 10000; i++)
            {
                timers.Schedule(100000 + i, callback);
            }
            Metric result = Measure("Timer Update, 10000 pending, none due", () => timers.Update(0, 0), 1024);
            Assert.That(timers.PendingCount, Is.EqualTo(10000));
            Assert.That(result.allocatedBytes, Is.Zero);
        }
        finally
        {
            timers.Dispose();
        }
    }

    [UnityTest]
    public IEnumerator DynamicListScrollKeepsItsPoolBoundedWithNativeRectTransforms()
    {
        var root = new GameObject("Performance Canvas", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var listRoot = new GameObject("List", typeof(RectTransform), typeof(ScrollRect));
        listRoot.transform.SetParent(root.transform, false);
        var listRect = (RectTransform)listRoot.transform;
        listRect.anchorMin = listRect.anchorMax = new Vector2(.5f, .5f);
        listRect.sizeDelta = new Vector2(240, 200);
        var viewport = (RectTransform)new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).transform;
        viewport.SetParent(listRoot.transform, false);
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.sizeDelta = Vector2.zero;
        var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
        content.SetParent(viewport, false);
        var prefab = new GameObject("Performance row", typeof(RectTransform));
        prefab.SetActive(false);
        var scroll = listRoot.GetComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var list = listRoot.AddComponent<UIVirtualDynamicList>();
        try
        {
            Canvas.ForceUpdateCanvases();
            list.Configure(scroll, prefab, 20);
            var keys = new string[10000];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = i.ToString();
            }
            int bindings = 0;
            list.SetItems(keys, (_, _) => bindings++);
            yield return null;
            int next = 0;
            Metric result = Measure(
                "Dynamic list ScrollTo, 10000 rows, native empty cells",
                () => list.ScrollTo(1000 + next++ % 100),
                64);
            Assert.That(bindings, Is.GreaterThan(1000));
            Assert.That(list.InstantiatedItemCount, Is.InRange(10, 13));
            Assert.That(result.allocatedBytes, Is.Zero);
        }
        finally
        {
            Object.Destroy(root);
            Object.Destroy(prefab);
        }
        yield return null;
    }

    private static Metric Measure(string name, Action operation, int operationsPerSample)
    {
        const int samples = 31;
        for (int i = 0; i < 2048; i++)
        {
            operation();
        }
        var timings = new double[samples];
        _ = GC.GetAllocatedBytesForCurrentThread();
        _ = Stopwatch.GetTimestamp();
        long allocated = 0;
        for (int sample = 0; sample < samples; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < operationsPerSample; i++)
            {
                operation();
            }
            long elapsed = Stopwatch.GetTimestamp() - started;
            allocated += GC.GetAllocatedBytesForCurrentThread() - before;
            timings[sample] = elapsed * 1000000d / Stopwatch.Frequency / operationsPerSample;
        }
        Array.Sort(timings);
        var result = new Metric
        {
            name = name,
            samples = samples,
            operationsPerSample = operationsPerSample,
            medianMicrosecondsPerOperation = timings[samples / 2],
            p95MicrosecondsPerOperation = timings[(int)Math.Ceiling(samples * .95) - 1],
            allocatedBytes = allocated,
            allocatedBytesPerOperation = allocated / (double)(samples * operationsPerSample)
        };
        TestContext.Out.WriteLine("NOVORIZON_PERF " + JsonUtility.ToJson(result));
        return result;
    }
}

using UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;

public sealed class UIVirtualDynamicListTests : IDisposable
{
    public UIVirtualDynamicListTests() => Object.ResetTestState();
    public void Dispose() => Object.ResetTestState();

    static (UIVirtualDynamicList List, ScrollRect Scroll, RectTransform Viewport, RectTransform Content, GameObject Prefab) Create(
        float height = 20,
        int limit = 32,
        Func<GameObject, float>? measure = null)
    {
        var root = new GameObject("Dynamic list", typeof(RectTransform));
        var viewport = (RectTransform)new GameObject("Viewport", typeof(RectTransform)).transform;
        viewport.SetParent(root.transform, false);
        viewport.anchorMin = viewport.anchorMax = Vector2.zero;
        viewport.sizeDelta = new Vector2(200, 200);
        var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
        content.SetParent(viewport, false);
        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        var prefab = new GameObject("Cell", typeof(RectTransform));
        prefab.SetActive(false);
        prefab.AddComponent<Probe>();
        var list = root.AddComponent<UIVirtualDynamicList>();
        list.Configure(scroll, prefab, height, maxVisibleItems: limit, measureHeight: measure);
        return (list, scroll, viewport, content, prefab);
    }

    static string[] Keys(int count) => Enumerable.Range(0, count).Select(i => i.ToString()).ToArray();
    static void Bind(GameObject cell, int index) => cell.GetComponent<Probe>().Index = index;
    static Probe[] Visible(RectTransform content) => content.gameObject.GetComponentsInChildren<Probe>()
        .Where(p => p.gameObject.activeInHierarchy).OrderBy(p => p.Index).ToArray();

    [Fact]
    public void TenThousandRowsUseABoundedPoolAndSeekWithMeasuredHeights()
    {
        var (list, _, _, content, _) = Create();
        list.SetItems(Keys(10_000), Bind);
        Assert.InRange(list.InstantiatedItemCount, 10, 12);
        Assert.Equal(200_000, content.sizeDelta.y);
        list.ReportHeight("0", 100, list.DataVersion);
        list.ReportHeight("1", 50, list.DataVersion);
        Assert.Equal(200_110, content.sizeDelta.y);
        list.ScrollTo(5000);
        Assert.Equal(100_110, content.anchoredPosition.y);
        Assert.Equal(5000, list.FirstVisibleIndex);
        Assert.Equal(5009, list.LastVisibleIndex);
        Assert.All(Visible(content), p => Assert.Equal(p.Index * 20 + 110, -((RectTransform)p.transform).anchoredPosition.y));
        list.ScrollTo(9999);
        Assert.Equal(9999, list.LastVisibleIndex);
        Assert.Equal(199_910, content.anchoredPosition.y);
        Assert.InRange(list.InstantiatedItemCount, 10, 12);
    }

    [Fact]
    public void SmallValidHeightsDoNotLoseRowsOrIgnoreMeasuredCorrections()
    {
        const float height = 1f / 1024;
        var (list, _, viewport, content, _) = Create(height: height);
        viewport.sizeDelta = new Vector2(200, height * 4);
        list.SetItems(Keys(100), Bind);
        Assert.Equal(3, list.LastVisibleIndex);
        list.ReportHeight("0", height * 2, list.DataVersion);
        Assert.Equal(height * 101, content.sizeDelta.y);
        Assert.Equal(2, list.LastVisibleIndex);
    }

    [Fact]
    public void HeightChangesAboveViewportPreserveTheVisibleKeyAndPixelOffset()
    {
        var (list, scroll, _, content, _) = Create();
        list.SetItems(Keys(1000), Bind);
        content.anchoredPosition = new Vector2(0, 1007);
        scroll.onValueChanged.Invoke(Vector2.zero);
        list.ReportHeight("10", 120, list.DataVersion);
        Assert.Equal(1107, content.anchoredPosition.y);
        Assert.Equal(50, list.FirstVisibleIndex);
        list.ReportHeight("50", 5, list.DataVersion);
        Assert.Equal(50, list.FirstVisibleIndex);
        Assert.InRange(content.anchoredPosition.y, 1104.98f, 1105);
    }

    [Fact]
    public void KeyedInsertionAndRemovalRetainAnchorAndOptionalHeightCache()
    {
        var (list, _, _, content, _) = Create();
        var keys = Keys(1000).ToList();
        list.SetItems(keys, Bind);
        list.ReportHeight("10", 80, list.DataVersion);
        list.ScrollTo(50);
        keys.Insert(0, "inserted");
        list.SetItems(keys, Bind, resetPosition: false, retainHeights: true);
        Assert.Equal(51, list.FirstVisibleIndex);
        Assert.Equal(1080, content.anchoredPosition.y);
        keys.RemoveRange(0, 11);
        list.SetItems(keys, Bind, resetPosition: false, retainHeights: true);
        Assert.Equal(40, list.FirstVisibleIndex);
        Assert.Equal(860, content.anchoredPosition.y);
    }

    [Fact]
    public void StaleAsyncReportsAndInvalidKeysCannotChangeNewData()
    {
        var (list, _, _, content, _) = Create();
        list.SetItems(Keys(100), Bind);
        int oldVersion = list.DataVersion;
        list.SetItems(Keys(100), Bind);
        Assert.False(list.ReportHeight("0", 500, oldVersion));
        Assert.False(list.ReportHeight("missing", 500, list.DataVersion));
        Assert.Equal(2000, content.sizeDelta.y);
        Assert.Throws<ArgumentOutOfRangeException>(() => list.ReportHeight("0", float.NaN, list.DataVersion));
        Assert.Throws<ArgumentException>(() => list.SetItems(new[] { "same", "same" }, Bind));
        Assert.Equal(100, list.ItemCount);
        Assert.Equal(2000, content.sizeDelta.y);
    }

    [Fact]
    public void WidthAndLocalizationInvalidationResetHeightsAndRebindWithANewVersion()
    {
        var (list, _, viewport, content, _) = Create();
        list.SetItems(Keys(100), Bind);
        Object.AdvanceFrameForTest();
        list.ReportHeight("0", 80, list.DataVersion);
        list.ScrollTo(20);
        int version = list.DataVersion;
        viewport.sizeDelta = new Vector2(300, 200);
        Object.AdvanceFrameForTest();
        Assert.NotEqual(version, list.DataVersion);
        Assert.Equal(400, content.anchoredPosition.y);
        Assert.Equal(2000, content.sizeDelta.y);
        list.ReportHeight("0", 70, list.DataVersion);
        list.InvalidateHeights();
        Assert.Equal(400, content.anchoredPosition.y);
        Assert.Equal(20, list.FirstVisibleIndex);
    }

    [Fact]
    public void OptionalMeasurementConvergesWithoutForcingLayoutOrMeasuringHiddenRows()
    {
        int measured = 0;
        var (list, _, _, content, _) = Create(measure: cell =>
        {
            measured++;
            return cell.GetComponent<Probe>().Index == 0 ? 80 : 20;
        });
        list.SetItems(Keys(100), Bind);
        Object.AdvanceFrameForTest();
        Assert.Equal(2060, content.sizeDelta.y);
        Object.AdvanceFrameForTest();
        Assert.InRange(measured, 1, list.InstantiatedItemCount * 2);
    }

    [Fact]
    public void BindingCanReplaceDataOrReportHeightWithoutOldRenderWinning()
    {
        var (list, _, _, content, _) = Create();
        bool replaced = false;
        list.SetItems(Keys(100), (_, _) =>
        {
            if (!replaced)
            {
                replaced = true;
                list.SetItems(new[] { "new" }, Bind);
            }
        });
        Object.AdvanceFrameForTest();
        Assert.Equal(1, list.ItemCount);
        Assert.Single(Visible(content));
        bool reported = false;
        list.SetItems(Keys(100), (cell, index) =>
        {
            Bind(cell, index);
            if (!reported)
            {
                reported = true;
                list.ReportHeight(index.ToString(), 80, list.DataVersion);
            }
        });
        Object.AdvanceFrameForTest();
        Assert.Equal(2060, content.sizeDelta.y);
        Assert.Equal(0, list.FirstVisibleIndex);
        Assert.Equal(6, list.LastVisibleIndex);
    }

    [Fact]
    public void HeightReportInsideForcedRefreshDoesNotLeaveOtherRowsWithOldBindings()
    {
        var (list, _, _, content, _) = Create();
        bool refresh = false;
        bool reported = false;
        list.SetItems(Keys(100), (cell, index) =>
        {
            Bind(cell, index);
            cell.GetComponent<Probe>().Version = list.DataVersion;
            if (refresh && !reported)
            {
                reported = true;
                list.ReportHeight(index.ToString(), 60, list.DataVersion);
            }
        });
        refresh = true;
        list.RefreshItems();
        Object.AdvanceFrameForTest();
        Assert.All(Visible(content), row => Assert.Equal(list.DataVersion, row.Version));
    }

    [Fact]
    public void BindingFailureHidesTheFailedCellAndRecoversOnRefresh()
    {
        var (list, _, _, content, _) = Create();
        bool fail = true;
        list.SetItems(new[] { "a", "b", "c" }, (cell, index) =>
        {
            if (fail && index == 1)
            {
                throw new InvalidOperationException("test bind failure");
            }
            Bind(cell, index);
        });
        Assert.Equal(new[] { 0, 2 }, Visible(content).Select(p => p.Index));
        Assert.Single(Debug.Exceptions);
        fail = false;
        list.RefreshItems();
        Assert.Equal(new[] { 0, 1, 2 }, Visible(content).Select(p => p.Index));
        Object.Destroy(Visible(content)[1].gameObject);
        list.RefreshItems();
        Assert.Equal(new[] { 0, 1, 2 }, Visible(content).Select(p => p.Index));
    }

    [Fact]
    public void ViewportHeightResizeClampsOffsetAndClearReleasesCellsBeforeNativeDestruction()
    {
        var (list, _, viewport, content, prefab) = Create();
        list.SetItems(Keys(100), Bind);
        list.ScrollTo(99);
        viewport.sizeDelta = new Vector2(200, 400);
        Object.AdvanceFrameForTest();
        Assert.Equal(1600, content.anchoredPosition.y);
        Assert.Equal(80, list.FirstVisibleIndex);
        Assert.Equal(99, list.LastVisibleIndex);
        Object.DeferGameObjectDestructionForTest = true;
        list.Clear();
        Assert.Equal(0, list.InstantiatedItemCount);
        Assert.Empty(Visible(content));
        Assert.False(prefab.DestroyedForTest);
    }

    [Fact]
    public void MeasurementCanReplaceTheDataAndThrowWithoutReadingOldIndices()
    {
        UIVirtualDynamicList? target = null;
        bool replaced = false;
        var (list, _, _, content, _) = Create(measure: _ =>
        {
            if (!replaced)
            {
                replaced = true;
                target!.SetItems(new[] { "new" }, Bind);
                throw new InvalidOperationException("measurement replaced its data");
            }
            return 40;
        });
        target = list;
        list.SetItems(Keys(100), Bind);
        Object.AdvanceFrameForTest();
        Object.AdvanceFrameForTest();
        Assert.Equal(1, list.ItemCount);
        Assert.Single(Visible(content));
        Assert.Equal(40, content.sizeDelta.y);
    }

    [Fact]
    public void CleanupCancelsBindingsAndReentryMayAssignAnotherSource()
    {
        var (list, scroll, _, content, prefab) = Create();
        int unbound = 0;
        list.SetItems(Keys(100), Bind, _ => unbound++);
        list.Clear();
        Assert.True(unbound > 0);
        Assert.Equal(0, list.InstantiatedItemCount);
        Assert.Empty(Visible(content));
        list.SetItems(Keys(100), Bind, _ => list.SetItems(new[] { "replacement" }, Bind));
        list.Clear();
        Assert.Equal(1, list.ItemCount);
        Object.Destroy(list);
        Assert.Equal(0, scroll.onValueChanged.ListenerCountForTest);
        Assert.Empty(Visible(content));
        Assert.False(prefab.DestroyedForTest);
    }

    [Fact]
    public void ExcessiveVisibleWindowIsRejectedBeforeAllocatingCells()
    {
        var (list, _, _, _, _) = Create(height: 1);
        Assert.Throws<InvalidOperationException>(() => list.SetItems(Keys(10000), Bind));
        Assert.Equal(0, list.InstantiatedItemCount);
    }

    [Fact]
    public void RandomHeightUpdatesAndSeeksMatchALinearReference()
    {
        var (list, _, _, content, _) = Create(height: 50);
        var keys = Keys(1000);
        var heights = Enumerable.Repeat(50f, keys.Length).ToArray();
        var random = new System.Random(147);
        list.SetItems(keys, (_, _) => { });
        for (int operation = 0; operation < 2000; operation++)
        {
            int changed = random.Next(heights.Length);
            heights[changed] = random.Next(10, 200);
            list.ReportHeight(keys[changed], heights[changed], list.DataVersion);
            int target = random.Next(heights.Length);
            list.ScrollTo(target);
            float expected = Math.Min(heights.Take(target).Sum(), heights.Sum() - 200);
            Assert.Equal(expected, content.anchoredPosition.y);
            Assert.Equal(heights.Sum(), content.sizeDelta.y);
            float prefix = 0;
            int first = 0;
            while (prefix + heights[first] <= expected && first < heights.Length - 1)
            {
                prefix += heights[first++];
            }
            Assert.Equal(first, list.FirstVisibleIndex);
        }
    }

    [Fact]
    public void RepeatedScrollingAllocatesNothingAfterBoundedPoolWarmup()
    {
        var (list, _, _, _, _) = Create();
        list.SetItems(Keys(10000), (_, _) => { });
        for (int i = 0; i < 1000; i++)
        {
            list.ScrollTo(1000 + i % 100);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            list.ScrollTo(1000 + i % 100);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.InRange(list.InstantiatedItemCount, 10, 12);
    }

    public sealed class Probe : MonoBehaviour
    {
        public int Index;
        public int Version;
    }
}

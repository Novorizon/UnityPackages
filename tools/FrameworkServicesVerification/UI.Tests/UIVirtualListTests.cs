using UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
// Production virtualization driven by a finite ScrollRect/geometry contract, not native layout/input.
public sealed class UIVirtualListTests : IDisposable
{
    public UIVirtualListTests() => Object.ResetTestState();
    public void Dispose() => Object.ResetTestState();
    static (UIVirtualList List, ScrollRect Scroll, RectTransform Viewport, RectTransform Content, GameObject Prefab) Create(bool horizontal = false, float size = 20, int overscan = 1, float spacing = 0, int limit = 512)
    {
        var root = new GameObject("Virtual list", typeof(RectTransform));
        var viewport = (RectTransform)new GameObject("Viewport", typeof(RectTransform)).transform;
        viewport.SetParent(root.transform, false);
        viewport.anchorMin = viewport.anchorMax = Vector2.zero;
        viewport.sizeDelta = new Vector2(200, 200);
        var content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
        content.SetParent(viewport, false);
        var scroll = root.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        var prefab = new GameObject("Cell prefab", typeof(RectTransform));
        prefab.SetActive(false);
        prefab.AddComponent<CellProbe>();
        var list = root.AddComponent<UIVirtualList>();
        list.Configure(scroll, prefab, size, horizontal, overscan, spacing, limit);
        return (list, scroll, viewport, content, prefab);
    }

    static void Bind(GameObject cell, int index) => cell.GetComponent<CellProbe>().Index = index;
    static CellProbe[] ActiveCells(RectTransform content) => content.gameObject.GetComponentsInChildren<CellProbe>().Where(cell => cell.gameObject.activeInHierarchy).ToArray();
    [Fact]
    public void TenThousandItemsCreateOnlyTheVisibleWindowAndScrollToBothBoundaries()
    {
        var(list, scroll, _, content, prefab) = Create();
        list.SetItemCount(10_000, Bind);
        Assert.Equal(10_000, list.ItemCount);
        Assert.Equal(13, list.InstantiatedItemCount);
        Assert.Equal(0, list.FirstVisibleIndex);
        Assert.Equal(9, list.LastVisibleIndex);
        Assert.Equal(200_000, content.sizeDelta.y);
        Assert.Equal(Enumerable.Range(0, 13), ActiveCells(content).Select(cell => cell.Index).OrderBy(index => index));
        list.ScrollTo(9_999);
        Assert.Equal(13, list.InstantiatedItemCount);
        Assert.Equal(199_800, content.anchoredPosition.y);
        Assert.Equal(9_990, list.FirstVisibleIndex);
        Assert.Equal(9_999, list.LastVisibleIndex);
        Assert.Equal(Enumerable.Range(9_987, 13), ActiveCells(content).Select(cell => cell.Index).OrderBy(index => index));
        list.ScrollTo(0);
        Assert.Equal(0, list.FirstVisibleIndex);
        Assert.Equal(0, content.anchoredPosition.y);
        Assert.True(scroll.vertical);
        Assert.False(scroll.horizontal);
        Assert.False(prefab.DestroyedForTest);
    }

    [Fact]
    public void ScrollingRecyclesOnlyEnteringCellsWithoutRepeatingActivationCallbacks()
    {
        var(list, scroll, _, content, _) = Create();
        int bindings = 0, unbindings = 0;
        list.SetItemCount(1000, (cell, index) =>
        {
            bindings++;
            Bind(cell, index);
        }, _ => unbindings++);
        Assert.Equal(13, bindings);
        content.anchoredPosition = new Vector2(0, 20);
        scroll.onValueChanged.Invoke(Vector2.zero);
        Assert.Equal(13, bindings); // The overscan already owns this next visible row.
        content.anchoredPosition = new Vector2(0, 40);
        scroll.onValueChanged.Invoke(Vector2.zero);
        Assert.Equal(14, bindings);
        Assert.Equal(1, unbindings);
        Assert.All(ActiveCells(content), cell =>
        {
            Assert.Equal(1, cell.Enables);
            Assert.Equal(0, cell.Disables);
        });
    }

    [Fact]
    public void RefreshReplacesAnExternallyDestroyedCellAndRestoresTheWholeWindow()
    {
        var(list, _, _, content, _) = Create();
        list.SetItemCount(10_000, Bind);
        var old = ActiveCells(content).Single(cell => cell.Index == 5);
        Object.Destroy(old.gameObject);
        Assert.Equal(12, ActiveCells(content).Length);
        list.RefreshItems();
        Assert.Equal(13, list.InstantiatedItemCount);
        Assert.Equal(Enumerable.Range(0, 13), ActiveCells(content).Select(cell => cell.Index).OrderBy(index => index));
        list.ScrollTo(9_999);
        Assert.Equal(Enumerable.Range(9_987, 13), ActiveCells(content).Select(cell => cell.Index).OrderBy(index => index));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ViewportResizeAdjustsPoolAndPositionsForVerticalAndHorizontalLists(bool horizontal)
    {
        var(list, _, viewport, content, _) = Create(horizontal);
        list.SetItemCount(10_000, Bind);
        list.ScrollTo(9999);
        viewport.sizeDelta = horizontal ? new Vector2(100, 200) : new Vector2(200, 100);
        Object.AdvanceFrameForTest();
        Assert.Equal(8, list.InstantiatedItemCount);
        Assert.Equal(8, ActiveCells(content).Length);
        viewport.sizeDelta = horizontal ? new Vector2(400, 200) : new Vector2(200, 400);
        Object.AdvanceFrameForTest();
        Assert.Equal(23, list.InstantiatedItemCount);
        Assert.Equal(23, ActiveCells(content).Length);
        Assert.All(ActiveCells(content), cell =>
        {
            var rect = (RectTransform)cell.transform;
            Assert.Equal(cell.Index * 20, horizontal ? rect.anchoredPosition.x : -rect.anchoredPosition.y);
        });
    }

    [Fact]
    public void SetDataRebindsRefreshReflectsChangesAndClearReleasesRowsAndDataCallbacks()
    {
        var(list, _, _, content, prefab) = Create();
        var first = Enumerable.Range(0, 100).ToList();
        int unbindings = 0;
        list.SetData(first, (cell, value, _) => cell.GetComponent<CellProbe>().Value = value, _ => unbindings++);
        Assert.Equal(13, list.InstantiatedItemCount);
        first[0] = 42;
        list.RefreshItems();
        Assert.Contains(ActiveCells(content), cell => cell.Value == 42);
        list.SetData(new[] { 7, 8 }, (cell, value, index) =>
        {
            var probe = cell.GetComponent<CellProbe>();
            probe.Value = value;
            probe.Index = index;
        });
        Assert.True(unbindings >= 13);
        Assert.Equal(2, list.InstantiatedItemCount);
        Assert.Equal(new[] { 7, 8 }, ActiveCells(content).OrderBy(cell => cell.Index).Select(cell => cell.Value));
        list.Clear();
        Assert.Equal(0, list.ItemCount);
        Assert.Equal(0, list.InstantiatedItemCount);
        Assert.Empty(ActiveCells(content));
        Assert.Equal(0, content.sizeDelta.y);
        Assert.False(prefab.DestroyedForTest);
        list.SetItemCount(1, Bind);
        Assert.Single(ActiveCells(content));
    }

    [Fact]
    public void DisableAndDestroyUnsubscribeAndNeverDestroyTheProjectPrefab()
    {
        var(list, scroll, _, content, prefab) = Create();
        list.SetItemCount(1000, Bind);
        Assert.Equal(1, scroll.onValueChanged.ListenerCountForTest);
        list.enabled = false;
        Assert.Equal(0, scroll.onValueChanged.ListenerCountForTest);
        list.enabled = true;
        Assert.Equal(1, scroll.onValueChanged.ListenerCountForTest);
        Object.Destroy(list);
        Assert.Equal(0, scroll.onValueChanged.ListenerCountForTest);
        Assert.Empty(ActiveCells(content));
        Assert.False(prefab.DestroyedForTest);
    }

    [Fact]
    public void LargeVisibleWindowFailsExplicitlyInsteadOfAllocatingEveryDataItem()
    {
        var(list, _, _, _, _) = Create(size: 1, limit: 32);
        Assert.Throws<InvalidOperationException>(() => list.SetItemCount(10_000, Bind));
        Assert.Equal(0, list.InstantiatedItemCount);
    }

    [Fact]
    public void BindingMayReplaceTheSourceWithoutTheOldRenderOverwritingTheNewData()
    {
        var(list, _, _, content, _) = Create();
        bool replaced = false;
        list.SetItemCount(1000, (_, _) =>
        {
            if (replaced)
            {
                return;
            }

            replaced = true;
            list.SetItemCount(2, Bind);
        });
        Object.AdvanceFrameForTest();
        Assert.Equal(2, list.ItemCount);
        Assert.Equal(2, list.InstantiatedItemCount);
        Assert.Equal(new[] { 0, 1 }, ActiveCells(content).Select(cell => cell.Index).OrderBy(index => index));
    }

    [Fact]
    public void ClearDoesNotOverwriteDataAndScrollAssignedByACellDisableCallback()
    {
        var(list, _, _, content, _) = Create();
        list.SetItemCount(1000, Bind);
        ActiveCells(content).Single(cell => cell.Index == 12).DisableAction = () =>
        {
            list.SetItemCount(2000, Bind);
            list.ScrollTo(1500);
        };
        list.Clear();
        Assert.Equal(2000, list.ItemCount);
        Assert.Equal(13, list.InstantiatedItemCount);
        Assert.Equal(1500, list.FirstVisibleIndex);
        Assert.Equal(1509, list.LastVisibleIndex);
        Assert.Equal(30_000, content.anchoredPosition.y);
        Assert.Equal(Enumerable.Range(1499, 13), ActiveCells(content).Select(cell => cell.Index).OrderBy(index => index));
    }

    [Fact]
    public void BindingFailureHidesTheFailedCellAndStillBindsTheRemainingRows()
    {
        var(list, _, _, content, _) = Create();
        list.SetItemCount(3, (cell, index) =>
        {
            if (index == 1)
            {
                throw new InvalidOperationException("Invalid row data.");
            }

            Bind(cell, index);
        });
        Assert.Equal(3, list.InstantiatedItemCount);
        Assert.Single(Debug.Exceptions);
        Assert.Equal(new[] { 0, 2 }, ActiveCells(content).Select(cell => cell.Index).OrderBy(index => index));
    }

    [Fact]
    public void RecycledScrollingDoesNotAllocateInTheFrameworkAfterWarmup()
    {
        var(list, _, _, _, _) = Create();
        int bindings = 0;
        list.SetItemCount(10_000, (_, _) => bindings++);
        for (int i = 0; i < 10_000; i++)
        {
            list.ScrollTo(1000 + i % 100);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            list.ScrollTo(1000 + i % 100);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(bindings > 10_000);
        Assert.Equal(13, list.InstantiatedItemCount);
    }

    public sealed class CellProbe : MonoBehaviour
    {
        public int Index = -1;
        public int Value;
        public int Enables, Disables;
        public Action? DisableAction = null;
        void OnEnable()
        {
            Enables++;
        }

        void OnDisable()
        {
            Disables++;
            var action = DisableAction;
            DisableAction = null;
            action?.Invoke();
        }
    }
}
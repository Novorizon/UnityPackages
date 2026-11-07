using UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;

public sealed class UIVirtualGridTests : IDisposable
{
    public UIVirtualGridTests() => Object.ResetTestState();
    public void Dispose() => Object.ResetTestState();

    static (UIVirtualGrid Grid, ScrollRect Scroll, RectTransform Content, GameObject Prefab) Create(int columns = 4, int limit = 128)
    {
        var root = new GameObject("Grid", typeof(RectTransform));
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
        prefab.AddComponent<UIVirtualListTests.CellProbe>();
        var grid = root.AddComponent<UIVirtualGrid>();
        grid.Configure(scroll, prefab, new Vector2(50, 20), columns, maximumVisibleCells: limit);
        return (grid, scroll, content, prefab);
    }

    static void Bind(GameObject cell, int index) => cell.GetComponent<UIVirtualListTests.CellProbe>().Index = index;
    static UIVirtualListTests.CellProbe[] Active(RectTransform content) => content.gameObject.GetComponentsInChildren<UIVirtualListTests.CellProbe>().Where(cell => cell.gameObject.activeInHierarchy).ToArray();

    [Fact]
    public void TenThousandCellsReuseTheRowWindowAndHandleThePartialLastRow()
    {
        var (grid, _, content, _) = Create();
        grid.SetItemCount(10_003, Bind);
        Assert.Equal(52, grid.InstantiatedItemCount);
        Assert.Equal(0, grid.FirstVisibleIndex);
        Assert.Equal(39, grid.LastVisibleIndex);
        Assert.Equal(Enumerable.Range(0, 52), Active(content).Select(cell => cell.Index).OrderBy(x => x));
        grid.ScrollTo(10_002);
        Assert.Equal(10_002, grid.LastVisibleIndex);
        Assert.Equal(52, grid.InstantiatedItemCount);
        Assert.Equal(51, Active(content).Length);
        Assert.DoesNotContain(Active(content), cell => cell.Index > 10_002);
        Assert.All(Active(content), cell => Assert.Equal((cell.Index % 4) * 50, ((RectTransform)cell.transform).anchoredPosition.x));
    }

    [Fact]
    public void RefreshClearAndDestroyReleaseBindingsAndPreserveTheProjectPrefab()
    {
        var (grid, scroll, content, prefab) = Create();
        int unbound = 0;
        grid.SetItemCount(10_000, Bind, _ => unbound++);
        grid.RefreshItems();
        Assert.Equal(52, unbound);
        grid.Clear();
        Assert.Equal(104, unbound);
        Assert.Equal(0, grid.ItemCount);
        Assert.Empty(Active(content));
        grid.SetItemCount(3, Bind);
        grid.enabled = false;
        Assert.Equal(0, scroll.onValueChanged.ListenerCountForTest);
        grid.enabled = true;
        Assert.Equal(1, scroll.onValueChanged.ListenerCountForTest);
        Object.Destroy(grid);
        Assert.Equal(0, scroll.onValueChanged.ListenerCountForTest);
        Assert.False(prefab.DestroyedForTest);
    }

    [Fact]
    public void BindingCanReplaceTheGridDataWithoutPublishingStaleIndices()
    {
        var (grid, _, content, _) = Create();
        bool changed = false;
        grid.SetItemCount(10_000, (_, _) =>
        {
            if (changed)
            {
                return;
            }
            changed = true;
            grid.SetItemCount(2, Bind);
        });
        Object.AdvanceFrameForTest();
        Assert.Equal(2, grid.ItemCount);
        Assert.Equal(new[] { 0, 1 }, Active(content).Select(cell => cell.Index).OrderBy(x => x));
    }

    [Fact]
    public void VisibleCellBudgetIsEnforcedBeforeInstantiatingTheWindow()
    {
        var (grid, _, _, _) = Create(limit: 16);
        Assert.Throws<InvalidOperationException>(() => grid.SetItemCount(10_000, Bind));
        Assert.Equal(0, grid.InstantiatedItemCount);
    }

    [Fact]
    public void RetirementReleasesRowOwnershipBeforeDeferredNativeDestruction()
    {
        var (grid, _, _, _) = Create();
        grid.SetItemCount(10_000, Bind);
        Object.DeferGameObjectDestructionForTest = true;
        grid.Clear();
        Assert.Equal(0, grid.InstantiatedItemCount);
        grid.SetItemCount(3, Bind);
        Assert.Equal(4, grid.InstantiatedItemCount);
        Object.FlushDestroyQueueForTest();
        Assert.Equal(4, grid.InstantiatedItemCount);
        Object.DeferGameObjectDestructionForTest = false;
    }

    [Fact]
    public void WarmedScrollingAllocatesNoManagedMemoryInTheGrid()
    {
        var (grid, _, _, _) = Create();
        grid.SetItemCount(10_000, (_, _) => { });
        for (int i = 0; i < 256; i++)
        {
            grid.ScrollTo(1000 + i % 100);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1024; i++)
        {
            grid.ScrollTo(1000 + i % 100);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}

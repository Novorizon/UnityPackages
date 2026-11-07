#nullable disable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UI
{
    /// <summary>Vertical, fixed-column grid. Reuses UIVirtualList's bounded row window.</summary>
    public sealed class UIVirtualGrid : MonoBehaviour
    {
        private UIVirtualList rows;
        private GameObject rowPrefab, driver, cellPrefab;
        private Vector2 size, spacing;
        private int columns, count, revision;
        private bool destroyed;
        private Action<GameObject, int> bind;
        private Action<GameObject> unbind;
        private readonly HashSet<UIVirtualGridRow> liveRows = new HashSet<UIVirtualGridRow>();
        private readonly Dictionary<GameObject, UIVirtualGridRow> rowLookup = new Dictionary<GameObject, UIVirtualGridRow>();

        public int ItemCount => count;
        public int FirstVisibleIndex => rows == null || rows.FirstVisibleIndex < 0 ? -1 : rows.FirstVisibleIndex * columns;
        public int LastVisibleIndex => rows == null || rows.LastVisibleIndex < 0 ? -1 : (int)Math.Min(count - 1L, (rows.LastVisibleIndex + 1L) * columns - 1);
        public int InstantiatedItemCount
        {
            get
            {
                int total = 0;
                foreach (var row in liveRows)
                {
                    if (row != null)
                    {
                        total += row.Cells.Length;
                    }
                }

                return total;
            }
        }

        public void Configure(
            ScrollRect scrollRect,
            GameObject itemPrefab,
            Vector2 cellSize,
            int columnCount,
            Vector2 cellSpacing = default,
            int overscanRows = 1,
            int maximumVisibleCells = 512)
        {
            ThrowIfDestroyed();
            if (scrollRect == null || itemPrefab == null || !(itemPrefab.transform is RectTransform))
            {
                throw new ArgumentException("A ScrollRect and RectTransform cell prefab are required.");
            }

            if (!Finite(cellSize.x) || !Finite(cellSize.y) || cellSize.x <= 0 || cellSize.y <= 0 ||
                !Finite(cellSpacing.x) || !Finite(cellSpacing.y) || cellSpacing.x < 0 || cellSpacing.y < 0 ||
                !Finite(cellSize.x + cellSpacing.x) || !Finite(cellSize.y + cellSpacing.y) ||
                columnCount < 1 || columnCount > maximumVisibleCells ||
                (double)columnCount * (cellSize.x + cellSpacing.x) > float.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSize));
            }

            int operation = ++revision;
            Clear();
            if (destroyed || revision != operation + 1)
            {
                return;
            }

            cellPrefab = itemPrefab;
            columns = columnCount;
            size = cellSize;
            spacing = cellSpacing;
            if (rows == null)
            {
                driver = new GameObject("Virtual Grid Driver");
                driver.transform.SetParent(transform, false);
                rows = driver.AddComponent<UIVirtualList>();
                rows.CellRetiring += RetireRow;
                rowPrefab = new GameObject("Virtual Grid Row", typeof(RectTransform));
                rowPrefab.SetActive(false);
                rowPrefab.transform.SetParent(driver.transform, false);
            }

            rows.Configure(scrollRect, rowPrefab, size.y, false, overscanRows, spacing.y, maximumVisibleCells / columns);
            rows.enabled = isActiveAndEnabled;
        }

        public void SetItemCount(int itemCount, Action<GameObject, int> bindItem, Action<GameObject> unbindItem = null)
        {
            RequireConfigured();
            if (itemCount < 0 || bindItem == null)
            {
                throw new ArgumentException("A nonnegative count and binding callback are required.");
            }

            int operation = ++revision;
            rows.Clear();
            if (destroyed || revision != operation)
            {
                return;
            }

            count = itemCount;
            bind = bindItem;
            unbind = unbindItem;
            int rowCount = (int)((itemCount + (long)columns - 1) / columns);
            rows.SetItemCount(rowCount, BindRow, UnbindRow);
        }

        public void SetData<T>(IReadOnlyList<T> data, Action<GameObject, T, int> bindItem, Action<GameObject> unbindItem = null)
        {
            if (data == null || bindItem == null)
            {
                throw new ArgumentNullException(data == null ? nameof(data) : nameof(bindItem));
            }

            SetItemCount(data.Count, (cell, index) => bindItem(cell, data[index], index), unbindItem);
        }

        public void ScrollTo(int index)
        {
            RequireConfigured();
            if (index < 0 || index >= count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            rows.ScrollTo(index / columns);
        }

        public void RefreshItems()
        {
            RequireConfigured();
            revision++;
            rows.RefreshItems();
        }

        public void Clear()
        {
            if (destroyed)
            {
                return;
            }

            int operation = ++revision;
            rows?.Clear();
            if (destroyed || revision != operation)
            {
                return;
            }

            count = 0;
            bind = null;
            unbind = null;
        }

        private void BindRow(GameObject instance, int rowIndex)
        {
            int operation = revision;
            if (!rowLookup.TryGetValue(instance, out var row))
            {
                row = instance.AddComponent<UIVirtualGridRow>();
                row.Owner = this;
                row.Cells = new GameObject[columns];
                row.Indices = new int[columns];
                liveRows.Add(row);
                rowLookup.Add(instance, row);
                for (int column = 0; column < columns; column++)
                {
                    row.Indices[column] = -1;
                    var cell = Object.Instantiate(cellPrefab, instance.transform, false);
                    row.Cells[column] = cell;
                    cell.SetActive(false);
                    if (destroyed || revision != operation)
                    {
                        return;
                    }

                    var rect = (RectTransform)cell.transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                    rect.sizeDelta = size;
                    rect.anchoredPosition = new Vector2(column * (size.x + spacing.x), 0);
                }
            }

            // An Unbind callback can refresh while this row is still being released.
            // Drain its remaining old bindings before publishing a new binding generation.
            if (row.IsReleasing)
            {
                row.ReleaseAll();
                if (revision != operation || destroyed)
                {
                    return;
                }
            }
            row.BeginBinding(unbind);
            for (int column = 0; column < columns; column++)
            {
                var cell = row.Cells[column];
                long index = rowIndex * (long)columns + column;
                if (cell == null)
                {
                    throw new InvalidOperationException("A grid cell was destroyed externally. Clear and reassign the grid data.");
                }

                if (index >= count)
                {
                    cell.SetActive(false);
                }
                else
                {
                    row.Indices[column] = (int)index;
                    try
                    {
                        bind(cell, (int)index);
                    }
                    catch (Exception error)
                    {
                        Debug.LogException(error);
                        row.Release(column);
                        if (revision != operation || destroyed)
                        {
                            return;
                        }

                        cell.SetActive(false);
                        continue;
                    }

                    if (revision != operation || destroyed || cell == null)
                    {
                        return;
                    }

                    cell.SetActive(true);
                }

                if (revision != operation || destroyed)
                {
                    return;
                }
            }
        }

        private void UnbindRow(GameObject instance)
        {
            if (rowLookup.TryGetValue(instance, out var row))
            {
                row.ReleaseAll();
            }
        }

        internal void Forget(UIVirtualGridRow row)
        {
            liveRows.Remove(row);
            rowLookup.Remove(row.gameObject);
        }

        private void RetireRow(GameObject instance)
        {
            if (rowLookup.TryGetValue(instance, out var row))
            {
                row.ReleaseAll();
                row.Owner = null;
                row.Unbind = null;
                liveRows.Remove(row);
                rowLookup.Remove(instance);
            }
        }
        private void OnEnable()
        {
            if (rows != null)
            {
                rows.enabled = true;
            }
        }

        private void OnDisable()
        {
            if (rows != null)
            {
                rows.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (destroyed)
            {
                return;
            }

            destroyed = true;
            revision++;
            if (rows != null)
            {
                rows.Clear();
                rows.CellRetiring -= RetireRow;
                Object.Destroy(rows);
            }

            if (driver != null)
            {
                Object.Destroy(driver);
            }

            rows = null;
            count = 0;
            bind = null;
            unbind = null;
            cellPrefab = null;
            liveRows.Clear();
            rowLookup.Clear();
        }

        private void RequireConfigured()
        {
            ThrowIfDestroyed();
            if (rows == null || cellPrefab == null)
            {
                throw new InvalidOperationException("Configure the grid before assigning data.");
            }
        }

        private void ThrowIfDestroyed()
        {
            if (destroyed)
            {
                throw new ObjectDisposedException(nameof(UIVirtualGrid));
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

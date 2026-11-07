#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UI
{
    /// <summary>Fixed-size, single-row/column ScrollRect virtualization. Call on Unity's main thread.</summary>
    /// <remarks>
    /// Owns only its instantiated cells. The project retains the supplied prefab and its resource lease
    /// until the list stops using it and every instantiated cell is actually destroyed by Unity.
    /// Clear/Configure/OnDestroy only request native destruction; do not release that lease immediately.
    /// Content must be directly under Viewport, with a Mask/RectMask2D on Viewport for clipping overscan.
    /// Do not attach a LayoutGroup/ContentSizeFitter to Content.
    /// Binding callbacks run only for recycled/refreshed cells. They must reset all cell-specific state.
    /// </remarks>
    public sealed class UIVirtualList : MonoBehaviour
    {
        sealed class Cell
        {
            internal readonly GameObject Object;
            internal readonly RectTransform Rect;
            internal int Index = -1;
            internal Cell(GameObject value, RectTransform rect) { Object = value; Rect = rect; }
        }

        readonly List<Cell> cells = new();
        ScrollRect? scroll;
        RectTransform? content, viewport;
        GameObject? prefab, staging;
        Action<GameObject, int>? bind;
        Action<GameObject>? unbind;
        UnityAction<Vector2>? scrollChanged;
        bool horizontal, subscribed, rendering, refreshPending, forcePending, destroyed;
        float itemSize, spacing, viewportLength = -1;
        int overscan, maxVisibleItems, count, revision;

        public int ItemCount => count;
        public int InstantiatedItemCount => cells.Count;
        public int FirstVisibleIndex { get; private set; } = -1;
        public int LastVisibleIndex { get; private set; } = -1;
        internal event Action<GameObject>? CellRetiring;

        /// <summary>Configure before SetData/SetItemCount. The component does not load or release the prefab.</summary>
        public void Configure(ScrollRect scrollRect, GameObject itemPrefab, float itemExtent,
            bool horizontal = false, int overscan = 1, float spacing = 0, int maxVisibleItems = 512)
        {
            ThrowIfDestroyed();
            if (scrollRect == null) throw new ArgumentNullException(nameof(scrollRect));
            if (itemPrefab == null) throw new ArgumentNullException(nameof(itemPrefab));
            if (!(itemPrefab.transform is RectTransform)) throw new ArgumentException("Cell prefab requires a RectTransform.", nameof(itemPrefab));
            if (scrollRect.viewport == null || scrollRect.content == null || scrollRect.content.parent != scrollRect.viewport)
                throw new ArgumentException("ScrollRect requires Content directly under its Viewport.", nameof(scrollRect));
            if (!Finite(itemExtent) || itemExtent <= 0) throw new ArgumentOutOfRangeException(nameof(itemExtent));
            if (!Finite(spacing) || spacing < 0 || !Finite(itemExtent + spacing)) throw new ArgumentOutOfRangeException(nameof(spacing));
            if (overscan < 0 || overscan > 1024) throw new ArgumentOutOfRangeException(nameof(overscan));
            if (maxVisibleItems <= 0) throw new ArgumentOutOfRangeException(nameof(maxVisibleItems));

            int operation = revision + 1;
            Clear();
            if (destroyed || operation != revision) return;
            Unsubscribe();
            scroll = scrollRect; content = scrollRect.content; viewport = scrollRect.viewport; prefab = itemPrefab;
            this.itemSize = itemExtent; this.horizontal = horizontal; this.overscan = overscan;
            this.spacing = spacing; this.maxVisibleItems = maxVisibleItems; viewportLength = -1;
            scroll.horizontal = horizontal; scroll.vertical = !horizontal;
            content.anchorMin = horizontal ? Vector2.zero : new Vector2(0, 1);
            content.anchorMax = horizontal ? new Vector2(0, 1) : Vector2.one;
            content.pivot = horizontal ? new Vector2(0, .5f) : new Vector2(.5f, 1);
            LayoutContent(true);
            Subscribe();
        }

        /// <summary>Set a logical count and bind cells by index. Unbind clears listeners/async cell work before reuse.</summary>
        public void SetItemCount(int itemCount, Action<GameObject, int> bindItem,
            Action<GameObject>? unbindItem = null, bool resetPosition = true)
        {
            RequireConfigured();
            if (itemCount < 0 || (double)itemCount * (itemSize + spacing) > float.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(itemCount));
            if (bindItem == null) throw new ArgumentNullException(nameof(bindItem));
            int operation = ++revision;
            UnbindAll(operation);
            if (operation != revision || destroyed) return;
            count = itemCount; bind = bindItem; unbind = unbindItem;
            LayoutContent(resetPosition);
            RequestRefresh(true);
        }

        /// <summary>Convenience binding. Retains data without copying; replace/refresh after changing the source.</summary>
        public void SetData<T>(IReadOnlyList<T> data, Action<GameObject, T, int> bindItem,
            Action<GameObject>? unbindItem = null, bool resetPosition = true)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (bindItem == null) throw new ArgumentNullException(nameof(bindItem));
            // One adapter per data assignment, never one closure per scroll or per cell.
            SetItemCount(data.Count, (cell, index) => bindItem(cell, data[index], index), unbindItem, resetPosition);
        }

        public void RefreshItems()
        {
            RequireConfigured();
            int operation = ++revision;
            UnbindAll(operation);
            if (operation == revision && !destroyed) RequestRefresh(true);
        }

        public void ScrollTo(int index)
        {
            RequireConfigured();
            if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
            scroll!.StopMovement();
            SetOffset(Math.Min(index * (double)(itemSize + spacing), MaximumOffset()));
            RequestRefresh(false);
        }

        /// <summary>Release instantiated cells and data/binding references, retaining the ScrollRect configuration.</summary>
        public void Clear()
        {
            if (destroyed) return;
            int operation = ++revision;
            UnbindAll(operation);
            if (operation != revision || destroyed) return;
            bind = null; unbind = null; count = 0;
            TrimCells(0, operation);
            if (operation != revision || destroyed) return;
            FirstVisibleIndex = LastVisibleIndex = -1;
            if (content != null) LayoutContent(true);
        }

        void OnEnable() { Subscribe(); if (scroll != null) RequestRefresh(true); }
        void OnDisable() => Unsubscribe();
        void OnDestroy()
        {
            if (destroyed) return;
            destroyed = true; revision++;
            Unsubscribe();
            // Terminal cleanup cannot be vetoed by a callback that assigns another data set.
            while (cells.Count != 0)
            {
                int last = cells.Count - 1; Cell cell = cells[last]; cells.RemoveAt(last);
                UnbindCell(cell);
                DestroyCell(cell);
            }
            bind = null; unbind = null; scroll = null; content = viewport = null; prefab = null;
            if (staging != null) Object.Destroy(staging);
            staging = null;
        }

        void Subscribe()
        {
            if (destroyed || subscribed || scroll == null || !isActiveAndEnabled) return;
            scrollChanged ??= OnScrollChanged;
            scroll.onValueChanged.AddListener(scrollChanged); subscribed = true;
        }
        void Unsubscribe()
        {
            if (!subscribed) return;
            if (scroll != null && scrollChanged != null) scroll.onValueChanged.RemoveListener(scrollChanged);
            subscribed = false;
        }
        void OnScrollChanged(Vector2 value) => RequestRefresh(false);
        void LateUpdate()
        {
            if (destroyed || scroll == null || viewport == null) return;
            float length = ViewportLength();
            if (refreshPending || length != viewportLength)
            {
                bool force = forcePending; refreshPending = forcePending = false;
                RequestRefresh(force);
            }
        }

        void RequestRefresh(bool force)
        {
            if (destroyed || scroll == null || content == null || viewport == null) return;
            if (rendering) { refreshPending = true; forcePending |= force; return; }
            rendering = true;
            try { Render(force); }
            finally { rendering = false; }
        }

        void Render(bool force)
        {
            int operation = revision;
            float length = ViewportLength();
            if (length != viewportLength) { viewportLength = length; LayoutContent(false); force = true; }
            int needed = count == 0 || length <= 0 ? 0 : (int)Math.Min(count,
                Math.Ceiling(length / (double)(itemSize + spacing)) + 1 + 2d * overscan);
            if (needed > maxVisibleItems)
                throw new InvalidOperationException("Visible cell count exceeds maxVisibleItems. Increase cell size or explicitly raise the configured limit.");
            // Cells belong to this pool, but recover if project code destroys one externally.
            // Removing dead wrappers lets the existing bounded-growth path replace the missing cells.
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                if (cells[i].Object != null) continue;
                NotifyRetiring(cells[i].Object);
                cells.RemoveAt(i);
                force = true;
            }
            if (needed != cells.Count)
            {
                UnbindAll(operation);
                if (operation != revision || destroyed) return;
                TrimCells(needed, operation);
                if (operation != revision || destroyed) return;
                while (cells.Count < needed)
                    if (!CreateCell(operation)) return;
                force = true;
            }
            if (needed == 0 || bind == null) { FirstVisibleIndex = LastVisibleIndex = -1; return; }
            double offset = Math.Max(0, Math.Min(CurrentOffset(), MaximumOffset()));
            int visible = (int)Math.Min(count - 1, Math.Floor(offset / (itemSize + spacing)));
            int start = Math.Max(0, Math.Min(count - needed, visible - overscan));
            FirstVisibleIndex = visible;
            LastVisibleIndex = (int)Math.Min(count - 1, Math.Max(visible, Math.Ceiling((offset + length) / (itemSize + spacing)) - 1));
            for (int index = start; index < start + needed; index++)
            {
                Cell cell = cells[index % needed];
                if (!force && cell.Index == index && cell.Object != null) continue;
                UnbindCell(cell);
                if (operation != revision || destroyed || cell.Object == null) return;
                cell.Index = index;
                cell.Rect.anchoredPosition = horizontal ? new Vector2(index * (itemSize + spacing), 0) : new Vector2(0, -index * (itemSize + spacing));
                try { bind(cell.Object, index); }
                catch (Exception exception)
                {
                    Debug.LogException(exception); UnbindCell(cell);
                    if (operation != revision || destroyed) return;
                    if (cell.Index < 0 && cell.Object != null) cell.Object.SetActive(false);
                    continue;
                }
                if (operation != revision || destroyed || cell.Object == null) return;
                cell.Object.SetActive(true);
                if (operation != revision || destroyed) return;
            }
        }

        bool CreateCell(int operation)
        {
            if (staging == null)
            {
                staging = new GameObject("Virtual List Staging"); staging.SetActive(false);
                staging.transform.SetParent(transform, false);
            }
            GameObject instance = Object.Instantiate(prefab!, staging.transform, false);
            if (operation != revision || destroyed) { Object.Destroy(instance); return false; }
            instance.SetActive(false);
            var rect = (RectTransform)instance.transform;
            var cell = new Cell(instance, rect); cells.Add(cell);
            rect.anchorMin = horizontal ? Vector2.zero : new Vector2(0, 1);
            rect.anchorMax = horizontal ? new Vector2(0, 1) : Vector2.one;
            rect.pivot = horizontal ? new Vector2(0, .5f) : new Vector2(.5f, 1);
            rect.sizeDelta = horizontal ? new Vector2(itemSize, 0) : new Vector2(0, itemSize);
            rect.SetParent(content, false);
            return operation == revision && !destroyed;
        }
        void UnbindCell(Cell cell)
        {
            if (cell.Index < 0) return;
            cell.Index = -1;
            if (cell.Object == null) return;
            try { unbind?.Invoke(cell.Object); } catch (Exception exception) { Debug.LogException(exception); }
            // A recycled cell stays active: moving/rebinding it must not repeat OnDisable/OnEnable.
        }
        void UnbindAll(int operation)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                UnbindCell(cells[i]);
                if (operation != revision || destroyed) return;
            }
        }
        void TrimCells(int needed, int operation)
        {
            while (cells.Count > needed)
            {
                int last = cells.Count - 1; Cell cell = cells[last]; cells.RemoveAt(last);
                UnbindCell(cell);
                DestroyCell(cell);
                if (operation != revision || destroyed) return;
            }
        }
        void NotifyRetiring(GameObject instance)
        {
            try
            {
                CellRetiring?.Invoke(instance);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        void DestroyCell(Cell cell)
        {
            if (cell.Object == null)
            {
                return;
            }

            NotifyRetiring(cell.Object);
            cell.Object.SetActive(false);
            Object.Destroy(cell.Object);
        }
        void LayoutContent(bool reset)
        {
            if (content == null) return;
            float extent = count == 0 ? 0 : (float)((double)count * itemSize + (double)(count - 1) * spacing);
            content.sizeDelta = horizontal ? new Vector2(extent, 0) : new Vector2(0, extent);
            SetOffset(reset ? 0 : Math.Max(0, Math.Min(CurrentOffset(), MaximumOffset())));
        }
        void SetOffset(double value)
        {
            content!.anchoredPosition = horizontal ? new Vector2(-(float)value, 0) : new Vector2(0, (float)value);
        }
        double CurrentOffset()
        {
            if (content == null) return 0;
            float value = horizontal ? -content.anchoredPosition.x : content.anchoredPosition.y;
            return Finite(value) ? value : 0;
        }
        double MaximumOffset() => Math.Max(0, count == 0 ? 0 : (double)count * itemSize + (double)(count - 1) * spacing - ViewportLength());
        float ViewportLength()
        {
            if (viewport == null) return 0;
            float value = horizontal ? viewport.rect.width : viewport.rect.height;
            return Finite(value) ? Math.Max(0, value) : 0;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        void RequireConfigured()
        {
            ThrowIfDestroyed();
            if (scroll == null || content == null || viewport == null || prefab == null)
                throw new InvalidOperationException("Configure the virtual list before assigning data.");
        }
        void ThrowIfDestroyed() { if (destroyed) throw new ObjectDisposedException(nameof(UIVirtualList)); }
    }
}

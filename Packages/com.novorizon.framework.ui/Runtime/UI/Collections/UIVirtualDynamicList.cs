#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UI
{
    /// <summary>Vertical virtualization with keyed, measured heights and a stable scroll anchor.</summary>
    /// <remarks>
    /// Call on Unity's main thread. Content must be directly under Viewport; use a viewport mask,
    /// and no Content LayoutGroup/ContentSizeFitter. Cells stretch horizontally, with list-owned heights.
    /// ReportHeight after asynchronous content/layout completes, capturing DataVersion at bind time.
    /// An optional measurement callback runs in LateUpdate: return the desired height (for example
    /// LayoutUtility.GetPreferredHeight), not the list-assigned RectTransform height.
    /// Retain the prefab's resource lease until all owned cells have actually been destroyed by Unity.
    /// </remarks>
    public sealed class UIVirtualDynamicList : MonoBehaviour
    {
        sealed class Cell
        {
            internal readonly GameObject Object;
            internal readonly RectTransform Rect;
            internal int Index = -1;

            internal Cell(GameObject value)
            {
                Object = value;
                Rect = (RectTransform)value.transform;
            }
        }

        readonly List<Cell> cells = new();
        readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);
        string[] keys = Array.Empty<string>();
        double[] heights = Array.Empty<double>();
        double[] tree = new double[1];
        float[] measurements = Array.Empty<float>();
        ScrollRect? scroll;
        RectTransform? content, viewport;
        GameObject? prefab, staging;
        Action<GameObject, int>? bind;
        Action<GameObject>? unbind;
        Func<GameObject, float>? measure;
        UnityAction<Vector2>? scrollChanged;
        float estimate, spacing, viewportHeight = -1, viewportWidth = -1;
        int overscan, limit, revision;
        bool subscribed, rendering, pending, forcePending, destroyed;

        public int ItemCount => keys.Length;
        public int InstantiatedItemCount => cells.Count;
        public int DataVersion { get; private set; }
        public int FirstVisibleIndex { get; private set; } = -1;
        public int LastVisibleIndex { get; private set; } = -1;

        public void Configure(
            ScrollRect scrollRect,
            GameObject itemPrefab,
            float estimatedHeight,
            int overscan = 1,
            float spacing = 0,
            int maxVisibleItems = 512,
            Func<GameObject, float>? measureHeight = null)
        {
            ThrowIfDestroyed();
            if (scrollRect == null || itemPrefab == null)
            {
                throw new ArgumentNullException(scrollRect == null ? nameof(scrollRect) : nameof(itemPrefab));
            }
            if (!(itemPrefab.transform is RectTransform) || scrollRect.viewport == null ||
                scrollRect.content == null || scrollRect.content.parent != scrollRect.viewport)
            {
                throw new ArgumentException("Require a RectTransform prefab and Content directly under Viewport.");
            }
            ValidateHeight(estimatedHeight);
            if (!Finite(spacing) || spacing < 0 || !Finite(estimatedHeight + spacing))
            {
                throw new ArgumentOutOfRangeException(nameof(spacing));
            }
            if (overscan < 0 || overscan > 1024 || maxVisibleItems <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxVisibleItems));
            }

            int operation = revision + 1;
            Clear();
            if (destroyed || operation != revision)
            {
                return;
            }
            Unsubscribe();
            scroll = scrollRect;
            content = scrollRect.content;
            viewport = scrollRect.viewport;
            prefab = itemPrefab;
            estimate = estimatedHeight;
            this.spacing = spacing;
            this.overscan = overscan;
            limit = maxVisibleItems;
            measure = measureHeight;
            viewportHeight = -1;
            viewportWidth = viewport.rect.width;
            scroll.horizontal = false;
            scroll.vertical = true;
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(.5f, 1);
            LayoutContent(0);
            Subscribe();
        }

        /// <summary>Keys are copied and must be nonempty/unique. Heights reset unless explicitly retained.</summary>
        public void SetItems(
            IReadOnlyList<string> itemKeys,
            Action<GameObject, int> bindItem,
            Action<GameObject>? unbindItem = null,
            bool resetPosition = true,
            bool retainHeights = false)
        {
            RequireConfigured();
            if (itemKeys == null || bindItem == null)
            {
                throw new ArgumentNullException(itemKeys == null ? nameof(itemKeys) : nameof(bindItem));
            }
            var nextKeys = new string[itemKeys.Count];
            var nextHeights = new double[itemKeys.Count];
            var nextIndices = new Dictionary<string, int>(itemKeys.Count, StringComparer.Ordinal);
            double total = 0;
            for (int i = 0; i < nextKeys.Length; i++)
            {
                string key = itemKeys[i];
                if (string.IsNullOrEmpty(key) || nextIndices.ContainsKey(key))
                {
                    throw new ArgumentException("Item keys must be nonempty and unique.", nameof(itemKeys));
                }
                nextKeys[i] = key;
                nextIndices.Add(key, i);
                nextHeights[i] = retainHeights && indices.TryGetValue(key, out int old) ? heights[old] : estimate;
                total += nextHeights[i] + spacing;
            }
            ValidateTotal(total);
            CaptureAnchor(out string? anchor, out double inside, out double offset);
            int operation = ++revision;
            DataVersion++;
            UnbindAll(operation);
            if (operation != revision || destroyed)
            {
                return;
            }
            keys = nextKeys;
            heights = nextHeights;
            indices.Clear();
            foreach (var pair in nextIndices)
            {
                indices.Add(pair.Key, pair.Value);
            }
            BuildTree();
            bind = bindItem;
            unbind = unbindItem;
            LayoutContent(resetPosition ? 0 : RestoreAnchor(anchor, inside, offset));
            RequestRefresh(true);
        }

        /// <summary>Retains data for binding; keys are snapshotted. Reassign after mutating the source.</summary>
        public void SetData<T>(
            IReadOnlyList<T> data,
            Func<T, string> keySelector,
            Action<GameObject, T, int> bindItem,
            Action<GameObject>? unbindItem = null,
            bool resetPosition = true,
            bool retainHeights = false)
        {
            if (data == null || keySelector == null || bindItem == null)
            {
                throw new ArgumentNullException(data == null ? nameof(data) : keySelector == null ? nameof(keySelector) : nameof(bindItem));
            }
            var itemKeys = new string[data.Count];
            for (int i = 0; i < itemKeys.Length; i++)
            {
                itemKeys[i] = keySelector(data[i]);
            }
            SetItems(itemKeys, (cell, index) => bindItem(cell, data[index], index), unbindItem, resetPosition, retainHeights);
        }

        /// <summary>Rejects stale asynchronous results and unknown keys. Preserves the first visible item.</summary>
        public bool ReportHeight(string key, float height, int dataVersion)
        {
            RequireConfigured();
            if (dataVersion != DataVersion || key == null || !indices.TryGetValue(key, out int index))
            {
                return false;
            }
            ValidateHeight(height);
            if (heights[index] == height)
            {
                return true;
            }
            ValidateTotal(Prefix(ItemCount) + height - heights[index]);
            CaptureAnchor(out string? anchor, out double inside, out double offset);
            UpdateHeight(index, height);
            revision++;
            LayoutContent(RestoreAnchor(anchor, inside, offset));
            RequestRefresh(false);
            return true;
        }

        /// <summary>Reset cached heights after text/font/width changes. Existing async reports become stale.</summary>
        public void InvalidateHeights()
        {
            RequireConfigured();
            CaptureAnchor(out string? anchor, out double inside, out double offset);
            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] = estimate;
            }
            BuildTree();
            DataVersion++;
            revision++;
            LayoutContent(RestoreAnchor(anchor, inside, offset));
            RequestRefresh(true);
        }

        public void RefreshItems()
        {
            RequireConfigured();
            DataVersion++;
            revision++;
            RequestRefresh(true);
        }

        public void ScrollTo(int index)
        {
            RequireConfigured();
            if (index < 0 || index >= ItemCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            scroll!.StopMovement();
            SetOffset(Prefix(index));
            RequestRefresh(false);
        }

        public void Clear()
        {
            if (destroyed)
            {
                return;
            }
            int operation = ++revision;
            DataVersion++;
            UnbindAll(operation);
            if (operation != revision || destroyed)
            {
                return;
            }
            bind = null;
            unbind = null;
            keys = Array.Empty<string>();
            heights = Array.Empty<double>();
            tree = new double[1];
            indices.Clear();
            TrimCells(0, operation);
            if (operation != revision || destroyed)
            {
                return;
            }
            FirstVisibleIndex = LastVisibleIndex = -1;
            LayoutContent(0);
        }

        void UpdateHeight(int index, float height)
        {
            double delta = height - heights[index];
            heights[index] = height;
            for (int node = index + 1; node < tree.Length; node += node & -node)
            {
                tree[node] += delta;
            }
        }

        void BuildTree()
        {
            tree = new double[heights.Length + 1];
            for (int i = 1; i < tree.Length; i++)
            {
                tree[i] += heights[i - 1] + spacing;
                int parent = i + (i & -i);
                if (parent < tree.Length)
                {
                    tree[parent] += tree[i];
                }
            }
        }

        double Prefix(int count)
        {
            double sum = 0;
            for (int node = count; node > 0; node -= node & -node)
            {
                sum += tree[node];
            }
            return sum;
        }

        int FindIndex(double offset)
        {
            int index = 0;
            double sum = 0;
            int step = 1;
            while (step <= ItemCount / 2)
            {
                step <<= 1;
            }
            for (; step > 0; step >>= 1)
            {
                int next = index + step;
                if (next < tree.Length && sum + tree[next] <= offset)
                {
                    index = next;
                    sum += tree[next];
                }
            }
            return Math.Min(index, ItemCount - 1);
        }

        void CaptureAnchor(out string? key, out double inside, out double offset)
        {
            offset = Math.Max(0, Math.Min(CurrentOffset(), MaximumOffset()));
            int index = ItemCount == 0 ? -1 : FindIndex(offset);
            key = index < 0 ? null : keys[index];
            inside = index < 0 ? 0 : offset - Prefix(index);
        }

        double RestoreAnchor(string? key, double inside, double fallback)
        {
            return key != null && indices.TryGetValue(key, out int index)
                ? Prefix(index) + Math.Min(inside, Math.Max(0, heights[index] - .01)) : fallback;
        }

        void OnEnable()
        {
            Subscribe();
            RequestRefresh(true);
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void OnDestroy()
        {
            if (destroyed)
            {
                return;
            }
            destroyed = true;
            revision++;
            Unsubscribe();
            while (cells.Count > 0)
            {
                Cell cell = cells[cells.Count - 1];
                cells.RemoveAt(cells.Count - 1);
                UnbindCell(cell);
                DestroyCell(cell);
            }
            bind = null;
            unbind = null;
            measure = null;
            keys = Array.Empty<string>();
            heights = Array.Empty<double>();
            tree = Array.Empty<double>();
            measurements = Array.Empty<float>();
            indices.Clear();
            scroll = null;
            content = viewport = null;
            prefab = null;
            if (staging != null)
            {
                Object.Destroy(staging);
            }
            staging = null;
        }

        void Subscribe()
        {
            if (destroyed || subscribed || scroll == null || !isActiveAndEnabled)
            {
                return;
            }
            scrollChanged ??= OnScrollChanged;
            scroll.onValueChanged.AddListener(scrollChanged);
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (subscribed && scroll != null && scrollChanged != null)
            {
                scroll.onValueChanged.RemoveListener(scrollChanged);
            }
            subscribed = false;
        }

        void OnScrollChanged(Vector2 value)
        {
            RequestRefresh(false);
        }

        void LateUpdate()
        {
            if (destroyed || viewport == null)
            {
                return;
            }
            float width = viewport.rect.width;
            if (viewportWidth >= 0 && width != viewportWidth)
            {
                viewportWidth = width;
                InvalidateHeights();
                if (destroyed)
                {
                    return;
                }
            }
            viewportWidth = width;
            if (pending || ViewportLength() != viewportHeight)
            {
                bool force = forcePending;
                pending = forcePending = false;
                RequestRefresh(force);
            }
            MeasureVisibleHeights();
        }

        void MeasureVisibleHeights()
        {
            if (measure == null)
            {
                return;
            }
            // Measure only the current bounded window, then publish one correction. No forced
            // layout and no recursive measurement of newly entering cells in the same frame.
            if (measurements.Length < cells.Count)
            {
                measurements = new float[cells.Count];
            }
            int operation = revision;
            double total = Prefix(ItemCount);
            bool changed = false;
            for (int i = 0; i < cells.Count; i++)
            {
                measurements[i] = 0;
                Cell cell = cells[i];
                if (cell.Index < 0 || cell.Object == null || !cell.Object.activeInHierarchy)
                {
                    continue;
                }
                float height;
                try
                {
                    height = measure(cell.Object);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    if (operation != revision || destroyed)
                    {
                        return;
                    }
                    continue;
                }
                if (operation != revision || destroyed)
                {
                    return;
                }
                if (Finite(height) && height > 0 && heights[cell.Index] != height)
                {
                    measurements[i] = height;
                    total += height - heights[cell.Index];
                    changed = true;
                }
            }
            if (!changed)
            {
                return;
            }
            ValidateTotal(total);
            CaptureAnchor(out string? anchor, out double inside, out double offset);
            for (int i = 0; i < cells.Count; i++)
            {
                if (measurements[i] > 0)
                {
                    UpdateHeight(cells[i].Index, measurements[i]);
                }
            }
            revision++;
            LayoutContent(RestoreAnchor(anchor, inside, offset));
            RequestRefresh(false);
        }

        void RequestRefresh(bool force)
        {
            if (destroyed || scroll == null || content == null || viewport == null)
            {
                return;
            }
            if (rendering)
            {
                pending = true;
                forcePending |= force;
                return;
            }
            rendering = true;
            int operation = revision;
            try
            {
                Render(force);
            }
            finally
            {
                rendering = false;
                // A height report inside Bind can interrupt a forced refresh. Remaining rows
                // still need fresh bindings/version tokens when the deferred window renders.
                if (force && pending && operation != revision)
                {
                    forcePending = true;
                }
            }
        }

        void Render(bool force)
        {
            int operation = revision;
            float length = ViewportLength();
            if (length != viewportHeight)
            {
                viewportHeight = length;
                SetOffset(CurrentOffset());
            }
            double offset = Math.Max(0, Math.Min(CurrentOffset(), MaximumOffset()));
            FirstVisibleIndex = ItemCount == 0 || viewportHeight <= 0 ? -1 : FindIndex(offset);
            LastVisibleIndex = FirstVisibleIndex < 0 ? -1 : FindIndex(offset + viewportHeight);
            if (LastVisibleIndex > FirstVisibleIndex && Prefix(LastVisibleIndex) == offset + viewportHeight)
            {
                LastVisibleIndex--;
            }
            int start = Math.Max(0, FirstVisibleIndex - overscan);
            int end = LastVisibleIndex < 0 ? -1 : Math.Min(ItemCount - 1, LastVisibleIndex + overscan);
            int needed = Math.Max(0, end - start + 1);
            if (needed > limit)
            {
                throw new InvalidOperationException("Visible cell count exceeds maxVisibleItems; raise the limit or use larger heights.");
            }
            for (int i = cells.Count - 1; i >= 0; i--)
            {
                if (cells[i].Object == null)
                {
                    cells.RemoveAt(i);
                    force = true;
                }
            }
            if (needed > cells.Count)
            {
                UnbindAll(operation);
                if (operation != revision || destroyed)
                {
                    return;
                }
                while (cells.Count < needed)
                {
                    if (!CreateCell(operation))
                    {
                        return;
                    }
                }
                force = true;
            }
            for (int i = 0; i < cells.Count; i++)
            {
                Cell cell = cells[i];
                int nextIndex = start + (i - start % cells.Count + cells.Count) % cells.Count;
                if (nextIndex <= end)
                {
                    continue;
                }
                UnbindCell(cell);
                if (operation != revision || destroyed)
                {
                    return;
                }
                if (cell.Object != null && cell.Object.activeSelf)
                {
                    cell.Object.SetActive(false);
                    if (operation != revision || destroyed)
                    {
                        return;
                    }
                }
            }
            for (int index = start; index <= end; index++)
            {
                Cell cell = cells[index % cells.Count];
                cell.Rect.anchoredPosition = new Vector2(0, -(float)Prefix(index));
                if (operation != revision || destroyed || cell.Object == null)
                {
                    return;
                }
                cell.Rect.sizeDelta = new Vector2(0, (float)heights[index]);
                if (operation != revision || destroyed || cell.Object == null)
                {
                    return;
                }
                if (!force && cell.Index == index)
                {
                    continue;
                }
                UnbindCell(cell);
                if (operation != revision || destroyed || cell.Object == null)
                {
                    return;
                }
                cell.Index = index;
                try
                {
                    bind?.Invoke(cell.Object, index);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    if (operation != revision || destroyed)
                    {
                        return;
                    }
                    UnbindCell(cell);
                    if (operation != revision || destroyed)
                    {
                        return;
                    }
                    if (cell.Object != null)
                    {
                        cell.Object.SetActive(false);
                    }
                    continue;
                }
                if (operation != revision || destroyed || cell.Object == null)
                {
                    return;
                }
                cell.Object.SetActive(true);
                if (operation != revision || destroyed)
                {
                    return;
                }
            }
        }

        bool CreateCell(int operation)
        {
            if (staging == null)
            {
                staging = new GameObject("Dynamic List Staging");
                staging.SetActive(false);
                staging.transform.SetParent(transform, false);
            }
            GameObject instance = Object.Instantiate(prefab!, staging.transform, false);
            if (operation != revision || destroyed)
            {
                Object.Destroy(instance);
                return false;
            }
            instance.SetActive(false);
            var cell = new Cell(instance);
            cells.Add(cell);
            cell.Rect.anchorMin = new Vector2(0, 1);
            cell.Rect.anchorMax = Vector2.one;
            cell.Rect.pivot = new Vector2(.5f, 1);
            cell.Rect.SetParent(content, false);
            return operation == revision && !destroyed;
        }

        void UnbindCell(Cell cell)
        {
            if (cell.Index < 0)
            {
                return;
            }
            cell.Index = -1;
            if (cell.Object != null)
            {
                try
                {
                    unbind?.Invoke(cell.Object);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                }
            }
        }

        void UnbindAll(int operation)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                UnbindCell(cells[i]);
                if (operation != revision || destroyed)
                {
                    return;
                }
            }
        }

        void TrimCells(int needed, int operation)
        {
            while (cells.Count > needed)
            {
                Cell cell = cells[cells.Count - 1];
                cells.RemoveAt(cells.Count - 1);
                UnbindCell(cell);
                DestroyCell(cell);
                if (operation != revision || destroyed)
                {
                    return;
                }
            }
        }

        static void DestroyCell(Cell cell)
        {
            if (cell.Object != null)
            {
                cell.Object.SetActive(false);
                Object.Destroy(cell.Object);
            }
        }

        void LayoutContent(double offset)
        {
            if (content != null)
            {
                int operation = revision;
                content.sizeDelta = new Vector2(0, (float)Extent());
                if (operation == revision && !destroyed && content != null)
                {
                    SetOffset(offset);
                }
            }
        }

        double Extent() => Math.Max(0, Prefix(ItemCount) - (ItemCount == 0 ? 0 : spacing));
        double MaximumOffset() => Math.Max(0, Extent() - ViewportLength());
        double CurrentOffset() => content == null || !Finite(content.anchoredPosition.y) ? 0 : content.anchoredPosition.y;
        float ViewportLength() => viewport == null || !Finite(viewport.rect.height) ? 0 : Math.Max(0, viewport.rect.height);

        void SetOffset(double offset)
        {
            content!.anchoredPosition = new Vector2(0, (float)Math.Max(0, Math.Min(offset, MaximumOffset())));
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static void ValidateHeight(float height)
        {
            if (!Finite(height) || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }
        }

        static void ValidateTotal(double total)
        {
            if (total > float.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(total));
            }
        }

        void RequireConfigured()
        {
            ThrowIfDestroyed();
            if (scroll == null || content == null || viewport == null || prefab == null)
            {
                throw new InvalidOperationException("Configure the dynamic list before assigning data.");
            }
        }

        void ThrowIfDestroyed()
        {
            if (destroyed)
            {
                throw new ObjectDisposedException(nameof(UIVirtualDynamicList));
            }
        }
    }
}

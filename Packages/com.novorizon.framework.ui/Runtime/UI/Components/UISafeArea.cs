#nullable enable
using UnityEngine;

namespace UI
{
    /// <summary>Insets an axis-aligned screen UI content container into the device safe area.</summary>
    /// <remarks>
    /// Attach below a screen-space Canvas. Keep full-screen backgrounds outside this container.
    /// This component owns the fitted axes; do not drive those axes with another layout controller.
    /// It does not change its parent, the Canvas, or sibling backgrounds.
    /// </remarks>
    /// <remarks>提供界面安全面积的实现。</remarks>
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class UISafeArea : MonoBehaviour
    {
        [SerializeField] bool fitHorizontal = true;
        [SerializeField] bool fitVertical = true;

        RectTransform? target;
        Canvas? parentCanvas;
        RectTransform? canvasParent;
        Layout original;
        Snapshot previous;
        bool captured, hasSnapshot, applying, ownsHorizontal, ownsVertical;

        public bool IsApplied { get; private set; }

        public bool FitHorizontal
        {
            get => fitHorizontal;
            set { if (fitHorizontal == value) return; fitHorizontal = value; Invalidate(); }
        }

        public bool FitVertical
        {
            get => fitVertical;
            set { if (fitVertical == value) return; fitVertical = value; Invalidate(); }
        }

        /// <summary>Immediately refresh after changing the layout. Returns false for unsupported contexts.</summary>
        /// <remarks>应用界面安全面积。</remarks>
        public bool Apply()
        {
            if (!isActiveAndEnabled || applying) return false;
            if (target == null) target = GetComponent<RectTransform>();
            if (target == null) return false;
            if (!captured) { original = Layout.Read(target); captured = true; }
            hasSnapshot = false;
            return ApplyIfChanged();
        }

        /// <summary>响应 Unity 组件启用。</summary>
        void OnEnable() => Apply();
        /// <summary>推进该对象的一次更新。</summary>
        void Update() { if (captured) ApplyIfChanged(); }
        /// <summary>Unity 在编辑器里验证序列化数据时执行本地校验。</summary>
        void OnValidate() { hasSnapshot = false; }
        /// <summary>响应矩形变换 Dimensions 变更。</summary>
        void OnRectTransformDimensionsChange() { if (captured && !applying) ApplyIfChanged(); }
        /// <summary>响应变换父节点变化。</summary>
        void OnTransformParentChanged() { parentCanvas = null; Invalidate(); }
        /// <summary>响应画布层级变化。</summary>
        void OnCanvasHierarchyChanged() { parentCanvas = null; Invalidate(); }

        /// <summary>响应 Unity 组件停用。</summary>
        void OnDisable()
        {
            RestoreOwnedAxes();
            captured = hasSnapshot = false;
            parentCanvas = null;
            canvasParent = null;
        }

        /// <summary>使失效界面安全面积。</summary>
        void Invalidate()
        {
            hasSnapshot = false;
            if (captured && !applying && isActiveAndEnabled) ApplyIfChanged();
        }

        /// <summary>应用 If 变化。</summary>
        bool ApplyIfChanged()
        {
            if (applying || target == null || !isActiveAndEnabled) return IsApplied;
            if (!TryCapture(out Snapshot current))
            {
                hasSnapshot = false;
                RestoreOwnedAxes();
                return false;
            }
            if (hasSnapshot && previous.SameAs(in current)) return IsApplied;
            previous = current;
            hasSnapshot = true;

            if (!TryProject(in current, out Vector2 safeMin, out Vector2 safeMax))
            {
                RestoreOwnedAxes();
                return false;
            }

            applying = true;
            try
            {
                Layout layout = Layout.Read(target);
                if (fitHorizontal)
                {
                    layout.Min.x = safeMin.x; layout.Max.x = safeMax.x;
                    layout.OffsetMin.x = layout.OffsetMax.x = 0;
                }
                else if (ownsHorizontal) layout.RestoreHorizontal(original);
                if (fitVertical)
                {
                    layout.Min.y = safeMin.y; layout.Max.y = safeMax.y;
                    layout.OffsetMin.y = layout.OffsetMax.y = 0;
                }
                else if (ownsVertical) layout.RestoreVertical(original);
                layout.Write(target);
                ownsHorizontal = fitHorizontal;
                ownsVertical = fitVertical;
                IsApplied = fitHorizontal || fitVertical;
                return IsApplied;
            }
            finally { applying = false; }
        }

        /// <summary>尝试处理 Capture，返回是否成功。</summary>
        bool TryCapture(out Snapshot snapshot)
        {
            snapshot = default;
            RectTransform? parent = target!.parent as RectTransform;
            if (parent == null) return false;
            if (parentCanvas == null || !ReferenceEquals(canvasParent, parent))
            {
                parentCanvas = GetComponentInParent<Canvas>();
                canvasParent = parent;
            }
            Canvas? canvas = parentCanvas != null ? parentCanvas.rootCanvas : null;
            if (canvas == null) return false;
            Camera? camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            snapshot = new Snapshot
            {
                Parent = parent, Canvas = canvas, Camera = camera,
                ParentRect = parent.rect, ParentMatrix = parent.localToWorldMatrix,
                ScreenWidth = Screen.width, ScreenHeight = Screen.height, SafeRect = Screen.safeArea,
                CanvasRect = canvas.pixelRect, RenderMode = canvas.renderMode, Scale = canvas.scaleFactor,
                CameraRect = camera != null ? camera.pixelRect : default,
                CameraView = camera != null ? camera.worldToCameraMatrix : Matrix4x4.identity,
                CameraProjection = camera != null ? camera.projectionMatrix : Matrix4x4.identity,
                Horizontal = fitHorizontal, Vertical = fitVertical
            };
            return true;
        }

        /// <summary>尝试处理项目，返回是否成功。</summary>
        static bool TryProject(in Snapshot input, out Vector2 min, out Vector2 max)
        {
            min = max = default;
            if (input.RenderMode == RenderMode.WorldSpace || input.ScreenWidth <= 0 || input.ScreenHeight <= 0 ||
                input.ParentRect.width <= 0 || input.ParentRect.height <= 0 ||
                input.SafeRect.width <= 0 || input.SafeRect.height <= 0 ||
                input.CanvasRect.width <= 0 || input.CanvasRect.height <= 0) return false;

            float xMin = Mathf.Max(0, Mathf.Max(input.SafeRect.xMin, input.CanvasRect.xMin));
            float yMin = Mathf.Max(0, Mathf.Max(input.SafeRect.yMin, input.CanvasRect.yMin));
            float xMax = Mathf.Min(input.ScreenWidth, Mathf.Min(input.SafeRect.xMax, input.CanvasRect.xMax));
            float yMax = Mathf.Min(input.ScreenHeight, Mathf.Min(input.SafeRect.yMax, input.CanvasRect.yMax));
            if (input.Camera != null)
            {
                xMin = Mathf.Max(xMin, input.CameraRect.xMin); yMin = Mathf.Max(yMin, input.CameraRect.yMin);
                xMax = Mathf.Min(xMax, input.CameraRect.xMax); yMax = Mathf.Min(yMax, input.CameraRect.yMax);
            }
            if (xMax <= xMin || yMax <= yMin) return false;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(input.Parent!, new Vector2(xMin, yMin), input.Camera, out Vector2 localMin) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(input.Parent!, new Vector2(xMax, yMax), input.Camera, out Vector2 localMax)) return false;
            min = new Vector2(Mathf.Clamp01((Mathf.Min(localMin.x, localMax.x) - input.ParentRect.xMin) / input.ParentRect.width),
                Mathf.Clamp01((Mathf.Min(localMin.y, localMax.y) - input.ParentRect.yMin) / input.ParentRect.height));
            max = new Vector2(Mathf.Clamp01((Mathf.Max(localMin.x, localMax.x) - input.ParentRect.xMin) / input.ParentRect.width),
                Mathf.Clamp01((Mathf.Max(localMin.y, localMax.y) - input.ParentRect.yMin) / input.ParentRect.height));
            return Finite(min.x) && Finite(min.y) && Finite(max.x) && Finite(max.y);
        }

        /// <summary>返回Finite的判断结果。</summary>
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>恢复自有 Axes。</summary>
        void RestoreOwnedAxes()
        {
            IsApplied = false;
            if (!captured || target == null || (!ownsHorizontal && !ownsVertical)) return;
            applying = true;
            try
            {
                Layout layout = Layout.Read(target);
                if (ownsHorizontal) layout.RestoreHorizontal(original);
                if (ownsVertical) layout.RestoreVertical(original);
                ownsHorizontal = ownsVertical = false;
                layout.Write(target);
            }
            finally { applying = false; }
        }

        /// <summary>保存布局所需的数据与配套操作。</summary>
        struct Layout
        {
            internal Vector2 Min, Max, OffsetMin, OffsetMax;
            /// <summary>读取传入的 rect。</summary>
            internal static Layout Read(RectTransform rect) => new Layout
            { Min = rect.anchorMin, Max = rect.anchorMax, OffsetMin = rect.offsetMin, OffsetMax = rect.offsetMax };
            /// <summary>恢复 Horizontal。</summary>
            internal void RestoreHorizontal(Layout value)
            { Min.x = value.Min.x; Max.x = value.Max.x; OffsetMin.x = value.OffsetMin.x; OffsetMax.x = value.OffsetMax.x; }
            /// <summary>恢复 Vertical。</summary>
            internal void RestoreVertical(Layout value)
            { Min.y = value.Min.y; Max.y = value.Max.y; OffsetMin.y = value.OffsetMin.y; OffsetMax.y = value.OffsetMax.y; }
            /// <summary>写入传入的 rect。</summary>
            internal void Write(RectTransform rect)
            {
                if (rect.anchorMin != Min) rect.anchorMin = Min;
                if (rect.anchorMax != Max) rect.anchorMax = Max;
                if (rect.offsetMin != OffsetMin) rect.offsetMin = OffsetMin;
                if (rect.offsetMax != OffsetMax) rect.offsetMax = OffsetMax;
            }
        }

        /// <summary>保存快照所需的数据与配套操作。</summary>
        struct Snapshot
        {
            internal RectTransform? Parent;
            internal Canvas? Canvas;
            internal Camera? Camera;
            internal Rect ParentRect, SafeRect, CanvasRect, CameraRect;
            internal Matrix4x4 ParentMatrix, CameraView, CameraProjection;
            internal RenderMode RenderMode;
            internal int ScreenWidth, ScreenHeight;
            internal float Scale;
            internal bool Horizontal, Vertical;
            /// <summary>比较是否相同 As。</summary>
            internal readonly bool SameAs(in Snapshot other) => ReferenceEquals(Parent, other.Parent) && ReferenceEquals(Canvas, other.Canvas) &&
                ReferenceEquals(Camera, other.Camera) && ParentRect == other.ParentRect && SafeRect == other.SafeRect &&
                CanvasRect == other.CanvasRect && CameraRect == other.CameraRect && ParentMatrix == other.ParentMatrix &&
                CameraView == other.CameraView && CameraProjection == other.CameraProjection && RenderMode == other.RenderMode &&
                ScreenWidth == other.ScreenWidth && ScreenHeight == other.ScreenHeight && Scale == other.Scale &&
                Horizontal == other.Horizontal && Vertical == other.Vertical;
        }
    }
}

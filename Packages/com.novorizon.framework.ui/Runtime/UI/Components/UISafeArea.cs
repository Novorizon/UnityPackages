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
        public bool Apply()
        {
            if (!isActiveAndEnabled || applying) return false;
            if (target == null) target = GetComponent<RectTransform>();
            if (target == null) return false;
            if (!captured) { original = Layout.Read(target); captured = true; }
            hasSnapshot = false;
            return ApplyIfChanged();
        }

        void OnEnable() => Apply();
        void Update() { if (captured) ApplyIfChanged(); }
        void OnValidate() { hasSnapshot = false; }
        void OnRectTransformDimensionsChange() { if (captured && !applying) ApplyIfChanged(); }
        void OnTransformParentChanged() { parentCanvas = null; Invalidate(); }
        void OnCanvasHierarchyChanged() { parentCanvas = null; Invalidate(); }

        void OnDisable()
        {
            RestoreOwnedAxes();
            captured = hasSnapshot = false;
            parentCanvas = null;
            canvasParent = null;
        }

        void Invalidate()
        {
            hasSnapshot = false;
            if (captured && !applying && isActiveAndEnabled) ApplyIfChanged();
        }

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

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

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

        struct Layout
        {
            internal Vector2 Min, Max, OffsetMin, OffsetMax;
            internal static Layout Read(RectTransform rect) => new Layout
            { Min = rect.anchorMin, Max = rect.anchorMax, OffsetMin = rect.offsetMin, OffsetMax = rect.offsetMax };
            internal void RestoreHorizontal(Layout value)
            { Min.x = value.Min.x; Max.x = value.Max.x; OffsetMin.x = value.OffsetMin.x; OffsetMax.x = value.OffsetMax.x; }
            internal void RestoreVertical(Layout value)
            { Min.y = value.Min.y; Max.y = value.Max.y; OffsetMin.y = value.OffsetMin.y; OffsetMax.y = value.OffsetMax.y; }
            internal void Write(RectTransform rect)
            {
                if (rect.anchorMin != Min) rect.anchorMin = Min;
                if (rect.anchorMax != Max) rect.anchorMax = Max;
                if (rect.offsetMin != OffsetMin) rect.offsetMin = OffsetMin;
                if (rect.offsetMax != OffsetMax) rect.offsetMax = OffsetMax;
            }
        }

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
            internal readonly bool SameAs(in Snapshot other) => ReferenceEquals(Parent, other.Parent) && ReferenceEquals(Canvas, other.Canvas) &&
                ReferenceEquals(Camera, other.Camera) && ParentRect == other.ParentRect && SafeRect == other.SafeRect &&
                CanvasRect == other.CanvasRect && CameraRect == other.CameraRect && ParentMatrix == other.ParentMatrix &&
                CameraView == other.CameraView && CameraProjection == other.CameraProjection && RenderMode == other.RenderMode &&
                ScreenWidth == other.ScreenWidth && ScreenHeight == other.ScreenHeight && Scale == other.Scale &&
                Horizontal == other.Horizontal && Vertical == other.Vertical;
        }
    }
}

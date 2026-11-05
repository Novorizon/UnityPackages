using UnityEngine;
using UnityEngine.UI;

namespace UI
{
    [CreateAssetMenu(menuName = "UI/UI Settings", fileName = "UISettings")]
    public sealed class UISettings : ScriptableObject
    {
        [Header("Canvas Scaler")]
        public int referenceWidth = 1080;
        public int referenceHeight = 1920;
        public CanvasScaler.ScreenMatchMode screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        [Range(0, 1)]
        public float matchWidthOrHeight = 0.5f;
        [Header("Inactive View Cache")]
        [Tooltip("Maximum cached views across all prefab paths. Zero disables caching.")]
        public int maxCachedInstances = 32;
        [Tooltip("Maximum cached views per prefab path.")]
        public int maxCachedPerPath = 2;
        [Header("Canvas")]
        [Tooltip("Overlay is the stable default. Enable to follow Camera.main, or call UIManager.SetCamera for a dedicated UI camera.")]
        public bool useMainCamera = false;
        [Tooltip("Create a legacy-input fallback only when no project-owned EventSystem is active. Input System-only projects must provide their own system.")]
        public bool autoCreateEventSystem = true;
        public float canvasPlaneDistance = 100f;
        [Tooltip("Layer order multiplier, clamped to 1..819. Panels render between Pages and Popups while preserving their serialized enum value.")]
        public int sortingOrderStep = 100;
    }
}
#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;

namespace UI
{
    public sealed class UIBootstrap : MonoBehaviour
    {
        [SerializeField]
        UISettings? settings = null;
        [SerializeField]
        Camera? uiCamera = null;
        [SerializeField]
        EventSystem? eventSystem = null;
        void Awake() => ApplyConfiguration();
        void OnEnable() => ApplyConfiguration();
        void ApplyConfiguration()
        {
            UIManager root = UIManager.Instance;
            if (settings != null)
            {
                root.SetSettings(settings);
            }

            if (uiCamera != null)
            {
                root.SetCamera(uiCamera);
            }

            if (eventSystem != null)
            {
                root.SetEventSystem(eventSystem);
            }
        }
    }
}
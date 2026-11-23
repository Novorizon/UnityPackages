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
            _ = UIManager.Instance;
            if (settings != null)
            {
                UIManager.SetSettings(settings);
            }

            if (uiCamera != null)
            {
                UIManager.SetCamera(uiCamera);
            }

            if (eventSystem != null)
            {
                UIManager.SetEventSystem(eventSystem);
            }
        }
    }
}
#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;

namespace UI
{
    /// <summary>提供界面启动入口的实现。</summary>
    public sealed class UIBootstrap : MonoBehaviour
    {
        [SerializeField]
        UISettings? settings = null;
        [SerializeField]
        Camera? uiCamera = null;
        [SerializeField]
        EventSystem? eventSystem = null;
        /// <summary>Unity 创建组件时执行本组件的初始化逻辑。</summary>
        void Awake() => ApplyConfiguration();
        /// <summary>响应 Unity 组件启用。</summary>
        void OnEnable() => ApplyConfiguration();
        /// <summary>应用配置。</summary>
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
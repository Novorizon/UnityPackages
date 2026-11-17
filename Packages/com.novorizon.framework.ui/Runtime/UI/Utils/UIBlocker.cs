#nullable enable
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UI
{
    /// <summary>提供界面屏蔽项的实现。</summary>
    public sealed class UIBlocker : MonoBehaviour, IPointerClickHandler
    {
        public event Action? Clicked;

        /// <summary>创建界面屏蔽项。</summary>
        public static UIBlocker Create(Transform parent, string name = "ModalBlocker")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.raycastTarget = true;
            img.color = new Color(0f, 0f, 0f, 0.4f);

            return go.AddComponent<UIBlocker>();
        }

        /// <summary>响应指针 Click。</summary>
        public void OnPointerClick(PointerEventData eventData)
        {
            Clicked?.Invoke();
        }
    }
}

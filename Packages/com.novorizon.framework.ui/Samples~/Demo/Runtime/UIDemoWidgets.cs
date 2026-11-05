#nullable enable
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace UI.Demo
{
    internal static class UIDemoWidgets
    {
        static Font? font;
        internal static Font Font => font != null ? font : font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        internal static readonly Color Background = new(.055f, .075f, .115f, 1);
        internal static readonly Color Card = new(.11f, .15f, .22f, 1);
        internal static readonly Color Accent = new(.16f, .38f, .65f, 1);

        internal static GameObject Template<T>(string name, Color background) where T : UIView
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.SetActive(false);
            Stretch((RectTransform)go.transform);
            var image = go.AddComponent<Image>(); image.color = background; image.raycastTarget = false;
            go.AddComponent<T>();
            return go;
        }

        internal static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        internal static RectTransform SafeContent(Transform parent)
        {
            RectTransform safe = Rect("SafeContent", parent); Stretch(safe);
            safe.gameObject.AddComponent<UISafeArea>();
            return safe;
        }

        internal static void Top(RectTransform rect, float top, float height, float left = 24, float right = 24,
            float minX = 0, float maxX = 1)
        {
            rect.anchorMin = new Vector2(minX, 1); rect.anchorMax = new Vector2(maxX, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.offsetMin = new Vector2(left, -top - height); rect.offsetMax = new Vector2(-right, -top);
        }

        internal static Text Text(string name, Transform parent, string message, int size = 22)
        {
            RectTransform rect = Rect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font; text.fontSize = size; text.alignment = TextAnchor.UpperLeft;
            text.color = Color.white; text.text = message; text.raycastTarget = false;
            return text;
        }

        internal static Button Button(string name, Transform parent, string caption, UnityAction action)
        {
            RectTransform rect = Rect(name, parent);
            var image = rect.gameObject.AddComponent<Image>(); image.color = Accent;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            button.onClick.AddListener(action);
            Text label = Text("Caption", rect, caption, 22);
            Stretch((RectTransform)label.transform); label.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        internal static RectTransform CenterCard(Transform parent, string name, float width, float height)
        {
            RectTransform rect = Rect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(width, height);
            var image = rect.gameObject.AddComponent<Image>(); image.color = Card;
            return rect;
        }
    }
}

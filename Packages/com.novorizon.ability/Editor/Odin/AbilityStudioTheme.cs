#if UNITY_EDITOR && ODIN_INSPECTOR
using UnityEditor;
using UnityEngine;

namespace Game.Ability.Editor.Odin
{
    internal sealed class AbilityStudioTheme
    {
        public readonly bool Dark = EditorGUIUtility.isProSkin;
        public readonly Color Background, Surface, Raised, Border, Text, Muted, Accent, Purple, Warning, Error;
        public readonly GUIStyle Title, Heading, Body, Small, Eyebrow, Code, Button, PrimaryButton, Card, Search, TreeRow, TreeSelected, Tab, ActiveTab;
        private readonly Texture2D cardTexture, selectedTexture, primaryTexture;

        public AbilityStudioTheme()
        {
            Background = Hex(Dark ? 0x121823 : 0xEFF2F7);
            Surface = Hex(Dark ? 0x1A2230 : 0xFFFFFF);
            Raised = Hex(Dark ? 0x222D3E : 0xE6ECF5);
            Border = Hex(Dark ? 0x303C50 : 0xD6DFEC);
            Text = Hex(Dark ? 0xE2E9F3 : 0x24344B);
            Muted = Hex(Dark ? 0x9BAAC0 : 0x60718A);
            Accent = Hex(Dark ? 0x53D9BC : 0x087F71);
            Purple = Hex(Dark ? 0xB2A5FF : 0x7255B0);
            Warning = Hex(Dark ? 0xF2C07C : 0xA66914);
            Error = Hex(Dark ? 0xF18C9E : 0xBC3553);
            cardTexture = Rounded(Surface);
            selectedTexture = Rounded(Dark ? Hex(0x273C46) : Hex(0xD9F0E9));
            primaryTexture = Rounded(Dark ? Hex(0x276F64) : Hex(0xC2EBDD));
            Title = Label(21, Text, FontStyle.Bold);
            Heading = Label(13, Text, FontStyle.Bold);
            Body = Label(12, Text);
            Small = Label(10, Muted);
            Small.wordWrap = true;
            Eyebrow = Label(9, Muted, FontStyle.Bold);
            Code = Label(11, Text);
            Code.wordWrap = false;
            Code.richText = false;
            Card = new GUIStyle { normal = { background = cardTexture }, border = new RectOffset(8, 8, 8, 8), padding = new RectOffset(14, 14, 12, 12), margin = new RectOffset(0, 0, 0, 10) };
            Button = new GUIStyle(GUI.skin.button) { fontSize = 11, fixedHeight = 28, padding = new RectOffset(12, 12, 3, 3) };
            Button.normal.textColor = Text;
            PrimaryButton = new GUIStyle(Button);
            PrimaryButton.normal.background = primaryTexture;
            PrimaryButton.normal.textColor = Dark ? Color.white : Text;
            PrimaryButton.border = new RectOffset(8, 8, 8, 8);
            Search = new GUIStyle(EditorStyles.toolbarSearchField) { fixedHeight = 24, margin = new RectOffset(0, 0, 5, 8) };
            TreeRow = Label(11, Text);
            TreeRow.padding = new RectOffset(9, 8, 0, 0);
            TreeRow.alignment = TextAnchor.MiddleLeft;
            TreeSelected = new GUIStyle(TreeRow);
            TreeSelected.normal.background = selectedTexture;
            TreeSelected.border = new RectOffset(8, 8, 8, 8);
            TreeSelected.normal.textColor = Accent;
            Tab = new GUIStyle(Button) { fixedHeight = 29 };
            ActiveTab = new GUIStyle(Tab);
            ActiveTab.normal.textColor = Accent;
            ActiveTab.normal.background = selectedTexture;
            ActiveTab.border = new RectOffset(8, 8, 8, 8);
        }

        public void Dispose()
        {
            Object.DestroyImmediate(cardTexture);
            Object.DestroyImmediate(selectedTexture);
            Object.DestroyImmediate(primaryTexture);
        }

        public void Rule()
        {
            Rect rect = GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, Border);
        }

        public void Badge(string text, Color color, float width = 70)
        {
            Rect rect = GUILayoutUtility.GetRect(width, 22, GUILayout.Width(width));
            Color background = color;
            background.a = Dark ? 0.14f : 0.10f;
            EditorGUI.DrawRect(rect, background);
            GUIStyle style = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            style.normal.textColor = color;
            GUI.Label(rect, text, style);
        }

        private static GUIStyle Label(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            GUIStyle style = new GUIStyle(EditorStyles.label) { fontSize = size, fontStyle = weight, richText = false, clipping = TextClipping.Clip };
            style.normal.textColor = color;
            return style;
        }

        private static Color Hex(int value) => new Color((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f, 1f);

        private static Texture2D Rounded(Color color)
        {
            const int size = 24;
            const float radius = 7f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius));
                    float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius));
                    Color pixel = color;
                    pixel.a *= 1f - Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(0f, dx) * Mathf.Max(0f, dx) + Mathf.Max(0f, dy) * Mathf.Max(0f, dy)) - radius + 0.5f);
                    pixels[y * size + x] = pixel;
                }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
#endif

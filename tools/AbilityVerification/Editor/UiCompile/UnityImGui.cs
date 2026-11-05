using System;
namespace UnityEngine
{
    public sealed class GUIContent { public string text, tooltip; public Texture image; public GUIContent() { } public GUIContent(string text) { this.text = text; } public GUIContent(string text, string tooltip) { this.text = text; this.tooltip = tooltip; } public GUIContent(string text, Texture image) { this.text = text; this.image = image; } public static GUIContent none => new GUIContent(); }
    public sealed class GUILayoutOption { }
    public class GUIStyleState { public Color textColor; public Texture2D background; }
    public class GUIStyle
    {
        public string name; public int fontSize; public FontStyle fontStyle; public TextAnchor alignment; public TextClipping clipping; public bool wordWrap, richText, stretchHeight, stretchWidth; public float fixedHeight, fixedWidth; public RectOffset padding = new RectOffset(), margin = new RectOffset(), border = new RectOffset(); public GUIStyleState normal = new GUIStyleState(), hover = new GUIStyleState(), active = new GUIStyleState(), focused = new GUIStyleState(), onNormal = new GUIStyleState();
        public GUIStyle() { } public GUIStyle(GUIStyle other) { } public GUIStyle(string name) { this.name = name; } public Vector2 CalcSize(GUIContent content) => new Vector2(); public float CalcHeight(GUIContent content, float width) => 18; public static GUIStyle none => new GUIStyle();
    }
    public enum TextClipping { Overflow, Clip }
    public class GUISkin { public GUIStyle label = new GUIStyle(), button = new GUIStyle(), box = new GUIStyle(); }
    public static class GUI
    {
        public static bool changed, enabled = true; public static Color color, backgroundColor, contentColor; public static GUISkin skin = new GUISkin();
        public static void Label(Rect rect, string text, GUIStyle style = null) { } public static void Label(Rect rect, GUIContent text, GUIStyle style = null) { } public static bool Button(Rect rect, string text, GUIStyle style = null) => false; public static bool Button(Rect rect, GUIContent content, GUIStyle style = null) => false; public static void Box(Rect rect, string text, GUIStyle style = null) { } public static void DrawTexture(Rect rect, Texture image) { } public static void FocusControl(string name) { } public static void SetNextControlName(string name) { } public static void BeginGroup(Rect rect) { } public static void EndGroup() { }
    }
    public static class GUILayout
    {
        public static void BeginHorizontal(params GUILayoutOption[] options) { } public static void BeginHorizontal(GUIStyle style, params GUILayoutOption[] options) { } public static void EndHorizontal() { } public static void BeginVertical(params GUILayoutOption[] options) { } public static void BeginVertical(GUIStyle style, params GUILayoutOption[] options) { } public static void EndVertical() { }
        public static void Label(string text, params GUILayoutOption[] options) { } public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { } public static void Label(GUIContent content, GUIStyle style, params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) => false; public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) => false; public static bool Button(GUIContent text, GUIStyle style, params GUILayoutOption[] options) => false;
        public static bool Toggle(bool value, string text, GUIStyle style, params GUILayoutOption[] options) => value; public static bool Toggle(bool value, string text, params GUILayoutOption[] options) => value;
        public static int Toolbar(int value, string[] texts, GUIStyle style, params GUILayoutOption[] options) => value; public static int Toolbar(int value, string[] texts, params GUILayoutOption[] options) => value;
        public static void FlexibleSpace() { } public static void Space(float pixels) { } public static string TextField(string value, params GUILayoutOption[] options) => value; public static Vector2 BeginScrollView(Vector2 scroll, params GUILayoutOption[] options) => scroll; public static Vector2 BeginScrollView(Vector2 scroll, GUIStyle style, params GUILayoutOption[] options) => scroll; public static void EndScrollView() { } public static void BeginArea(Rect area) { } public static void EndArea() { }
        public static GUILayoutOption Width(float value) => new GUILayoutOption(); public static GUILayoutOption Height(float value) => new GUILayoutOption(); public static GUILayoutOption MinWidth(float value) => new GUILayoutOption(); public static GUILayoutOption MaxWidth(float value) => new GUILayoutOption(); public static GUILayoutOption MinHeight(float value) => new GUILayoutOption(); public static GUILayoutOption MaxHeight(float value) => new GUILayoutOption(); public static GUILayoutOption ExpandWidth(bool value) => new GUILayoutOption(); public static GUILayoutOption ExpandHeight(bool value) => new GUILayoutOption();
    }
    public static class GUILayoutUtility { public static Rect GetRect(float width, float height, params GUILayoutOption[] options) => new Rect(); public static Rect GetRect(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => new Rect(); public static Rect GetLastRect() => new Rect(); }
    public enum EventType { MouseDown, MouseUp, MouseDrag, KeyDown, Repaint, Layout, Used }
    public enum KeyCode { None, S, F, Return, Escape, Delete }
    public class Event { public static Event current = new Event(); public EventType type; public int button; public bool control, command, shift, alt; public KeyCode keyCode; public Vector2 mousePosition; public void Use() { } }
    public static class GUIUtility { public static int hotControl; public static string systemCopyBuffer; public static void ExitGUI() { } }
}

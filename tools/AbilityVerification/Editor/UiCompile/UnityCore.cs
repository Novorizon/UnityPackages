// Compile-only API substitutes. These are not Unity binaries and must never enter Assets or a Player.
using System;
using System.Collections.Generic;
using System.Text.Json;
namespace UnityEngine
{
    public class Object { public string name; public HideFlags hideFlags; public static void DestroyImmediate(Object value) { } public static implicit operator bool(Object value) => value != null; }
    public class ScriptableObject : Object { public static T CreateInstance<T>() where T : ScriptableObject, new() => new T(); public static ScriptableObject CreateInstance(Type type) => (ScriptableObject)Activator.CreateInstance(type); }
    public class PropertyAttribute : Attribute { }
    public sealed class SerializeField : Attribute { }
    public sealed class HideInInspector : Attribute { }
    public sealed class TextAreaAttribute : Attribute { public TextAreaAttribute(int minLines, int maxLines) { } }
    [Flags] public enum HideFlags { None = 0, HideAndDontSave = 61, DontSave = 52 }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 zero => new Vector2(); }
    public struct Vector3
    {
        public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(); public static Vector3 right => new Vector3(1, 0, 0); public static Vector3 left => new Vector3(-1, 0, 0); public static Vector3 forward => new Vector3(0, 0, 1);
        public float sqrMagnitude => x * x + y * y + z * z; public float magnitude => MathF.Sqrt(sqrMagnitude); public Vector3 normalized => magnitude > 0 ? this * (1f / magnitude) : zero;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b); public static Vector3 operator /(Vector3 a, float b) => a * (1f / b);
    }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white => new Color(1, 1, 1); public static Color black => new Color(0, 0, 0); public static Color clear => new Color(0, 0, 0, 0); public static Color Lerp(Color a, Color b, float t) => a; }
    public struct Rect
    {
        public float x, y, width, height; public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float xMin { get => x; set => x = value; } public float xMax { get => x + width; set => width = value - x; } public float yMin { get => y; set => y = value; } public float yMax { get => y + height; set => height = value - y; }
        public Vector2 position { get => new Vector2(x, y); set { x = value.x; y = value.y; } } public Vector2 size { get => new Vector2(width, height); set { width = value.x; height = value.y; } } public bool Contains(Vector2 point) => false;
    }
    public class RectOffset { public int left, right, top, bottom; public RectOffset() { } public RectOffset(int left, int right, int top, int bottom) { this.left = left; this.right = right; this.top = top; this.bottom = bottom; } }
    public class Texture : Object { }
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public class Texture2D : Texture { public FilterMode filterMode; public Texture2D(int width, int height) { } public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { } public void SetPixel(int x, int y, Color color) { } public void SetPixels(Color[] colors) { } public void Apply() { } public static Texture2D whiteTexture => new Texture2D(1, 1); }
    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public static class Mathf { public static float Max(float a, float b) => MathF.Max(a, b); public static int Max(int a, int b) => Math.Max(a, b); public static float Min(float a, float b) => MathF.Min(a, b); public static int Min(int a, int b) => Math.Min(a, b); public static int Clamp(int v, int a, int b) => Math.Clamp(v, a, b); public static float Clamp(float v, float a, float b) => Math.Clamp(v, a, b); public static float Clamp01(float v) => Math.Clamp(v, 0, 1); public static int RoundToInt(float v) => (int)MathF.Round(v); public static int CeilToInt(float v) => (int)MathF.Ceiling(v); public static float Abs(float v) => MathF.Abs(v); public static float Sqrt(float v) => MathF.Sqrt(v); }
    public static class JsonUtility { public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { IncludeFields = true }); public static string ToJson(object obj, bool prettyPrint = false) => JsonSerializer.Serialize(obj, new JsonSerializerOptions { IncludeFields = true }); }
    public static class Application { public static string dataPath => "/Assets"; public static string persistentDataPath => "/tmp"; public static string unityVersion => "compile-only"; public static void OpenURL(string url) { } }
    public static class Debug { public static void Log(object value) { } public static void LogWarning(object value) { } public static void LogError(object value) { } public static void LogException(Exception value) { } }
}
namespace UnityEngine.Pool { public static class ListPool<T> { public static List<T> Get() => new List<T>(); public static void Release(List<T> value) => value.Clear(); } }

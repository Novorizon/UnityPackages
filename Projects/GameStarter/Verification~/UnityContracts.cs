// Test-only API contracts. No native Unity import, render, IL2CPP or destruction semantics.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace UnityEngine
{
    public class Object
    {
        internal static readonly List<Object> Objects = new List<Object>();
        internal bool dead;
        public string name;
        public Object() { Objects.Add(this); }
        public static T[] FindObjectsOfType<T>(bool includeInactive = false) where T : Object => Objects.OfType<T>()
            .Where(o => !o.dead && (includeInactive || !(o is Component c) || c.gameObject.activeInHierarchy)).ToArray();
        public static void DontDestroyOnLoad(Object target) { }
        public static void Destroy(Object target)
        {
            if (ReferenceEquals(target, null) || target.dead) return;
            target.dead = true;
            if (target is GameObject go) foreach (var c in go.Components.ToArray()) Destroy(c);
            else if (target is MonoBehaviour) Invoke(target, "OnDestroy");
        }
        public static void Invoke(Object target, string method) => target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(target, null);
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) || ((ReferenceEquals(a, null) || a.dead) && (ReferenceEquals(b, null) || b.dead));
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object obj) => this == obj as Object;
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void ClearForTest() { foreach (var go in FindObjectsOfType<GameObject>(true)) Destroy(go); Objects.Clear(); }
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }
    public class MonoBehaviour : Component { }
    public class Transform : Component { public Transform parent; }
    public sealed class GameObject : Object
    {
        public readonly List<Component> Components = new List<Component>();
        public Transform transform;
        public bool activeInHierarchy = true;
        public GameObject(string name = "")
        {
            this.name = name;
            transform = new Transform { gameObject = this };
            Components.Add(transform);
        }
        public T AddComponent<T>() where T : Component
        {
            var c = (T)Activator.CreateInstance(typeof(T));
            c.gameObject = this;
            Components.Add(c);
            if (activeInHierarchy) Invoke(c, "Awake");
            return c;
        }
    }
    public static class Debug
    {
        public static readonly List<Exception> Exceptions = new List<Exception>();
        public static readonly List<string> Messages = new List<string>();
        public static void Log(object value) { Messages.Add(value?.ToString()); }
        public static void LogException(Exception value) { Exceptions.Add(value); }
    }
    public static class Time { public static float deltaTime = .016f, unscaledDeltaTime = .016f; }
    public struct Vector2 { }
    public sealed class GUIStyle { }
    public static class GUILayout { public static bool Button(string label) => false; }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    public enum RuntimeInitializeLoadType { SubsystemRegistration, BeforeSceneLoad, AfterSceneLoad }
    [AttributeUsage(AttributeTargets.Method)] public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { } }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
        public static string ToJson(object target) => JsonSerializer.Serialize(target, Options);
    }
}

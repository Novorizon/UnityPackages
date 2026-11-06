// Test-only Unity API contracts and a deterministic lifecycle simulator.
// This is not Unity: native destruction timing, rendering, serialization, coroutines,
// asset I/O, editor import and IL2CPP must be verified in Unity 2022.3 separately.
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

[assembly: InternalsVisibleTo("Game.Framework.UI.Tests")]
namespace UnityEngine
{
    public partial class Object
    {
        private static readonly List<Object> objects = new List<Object>();
        private static readonly List<Object> pendingDestruction = new List<Object>();
        public static bool DeferGameObjectDestructionForTest { get; set; }

        internal bool Destroying;
        public bool DestroyedForTest { get; private set; }

        public string name;
        protected Object()
        {
            objects.Add(this);
        }

        public static T FindFirstObjectByType<T>()
            where T : Object => FindObjectsOfType<T>().FirstOrDefault();
        public static T[] FindObjectsOfType<T>(bool includeInactive = false)
            where T : Object => objects.OfType<T>().Where(value => !value.DestroyedForTest && !value.Destroying && (includeInactive || !(value is Component component) || component.gameObject.activeInHierarchy)).ToArray();
        public bool PersistentForTest { get; private set; }

        public static void DontDestroyOnLoad(Object value)
        {
            var go = value as GameObject ?? (value as Component)?.gameObject;
            if (go != null && go.transform.parent == null)
            {
                go.PersistentForTest = true;
            }
        }

        public static void Destroy(Object value)
        {
            if (ReferenceEquals(value, null) || value.DestroyedForTest || value.Destroying)
            {
                return;
            }

            if (DeferGameObjectDestructionForTest && value is GameObject)
            {
                if (!pendingDestruction.Contains(value))
                {
                    pendingDestruction.Add(value);
                }

                return;
            }

            value.Destroying = true;
            if (value is GameObject gameObject)
            {
                gameObject.SetActive(false);
                foreach (var child in gameObject.transform.Children.ToArray())
                {
                    Destroy(child.gameObject);
                }

                foreach (var component in gameObject.Components.ToArray())
                {
                    Destroy(component);
                }

                gameObject.transform.SetParent(null, false);
            }

            if (value is MonoBehaviour behaviour)
            {
                GameObject.Deactivate(behaviour);
                behaviour.StopAllCoroutines();
            }

            // Unity only sends OnDestroy to MonoBehaviours whose GameObject was active.
            try
            {
                if (!(value is MonoBehaviour component) || component.Awoken)
                {
                    InvokeLifecycle(value, "OnDestroy");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                value.DestroyedForTest = true;
            }
        }

        internal static void InvokeLifecycle(Object target, string method)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                var callback = type.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (callback == null)
                {
                    continue;
                }

                try
                {
                    callback.Invoke(target, null);
                }
                catch (TargetInvocationException exception)when (exception.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                }

                return;
            }
        }

        public static bool operator ==(Object left, Object right) => ReferenceEquals(left, right) || ((ReferenceEquals(left, null) || left.DestroyedForTest) && (ReferenceEquals(right, null) || right.DestroyedForTest));
        public static bool operator !=(Object left, Object right) => !(left == right);
        public static implicit operator bool (Object value) => value != null;
        public override bool Equals(object value) => this == value as Object;
        public override int GetHashCode() => base.GetHashCode();
        public static T Instantiate<T>(T value, Transform parent)
            where T : Object => Instantiate(value, parent, false);
        public static T Instantiate<T>(T value, Transform parent, bool worldPositionStays)
            where T : Object
        {
            if (!(value is GameObject source))
            {
                throw new NotSupportedException("Only GameObject instantiation is simulated.");
            }

            var clone = new GameObject(source.name + "(Clone)");
            clone.SetActive(false);
            if (source.transform is RectTransform sourceRect)
            {
                var targetRect = clone.AddComponent<RectTransform>();
                targetRect.anchorMin = sourceRect.anchorMin;
                targetRect.anchorMax = sourceRect.anchorMax;
                targetRect.pivot = sourceRect.pivot;
                targetRect.sizeDelta = sourceRect.sizeDelta;
                targetRect.anchoredPosition = sourceRect.anchoredPosition;
            }
            else
            {
                clone.transform.localPosition = source.transform.localPosition;
            }

            clone.transform.localScale = source.transform.localScale;
            clone.transform.SetParent(parent, worldPositionStays);
            foreach (var component in source.Components.Where(component => !(component is Transform)))
            {
                var copy = clone.AddComponent(component.GetType());
                foreach (var field in component.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    field.SetValue(copy, field.GetValue(component));
                }
            }

            clone.SetActive(source.activeSelf);
            return (T)(Object)clone;
        }

        public static void FlushDestroyQueueForTest()
        {
            bool deferred = DeferGameObjectDestructionForTest;
            DeferGameObjectDestructionForTest = false;
            foreach (var value in pendingDestruction.ToArray())
            {
                pendingDestruction.Remove(value);
                Destroy(value);
            }

            DeferGameObjectDestructionForTest = deferred;
        }

        public static void MarkQuittingForTest()
        {
            var state = typeof(Game.Framework.Singleton<>).Assembly.GetType("Game.Framework.SingletonRuntimeState")!;
            state.GetField("IsQuitting", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        }

        public static void ResetSingletonsForPlay()
        {
            var state = typeof(Game.Framework.Singleton<>).Assembly.GetType("Game.Framework.SingletonRuntimeState")!;
            state.GetField("IsQuitting", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, false);
            var epoch = state.GetField("Epoch", BindingFlags.Static | BindingFlags.NonPublic)!;
            epoch.SetValue(null, (int)epoch.GetValue(null)! + 1);
#if UNITY_EDITOR
            Game.Framework.EditorStaticReset.RestoreDeclaredValues();
#endif
        }

        public static void ResetTestState()
        {
            Game.Framework.UnityThread.Reset();
            Game.Framework.UnityThread.InitializeManual();
            ResetSingletonsForPlay();
            DeferGameObjectDestructionForTest = false;
            FlushDestroyQueueForTest();
            foreach (var value in objects.OfType<GameObject>().ToArray())
            {
                Destroy(value);
            }

            objects.Clear();
            Resources.ResetTestState();
            Debug.Exceptions.Clear();
            Application.ResetForTest();
            UnityEngine.SceneManagement.SceneManager.ResetForTest();
            Camera.main = null;
            Screen.ResetForTest();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies().Where(value => value.GetName().Name?.StartsWith("Game.Framework", StringComparison.Ordinal) == true))
            {
                foreach (var type in assembly.GetTypes())
                {
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>()?.LoadType == RuntimeInitializeLoadType.SubsystemRegistration)
                        {
                            method.Invoke(null, null);
                        }
                    }
                }
            }
        }

        public static void AdvanceFrameForTest()
        {
            foreach (string message in new[]
            {
                "Update",
                "LateUpdate"
            }

            )
            {
                foreach (var behaviour in objects.OfType<MonoBehaviour>().ToArray())
                {
                    if (!behaviour.DestroyedForTest && !behaviour.Destroying && behaviour.isActiveAndEnabled)
                    {
                        InvokeLifecycle(behaviour, message);
                    }
                }
            }
        }

        public static void AdvanceCoroutinesForTest(float elapsed)
        {
            foreach (var behaviour in objects.OfType<MonoBehaviour>().Where(x => !x.DestroyedForTest && x.gameObject.activeInHierarchy).ToArray())
            {
                behaviour.AdvanceCoroutines(elapsed);
            }
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;

        public T GetComponent<T>()
            where T : class => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>(bool includeInactive = false)
            where T : class => gameObject.GetComponentInChildren<T>(includeInactive);
        public T GetComponentInParent<T>(bool includeInactive = false)
            where T : class
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (!includeInactive && !current.gameObject.activeInHierarchy)
                {
                    continue;
                }

                T component = current.gameObject.GetComponent<T>();
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }
    }

    public partial class Transform : Component
    {
        private Transform parentValue;
        internal readonly List<Transform> Children = new List<Transform>();
        private Vector3 localPositionValue;
        public virtual Vector3 localPosition { get => localPositionValue; set => localPositionValue = value; }
        public Vector3 localScale { get; set; } = Vector3.one;
        public Vector3 lossyScale => parent == null ? localScale : Vector3.Scale(parent.lossyScale, localScale);
        public Vector3 position { get => parent == null ? localPosition : parent.TransformPoint(localPosition); set => localPosition = parent == null ? value : parent.InverseTransformPoint(value); }
        public Matrix4x4 localToWorldMatrix => Matrix4x4.TRS(position, lossyScale);

        public Vector3 TransformPoint(Vector3 point) => position + Vector3.Scale(point, lossyScale);
        public Vector3 InverseTransformPoint(Vector3 point)
        {
            Vector3 relative = point - position, scale = lossyScale;
            return new Vector3(relative.x / scale.x, relative.y / scale.y, relative.z / scale.z);
        }

        public Transform parent { get => parentValue; set => SetParent(value, false); }
        public int childCount => Children.Count;

        public Transform GetChild(int index) => Children[index];
        public Transform Find(string name) => Children.FirstOrDefault(child => child.gameObject.name == name);
        internal void SetParentRaw(Transform parent)
        {
            parentValue?.Children.Remove(this);
            parentValue = parent;
            parent?.Children.Add(this);
        }

        public void SetParent(Transform parent, bool worldPositionStays = true)
        {
            if (ReferenceEquals(parentValue, parent))
            {
                return;
            }

            Vector3 worldPosition = position, worldScale = lossyScale;
            var before = gameObject.CaptureHierarchyState();
            SetParentRaw(parent);
            if (worldPositionStays)
            {
                position = worldPosition;
                Vector3 parentScale = parent == null ? Vector3.one : parent.lossyScale;
                localScale = new Vector3(worldScale.x / parentScale.x, worldScale.y / parentScale.y, worldScale.z / parentScale.z);
            }

            foreach (var behaviour in gameObject.Components.OfType<MonoBehaviour>().ToArray())
            {
                if (!behaviour.DestroyedForTest && !behaviour.Destroying)
                {
                    InvokeLifecycle(behaviour, "OnTransformParentChanged");
                }
            }

            GameObject.ApplyHierarchyState(before);
        }

        public int GetSiblingIndex() => parentValue?.Children.IndexOf(this) ?? 0;
        public void SetSiblingIndex(int index)
        {
            if (parentValue == null)
            {
                return;
            }

            parentValue.Children.Remove(this);
            parentValue.Children.Insert(Math.Max(0, Math.Min(index, parentValue.Children.Count)), this);
        }

        public void SetAsLastSibling() => SetSiblingIndex(parentValue?.Children.Count ?? 0);
    }

    // Axis-aligned RectTransform geometry only. No driven layout, rotation or CanvasScaler simulation.
    public class RectTransform : Transform
    {
        public enum Axis
        {
            Horizontal,
            Vertical
        }

        private Vector2 anchorMinValue = new Vector2(.5f, .5f), anchorMaxValue = new Vector2(.5f, .5f);
        public int LayoutWriteCountForTest { get; private set; }

        public Vector2 anchorMin
        {
            get => anchorMinValue;
            set
            {
                anchorMinValue = value;
                LayoutWriteCountForTest++;
            }
        }

        public Vector2 anchorMax
        {
            get => anchorMaxValue;
            set
            {
                anchorMaxValue = value;
                LayoutWriteCountForTest++;
            }
        }

        public Vector2 pivot { get; set; } = new Vector2(.5f, .5f);
        public Vector2 sizeDelta { get; set; } = new Vector2(100, 100);
        public Vector2 anchoredPosition { get; set; }

        public Rect rect
        {
            get
            {
                Vector2 size = sizeDelta;
                if (parent is RectTransform parentRect)
                {
                    Rect bounds = parentRect.rect;
                    size.x += bounds.width * (anchorMax.x - anchorMin.x);
                    size.y += bounds.height * (anchorMax.y - anchorMin.y);
                }

                return new Rect(-pivot.x * size.x, -pivot.y * size.y, size.x, size.y);
            }
        }

        private Vector2 AnchorReference
        {
            get
            {
                if (!(parent is RectTransform parentRect))
                {
                    return Vector2.zero;
                }

                Rect bounds = parentRect.rect;
                return new Vector2(bounds.xMin + bounds.width * (anchorMin.x + (anchorMax.x - anchorMin.x) * pivot.x), bounds.yMin + bounds.height * (anchorMin.y + (anchorMax.y - anchorMin.y) * pivot.y));
            }
        }

        public override Vector3 localPosition
        {
            get
            {
                Vector2 origin = AnchorReference;
                return new Vector3(origin.x + anchoredPosition.x, origin.y + anchoredPosition.y, base.localPosition.z);
            }

            set
            {
                Vector2 origin = AnchorReference;
                anchoredPosition = new Vector2(value.x - origin.x, value.y - origin.y);
                base.localPosition = value;
            }
        }

        public Vector2 offsetMin
        {
            get => anchoredPosition - Vector2.Scale(sizeDelta, pivot);
            set
            {
                LayoutWriteCountForTest++;
                Vector2 delta = value - offsetMin;
                sizeDelta -= delta;
                anchoredPosition += Vector2.Scale(delta, Vector2.one - pivot);
            }
        }

        public Vector2 offsetMax
        {
            get => anchoredPosition + Vector2.Scale(sizeDelta, Vector2.one - pivot);
            set
            {
                LayoutWriteCountForTest++;
                Vector2 delta = value - offsetMax;
                sizeDelta += delta;
                anchoredPosition += Vector2.Scale(delta, pivot);
            }
        }

        public void SetSizeWithCurrentAnchors(Axis axis, float size)
        {
            Vector2 value = sizeDelta;
            Rect bounds = parent is RectTransform parentRect ? parentRect.rect : default;
            if (axis == Axis.Horizontal)
            {
                value.x = size - bounds.width * (anchorMax.x - anchorMin.x);
            }
            else
            {
                value.y = size - bounds.height * (anchorMax.y - anchorMin.y);
            }

            sizeDelta = value;
        }
    }

    public class GameObject : Object
    {
        public Transform transform { get; private set; }

        internal readonly List<Component> Components = new List<Component>();
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);

        public GameObject(string name = "", params Type[] components)
        {
            this.name = name;
            transform = new Transform
            {
                gameObject = this
            };
            Components.Add(transform);
            foreach (var type in components)
            {
                AddComponent(type);
            }
        }

        public Component AddComponent(Type type)
        {
            foreach (RequireComponent requirement in type.GetCustomAttributes(typeof(RequireComponent), true))
            {
                foreach (Type required in requirement.Types)
                {
                    if (GetComponent(required) == null)
                    {
                        AddComponent(required);
                    }
                }
            }

            var component = (Component)Activator.CreateInstance(type, true);
            component.gameObject = this;
            if (component is Transform replacement)
            {
                var parent = transform.parent;
                var children = transform.Children.ToArray();
                Vector3 oldPosition = transform.localPosition, oldScale = transform.localScale;
                transform.SetParentRaw(null);
                Components.Remove(transform);
                transform = replacement;
                replacement.SetParentRaw(parent);
                replacement.localPosition = oldPosition;
                replacement.localScale = oldScale;
                foreach (var child in children)
                {
                    child.SetParentRaw(replacement);
                }
            }

            Components.Add(component);
            if (activeInHierarchy && component is MonoBehaviour behaviour)
            {
                Activate(behaviour);
            }

            return component;
        }

        internal static void Activate(MonoBehaviour value)
        {
            if (value.DestroyedForTest || value.Destroying || !value.gameObject.activeInHierarchy)
            {
                return;
            }

            if (!value.Awoken)
            {
                value.Awoken = true;
                InvokeLifecycle(value, "Awake");
            }

            if (!value.DestroyedForTest && !value.Destroying && value.isActiveAndEnabled && !value.EnableNotified)
            {
                value.EnableNotified = true;
                InvokeLifecycle(value, "OnEnable");
            }
        }

        internal static void Deactivate(MonoBehaviour value)
        {
            if (!value.EnableNotified)
            {
                return;
            }

            value.EnableNotified = false;
            InvokeLifecycle(value, "OnDisable");
        }

        internal List<(GameObject Object, bool Active)> CaptureHierarchyState()
        {
            var states = new List<(GameObject, bool)>();
            CaptureHierarchyState(states);
            return states;
        }

        private void CaptureHierarchyState(List<(GameObject, bool)> states)
        {
            states.Add((this, activeInHierarchy));
            foreach (var child in transform.Children.ToArray())
            {
                child.gameObject.CaptureHierarchyState(states);
            }
        }

        internal static void ApplyHierarchyState(List<(GameObject Object, bool Active)> states)
        {
            foreach (var state in states)
            {
                var go = state.Object;
                if (go.DestroyedForTest)
                {
                    continue;
                }

                bool after = go.activeInHierarchy;
                if (go.Destroying && after)
                {
                    continue;
                }

                if (state.Active == after)
                {
                    continue;
                }

                foreach (var behaviour in go.Components.OfType<MonoBehaviour>().ToArray())
                {
                    if (after)
                    {
                        Activate(behaviour);
                    }
                    else
                    {
                        behaviour.StopAllCoroutines();
                        Deactivate(behaviour);
                    }
                }
            }
        }

        public T AddComponent<T>()
            where T : Component => (T)AddComponent(typeof(T));
        public T GetComponent<T>()
            where T : class => Components.OfType<T>().FirstOrDefault();
        public Component GetComponent(Type type) => Components.FirstOrDefault(type.IsInstanceOfType);
        public T GetComponentInChildren<T>(bool includeInactive = false)
            where T : class => GetComponentsInChildren<T>(includeInactive).FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive = false)
            where T : class
        {
            var result = new List<T>();
            if (includeInactive || activeInHierarchy)
            {
                result.AddRange(Components.OfType<T>());
            }

            foreach (var child in transform.Children)
            {
                result.AddRange(child.gameObject.GetComponentsInChildren<T>(includeInactive));
            }

            return result.ToArray();
        }

        public void SetActive(bool value)
        {
            if (activeSelf == value)
            {
                return;
            }

            var before = CaptureHierarchyState();
            activeSelf = value;
            ApplyHierarchyState(before);
        }
    }

    public class MonoBehaviour : Component
    {
        internal bool Awoken;
        private readonly List<Coroutine> coroutines = new List<Coroutine>();
        internal bool EnableNotified;
        private bool enabledValue = true;
        public bool enabled
        {
            get => enabledValue;
            set
            {
                if (enabledValue == value)
                {
                    return;
                }

                enabledValue = value;
                if (!Awoken || gameObject == null || !gameObject.activeInHierarchy)
                {
                    return;
                }

                if (value)
                {
                    GameObject.Activate(this);
                }
                else
                {
                    GameObject.Deactivate(this);
                }
            }
        }

        public bool isActiveAndEnabled => enabled && gameObject != null && gameObject.activeInHierarchy && !DestroyedForTest && !Destroying;

        public Coroutine StartCoroutine(IEnumerator routine)
        {
            var coroutine = new Coroutine(routine);
            if (routine.MoveNext())
            {
                coroutine.Remaining = (routine.Current as WaitForSeconds)?.Seconds ?? 0;
                coroutines.Add(coroutine);
            }

            return coroutine;
        }

        public void StopCoroutine(Coroutine coroutine)
        {
            coroutines.Remove(coroutine);
        }

        public void StopAllCoroutines()
        {
            coroutines.Clear();
        }

        internal void AdvanceCoroutines(float elapsed)
        {
            foreach (var coroutine in coroutines.ToArray())
            {
                if (!coroutines.Contains(coroutine))
                {
                    continue;
                }

                coroutine.Remaining -= elapsed;
                if (coroutine.Remaining > 0)
                {
                    continue;
                }

                if (!coroutine.Routine.MoveNext())
                {
                    coroutines.Remove(coroutine);
                }
                else
                {
                    coroutine.Remaining = (coroutine.Routine.Current as WaitForSeconds)?.Seconds ?? 0;
                }
            }
        }
    }

    public sealed class Coroutine
    {
        internal readonly IEnumerator Routine;
        internal float Remaining;
        internal Coroutine(IEnumerator routine)
        {
            Routine = routine;
        }
    }

    public sealed class WaitForSeconds
    {
        internal readonly float Seconds;
        public WaitForSeconds(float seconds)
        {
            Seconds = seconds;
        }
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>()
            where T : ScriptableObject, new() => new T();
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 zero => default;
        public static Vector2 one => new Vector2(1, 1);

        public static Vector2 Scale(Vector2 a, Vector2 b) => new Vector2(a.x * b.x, a.y * b.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator *(Vector2 value, float scale) => new Vector2(value.x * scale, value.y * scale);
        public static Vector2 operator /(Vector2 value, float scale) => new Vector2(value.x / scale, value.y / scale);
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public float sqrMagnitude => x * x + y * y;

        public override bool Equals(object other) => other is Vector2 value && x.Equals(value.x) && y.Equals(value.y);
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z = 0)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 zero => default;
        public static Vector3 one => new Vector3(1, 1, 1);

        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 value, float scale) => new Vector3(value.x * scale, value.y * scale, value.z * scale);
        public static implicit operator Vector3(Vector2 value) => new Vector3(value.x, value.y);
        public static explicit operator Vector2(Vector3 value) => new Vector2(value.x, value.y);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x;
            this.y = y;
            this.width = width;
            this.height = height;
        }

        public float xMin
        {
            get => x;
            set
            {
                float max = xMax;
                x = value;
                width = max - value;
            }
        }

        public float yMin
        {
            get => y;
            set
            {
                float max = yMax;
                y = value;
                height = max - value;
            }
        }

        public float xMax { get => x + width; set => width = value - x; }
        public float yMax { get => y + height; set => height = value - y; }

        public Vector2 min
        {
            get => new Vector2(xMin, yMin);
            set
            {
                xMin = value.x;
                yMin = value.y;
            }
        }

        public Vector2 max
        {
            get => new Vector2(xMax, yMax);
            set
            {
                xMax = value.x;
                yMax = value.y;
            }
        }

        public Vector2 size
        {
            get => new Vector2(width, height);
            set
            {
                width = value.x;
                height = value.y;
            }
        }

        public Vector2 center
        {
            get => new Vector2(x + width / 2, y + height / 2);
            set
            {
                x = value.x - width / 2;
                y = value.y - height / 2;
            }
        }

        public static bool operator ==(Rect a, Rect b) => a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
        public static bool operator !=(Rect a, Rect b) => !(a == b);
        public override bool Equals(object other) => other is Rect value && this == value;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (width.GetHashCode() >> 2) ^ (height.GetHashCode() >> 1);
    }

    // Matrix equality reflects the simulated 2D hierarchy transform; no 3D camera projection.
    public struct Matrix4x4
    {
        private System.Numerics.Matrix4x4 value;
        public static Matrix4x4 identity => new Matrix4x4
        {
            value = System.Numerics.Matrix4x4.Identity
        };

        internal static Matrix4x4 TRS(Vector3 position, Vector3 scale) => new Matrix4x4
        {
            value = System.Numerics.Matrix4x4.CreateScale(scale.x, scale.y, scale.z) * System.Numerics.Matrix4x4.CreateTranslation(position.x, position.y, position.z)
        };
        public static Matrix4x4 Translate(Vector3 position) => TRS(position, Vector3.one);
        public static Matrix4x4 Scale(Vector3 scale) => TRS(Vector3.zero, scale);
        public static bool operator ==(Matrix4x4 a, Matrix4x4 b) => a.value == b.value;
        public static bool operator !=(Matrix4x4 a, Matrix4x4 b) => !(a == b);
        public override bool Equals(object other) => other is Matrix4x4 matrix && this == matrix;
        public override int GetHashCode() => value.GetHashCode();
    }

    public static class Mathf
    {
        public static float Clamp01(float value) => Math.Max(0, Math.Min(1, value));
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
    }

    public static class Screen
    {
        private static Rect? safeAreaOverride;
        public static int width { get; private set; } = 1920;
        public static int height { get; private set; } = 1080;
        public static Rect safeArea => safeAreaOverride ?? new Rect(0, 0, width, height);

        public static void SetForTest(int screenWidth, int screenHeight, Rect safeArea)
        {
            width = screenWidth;
            height = screenHeight;
            safeAreaOverride = safeArea;
        }

        internal static void ResetForTest()
        {
            width = 1920;
            height = 1080;
            safeAreaOverride = null;
        }
    }

    public static class RectTransformUtility
    {
        // Pixel-aligned screen plane with translation/scale only. Tests must arrange a matching
        // canvas plane explicitly; perspective cameras, rotation, driven layout and native raycasts are not simulated.
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera camera, out Vector2 localPoint)
        {
            localPoint = default;
            if (rect == null || rect.lossyScale.x == 0 || rect.lossyScale.y == 0)
            {
                return false;
            }

            Vector3 point = rect.InverseTransformPoint(new Vector3(screenPoint.x, screenPoint.y, rect.position.z));
            localPoint = new Vector2(point.x, point.y);
            return true;
        }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white => new Color(1, 1, 1, 1);
    }

    public static partial class Application
    {
        public static bool isPlaying = true;
        public static event Action quitting;
        public static void QuitForTest()
        {
            Object.MarkQuittingForTest();
            quitting?.Invoke();
        }

        public static int QuittingSubscriberCountForTest => quitting?.GetInvocationList().Length ?? 0;

        internal static void ResetForTest()
        {
            quitting = null;
            isPlaying = true;
        }
    }

    public sealed class Font : Object
    {
    }

    public enum TextAnchor
    {
        UpperLeft,
        MiddleLeft,
        MiddleCenter
    }

    public class Camera : MonoBehaviour
    {
        public static Camera main;
        private Rect? pixelRectOverride;
        public Rect pixelRect { get => pixelRectOverride ?? new Rect(0, 0, Screen.width, Screen.height); set => pixelRectOverride = value; }
        public Matrix4x4 worldToCameraMatrix { get; set; } = Matrix4x4.identity;
        public Matrix4x4 projectionMatrix { get; set; } = Matrix4x4.identity;
    }

    public enum RenderMode
    {
        ScreenSpaceOverlay,
        ScreenSpaceCamera,
        WorldSpace
    }

    public class Canvas : MonoBehaviour
    {
        public RenderMode renderMode;
        public Camera worldCamera;
        public float planeDistance;
        public bool overrideSorting;
        public int sortingOrder;
        public float scaleFactor { get; set; } = 1;

        public Canvas rootCanvas
        {
            get
            {
                Canvas root = this;
                for (Transform current = transform.parent; current != null; current = current.parent)
                {
                    Canvas candidate = current.GetComponent<Canvas>();
                    if (candidate != null)
                    {
                        root = candidate;
                    }
                }

                return root;
            }
        }

        public Rect pixelRect
        {
            get
            {
                Canvas root = rootCanvas;
                return root.renderMode == RenderMode.ScreenSpaceCamera && root.worldCamera != null ? root.worldCamera.pixelRect : new Rect(0, 0, Screen.width, Screen.height);
            }
        }
    }

    public partial class AsyncOperation
    {
        public bool isDone { get; private set; }

        public event Action<AsyncOperation> completed;
        public void CompleteForTest()
        {
            if (isDone)
            {
                return;
            }

            isDone = true;
            completed?.Invoke(this);
        }
    }

    public class ResourceRequest : AsyncOperation
    {
        public Object asset;
    }

    public static partial class Resources
    {
        private static readonly Dictionary<string, Object> assets = new Dictionary<string, Object>();
        private static readonly List<(string path, ResourceRequest request)> pending = new List<(string, ResourceRequest)>();
        public static int SynchronousLoadCount { get; private set; }
        public static int AsyncLoadCount { get; private set; }
        public static bool CompleteSynchronouslyForTest { get; set; }

        public static T Load<T>(string path)
            where T : Object
        {
            SynchronousLoadCount++;
            return assets.TryGetValue(path, out var value) ? value as T : null;
        }

        public static T GetBuiltinResource<T>(string path)
            where T : Object
        {
            string key = "builtin:" + path;
            if (!assets.TryGetValue(key, out Object value))
            {
                if (typeof(T) != typeof(Font))
                {
                    return null;
                }

                value = new Font();
                assets[key] = value;
            }

            return value as T;
        }

        public static ResourceRequest LoadAsync<T>(string path)
            where T : Object
        {
            AsyncLoadCount++;
            var request = new ResourceRequest();
            pending.Add((path, request));
            if (CompleteSynchronouslyForTest)
            {
                CompletePendingForTest();
            }

            return request;
        }

        public static void RegisterAssetForTest(string path, Object value)
        {
            assets[path] = value;
        }

        public static void CompletePendingForTest()
        {
            foreach (var item in pending.ToArray())
            {
                pending.Remove(item);
                item.request.asset = assets.TryGetValue(item.path, out var value) ? value : null;
                item.request.CompleteForTest();
            }
        }

        public static void ResetTestState()
        {
            assets.Clear();
            pending.Clear();
            SynchronousLoadCount = 0;
            AsyncLoadCount = 0;
            CompleteSynchronouslyForTest = false;
        }
    }

    public static class Debug
    {
        public static readonly List<Exception> Exceptions = new List<Exception>();
        public static void Log(object value)
        {
        }

        public static void LogError(object value)
        {
        }

        public static void LogWarning(object value)
        {
        }

        public static void LogException(Exception exception)
        {
            Exceptions.Add(exception);
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName, fileName;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string text)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string text)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute
    {
        internal Type[] Types;
        public RequireComponent(Type type)
        {
            Types = new[]
            {
                type
            };
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute
    {
    }

    public enum RuntimeInitializeLoadType
    {
        SubsystemRegistration,
        BeforeSceneLoad
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type)
        {
            LoadType = type;
        }

        public RuntimeInitializeLoadType LoadType { get; }
    }
}

namespace UnityEngine.EventSystems
{
    public class EventSystem : UnityEngine.MonoBehaviour
    {
        public static EventSystem current;
        private void OnEnable()
        {
            if (current == null || !current.isActiveAndEnabled)
            {
                current = this;
            }
        }

        private void OnDisable()
        {
            if (ReferenceEquals(current, this))
            {
                current = UnityEngine.Object.FindObjectsOfType<EventSystem>().FirstOrDefault(value => !ReferenceEquals(value, this) && value.isActiveAndEnabled);
            }
        }
    }

    public class StandaloneInputModule : UnityEngine.MonoBehaviour
    {
    }

    public class PointerEventData
    {
    }

    public interface IPointerClickHandler
    {
        void OnPointerClick(PointerEventData eventData);
    }
}

namespace UnityEngine.UI
{
    public class CanvasScaler : UnityEngine.MonoBehaviour
    {
        public enum ScaleMode
        {
            ScaleWithScreenSize
        }

        public enum ScreenMatchMode
        {
            MatchWidthOrHeight,
            Expand,
            Shrink
        }

        public ScaleMode uiScaleMode;
        public ScreenMatchMode screenMatchMode;
        public UnityEngine.Vector2 referenceResolution;
        public float matchWidthOrHeight;
    }

    public class GraphicRaycaster : UnityEngine.MonoBehaviour
    {
    }

    public class Graphic : UnityEngine.MonoBehaviour
    {
        public bool raycastTarget;
    }

    public class Image : Graphic
    {
        public UnityEngine.Color color;
    }

    public class Button : UnityEngine.MonoBehaviour
    {
        public readonly UnityEngine.Events.UnityEvent onClick = new UnityEngine.Events.UnityEvent();
        public bool interactable = true;
        public Graphic targetGraphic;
    }

    public class Text : Graphic
    {
        public string text;
        public UnityEngine.Font font;
        public int fontSize;
        public UnityEngine.TextAnchor alignment;
        public UnityEngine.Color color;
    }

    public class RectMask2D : UnityEngine.MonoBehaviour
    {
    }

    // Geometry and explicit change events only; there is no simulated input, drag or inertia loop.
    public class ScrollRect : UnityEngine.MonoBehaviour
    {
        public enum MovementType
        {
            Unrestricted,
            Elastic,
            Clamped
        }

        public MovementType movementType;
        public float scrollSensitivity;
        public class ScrollRectEvent : UnityEngine.Events.UnityEvent<UnityEngine.Vector2>
        {
        }

        public UnityEngine.RectTransform content, viewport;
        public bool horizontal = true, vertical = true;
        public UnityEngine.Vector2 velocity;
        public ScrollRectEvent onValueChanged { get; set; } = new ScrollRectEvent();
        public int StopMovementCallsForTest { get; private set; }

        public void StopMovement()
        {
            velocity = UnityEngine.Vector2.zero;
            StopMovementCallsForTest++;
        }
    }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T>(T value);
    // Stable snapshots keep explicit event dispatch allocation-free. They do not model serialized persistent listeners.
    public class UnityEvent
    {
        private UnityAction[] listeners = Array.Empty<UnityAction>();
        public int ListenerCountForTest => listeners.Length;

        public void AddListener(UnityAction action)
        {
            if (action == null || Array.IndexOf(listeners, action) >= 0)
            {
                return;
            }

            var next = new UnityAction[listeners.Length + 1];
            Array.Copy(listeners, next, listeners.Length);
            next[next.Length - 1] = action;
            listeners = next;
        }

        public void RemoveListener(UnityAction action)
        {
            int index = Array.IndexOf(listeners, action);
            if (index < 0)
            {
                return;
            }

            var next = new UnityAction[listeners.Length - 1];
            if (index > 0)
            {
                Array.Copy(listeners, 0, next, 0, index);
            }

            if (index < next.Length)
            {
                Array.Copy(listeners, index + 1, next, index, next.Length - index);
            }

            listeners = next;
        }

        public void Invoke()
        {
            var snapshot = listeners;
            foreach (var callback in snapshot)
            {
                callback();
            }
        }
    }

    public class UnityEvent<T>
    {
        private UnityAction<T>[] listeners = Array.Empty<UnityAction<T>>();
        public int ListenerCountForTest => listeners.Length;

        public void AddListener(UnityAction<T> action)
        {
            if (action == null || Array.IndexOf(listeners, action) >= 0)
            {
                return;
            }

            var next = new UnityAction<T>[listeners.Length + 1];
            Array.Copy(listeners, next, listeners.Length);
            next[next.Length - 1] = action;
            listeners = next;
        }

        public void RemoveListener(UnityAction<T> action)
        {
            int index = Array.IndexOf(listeners, action);
            if (index < 0)
            {
                return;
            }

            var next = new UnityAction<T>[listeners.Length - 1];
            if (index > 0)
            {
                Array.Copy(listeners, 0, next, 0, index);
            }

            if (index < next.Length)
            {
                Array.Copy(listeners, index + 1, next, index, next.Length - index);
            }

            listeners = next;
        }

        public void Invoke(T value)
        {
            var snapshot = listeners;
            foreach (var callback in snapshot)
            {
                callback(value);
            }
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public partial struct Scene
    {
        public int handle;
        public string name;
        public bool isLoaded;
    }

    public enum LoadSceneMode
    {
        Single,
        Additive
    }

    public static partial class SceneManager
    {
        public static event Action<Scene, LoadSceneMode> sceneLoaded;
        public static event Action<Scene> sceneUnloaded;
        public static event Action<Scene, Scene> activeSceneChanged;
        public static int SceneLoadedSubscriberCountForTest => sceneLoaded?.GetInvocationList().Length ?? 0;

        public static void LoadForTest()
        {
            sceneLoaded?.Invoke(new Scene(), LoadSceneMode.Single);
        }

        public static void UnloadForTest()
        {
            sceneUnloaded?.Invoke(new Scene());
        }

        public static void ChangeActiveForTest()
        {
            activeSceneChanged?.Invoke(new Scene(), new Scene());
        }

        internal static void ResetForTest()
        {
            sceneLoaded = null;
            sceneUnloaded = null;
            activeSceneChanged = null;
        }
    }
}

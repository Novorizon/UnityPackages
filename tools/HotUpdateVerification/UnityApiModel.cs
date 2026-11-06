// Headless API contracts with controlled UPM/compile request boundaries. Native Unity import, build, HTTP and HybridCLR installation are not executed.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#if !NETSTANDARD2_1
using System.Text.Json;

#endif
namespace UnityEngine
{
    public class Object
    {
        static readonly List<Object> objects = new List<Object>();
        public string name;
        protected Object()
        {
            objects.Add(this);
        }

        public static implicit operator bool (Object obj) => obj != null;
        public static T[] FindObjectsOfType<T>(bool inactive = false) => objects.OfType<T>().ToArray();
        public static void Destroy(Object obj)
        {
            objects.Remove(obj);
            if (obj is GameObject go)
            {
                foreach (var c in go.Components)
                {
                    c.GetType().GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(c, null);
                    objects.Remove(c);
                }
            }
        }

        public static void DontDestroyOnLoad(Object obj)
        {
        }
    }

    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
    }

    public class MonoBehaviour : Component
    {
    }

    public class Transform
    {
        public Transform parent;
    }

    public class GameObject : Object
    {
        public Transform transform = new Transform();
        internal List<Component> Components = new List<Component>();
        public GameObject(string name)
        {
            this.name = name;
        }

        public T AddComponent<T>()
        {
            var c = (Component)Activator.CreateInstance(typeof(T));
            c.gameObject = this;
            Components.Add(c);
            c.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(c, null);
            return (T)(object)c;
        }
    }

    public class TextAsset : Object
    {
        public string text;
        public TextAsset(string text)
        {
            this.text = text;
        }
    }

    public static class Resources
    {
        public static readonly Dictionary<string, Object> Assets = new Dictionary<string, Object>();
        public static T Load<T>(string name)
            where T : Object => Assets.TryGetValue(name, out var o) ? o as T : null;
    }

    public static class JsonUtility
    {
#if NETSTANDARD2_1
        public static T FromJson<T>(string text) => throw new NotSupportedException("Compile-only Unity JSON contract.");
        public static string ToJson(object obj, bool pretty=false) => throw new NotSupportedException("Compile-only Unity JSON contract.");
#else
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true
        };
        public static T FromJson<T>(string text) => JsonSerializer.Deserialize<T>(text, Options);
        public static string ToJson(object obj, bool pretty = false) => JsonSerializer.Serialize(obj, obj.GetType(), Options);
#endif
    }

    public static class Application
    {
        public static string identifier = "com.novorizon.verification";
        public static string dataPath, streamingAssetsPath, persistentDataPath, unityVersion = "2022.3.62f1";
        public static bool isBatchMode, isEditor = true;
        public static void Quit(int code)
        {
        }
    }

    public static class Debug
    {
        public static readonly List<string> Errors = new List<string>();
        public static void Log(object o)
        {
        }

        public static void LogWarning(object o)
        {
        }

        public static void LogError(object o)
        {
            Errors.Add(o.ToString());
        }

        public static void LogException(Exception e)
        {
            Errors.Add(e.ToString());
        }

        public static void Assert(bool condition)
        {
            if (!condition)
            {
                throw new Exception("Assertion failed");
            }
        }

        public static void LogFormat(string s, params object[] a)
        {
        }
    }

    public enum RuntimeInitializeLoadType
    {
        SubsystemRegistration,
        AfterSceneLoad
    }

    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t)
        {
        }
    }

    public enum RuntimePlatform
    {
        WindowsEditor,
        OSXEditor,
        LinuxEditor
    }

    public class SerializeField : Attribute
    {
    }

    public class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string s)
        {
        }
    }
}

namespace UnityEngine.Scripting
{
    public class PreserveAttribute : Attribute
    {
    }
}

namespace UnityEngine.Networking
{
    public class DownloadHandlerScript : IDisposable
    {
        public DownloadHandlerScript(byte[] b)
        {
        }

        protected virtual bool ReceiveData(byte[] d, int l) => false;
        public void Dispose()
        {
        }
    }

    public class UnityWebRequest : IDisposable
    {
        public UnityWebRequest(string u, string m, DownloadHandlerScript d, object h)
        {
        }

        public bool disposeDownloadHandlerOnDispose;
        public int timeout, redirectLimit;
        public long responseCode;
        public string error;
        public enum Result
        {
            Success,
            Failure
        }

        public Result result;
        public sealed class Operation
        {
            public bool isDone;
        }

        public Operation SendWebRequest() => throw new NotSupportedException("Native HTTP is not covered by headless tests.");
        public void Abort()
        {
        }

        public void Dispose()
        {
        }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
    }
}

namespace UnityEditorInternal
{
    public class AssemblyDefinitionAsset
    {
    }
}

namespace UnityEditor
{
    public class MenuItem : Attribute
    {
        public int priority;
        public MenuItem(string s)
        {
        }
    }

    public class InitializeOnLoadAttribute : Attribute
    {
    }

    public static class AssetDatabase
    {
        public static int Refreshes;
        public static void Refresh()
        {
            Refreshes++;
        }

        public static void SaveAssets()
        {
        }
    }

    public enum BuildTarget
    {
        StandaloneWindows64,
        Android,
        iOS
    }

    public enum BuildTargetGroup
    {
        Standalone,
        Android,
        iOS
    }

    public enum ScriptingImplementation
    {
        IL2CPP
    }

    public enum ManagedStrippingLevel
    {
        Low
    }

    public enum AndroidArchitecture
    {
        ARM64,
        ARMv7
    }

    [Flags]
    public enum BuildOptions
    {
        None = 0,
        StrictMode = 1,
        Development = 2
    }

    public class BuildPlayerOptions
    {
        public string[] scenes;
        public string locationPathName;
        public BuildTarget target;
        public BuildOptions options;
    }

    public static class PlayerSettings
    {
        public static string productName = "TestGame";
        public static void SetScriptingBackend(BuildTargetGroup g, ScriptingImplementation s)
        {
        }

        public static ScriptingImplementation GetScriptingBackend(BuildTargetGroup g) => ScriptingImplementation.IL2CPP;
        public static void SetManagedStrippingLevel(BuildTargetGroup g, ManagedStrippingLevel l)
        {
        }

        public static class Android
        {
            public static AndroidArchitecture targetArchitectures;
        }
    }

    public static class EditorUserBuildSettings
    {
        public static BuildTarget activeBuildTarget = BuildTarget.StandaloneWindows64;
        public static bool development, buildAppBundle;
    }

    public class EditorBuildSettingsScene
    {
        public string path;
        public bool enabled;
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes = Array.Empty<EditorBuildSettingsScene>();
    }

    public static class BuildPipeline
    {
        public static BuildTargetGroup GetBuildTargetGroup(BuildTarget t) => (BuildTargetGroup)(int)t;
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions o) => throw new NotSupportedException("Native builds are not covered by headless tests.");
    }

    public static class EditorApplication
    {
        public static bool isCompiling, isUpdating, isPlaying;
        public static string applicationContentsPath = "MissingNativeModule";
        public static Action update;
        public static int? LastExitCode;
        public static void Exit(int code)
        {
            LastExitCode = code;
        }
    }

    public static class SessionState
    {
        public static int GetInt(string k, int d) => values.TryGetValue(k, out var s) ? int.Parse(s) : d;
        public static void SetInt(string k, int v) => values[k] = v.ToString();
        static Dictionary<string, string> values = new Dictionary<string, string>();
        public static bool GetBool(string k, bool d) => values.TryGetValue(k, out var s) ? bool.Parse(s) : d;
        public static void SetBool(string k, bool v) => values[k] = v.ToString();
        public static string GetString(string k, string d) => values.TryGetValue(k, out var s) ? s : d;
        public static void SetString(string k, string v) => values[k] = v;
    }
}

namespace UnityEditor.Compilation
{
    public enum AssembliesType
    {
        Player
    }

    public class Assembly
    {
        public string name;
        public Assembly[] assemblyReferences = Array.Empty<Assembly>();
        public string[] sourceFiles = Array.Empty<string>(), compiledAssemblyReferences = Array.Empty<string>();
        public string Definition;
    }

    public static class CompilationPipeline
    {
        public static Assembly[] Assemblies = Array.Empty<Assembly>();
        public static Assembly[] GetAssemblies(AssembliesType t) => Assemblies;
        public static string GetAssemblyDefinitionFilePathFromAssemblyName(string n) => Assemblies.Single(a => a.name == n).Definition;
    }
}

namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager
    {
        public static void OpenScene(string s)
        {
        }
    }
}

namespace UnityEditor.PackageManager
{
    public class PackageInfo
    {
        public string resolvedPath, name, version;
        public static string TestPackagePath;
        public static PackageInfo FindForAssembly(System.Reflection.Assembly a) => new PackageInfo
        {
            resolvedPath = TestPackagePath
        };
    }
}

namespace UnityEditor.Build
{
    public class BuildFailedException : Exception
    {
        public BuildFailedException(string s) : base(s)
        {
        }
    }

    public interface IPreprocessBuildWithReport
    {
        int callbackOrder { get; }

        void OnPreprocessBuild(Reporting.BuildReport r);
    }

    public interface IPostprocessBuildWithReport
    {
        int callbackOrder { get; }

        void OnPostprocessBuild(Reporting.BuildReport r);
    }
}

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult
    {
        Succeeded,
        Failed,
        Cancelled
    }

    public class BuildSummary
    {
        public BuildResult result;
        public BuildOptions options;
        public string outputPath;
    }

    public class BuildReport
    {
        public BuildSummary summary = new BuildSummary();
    }
}

namespace UnityEditor.PackageManager
{
    public enum StatusCode
    {
        Success,
        Failure
    }

    public class Error
    {
        public string message;
    }

    public static class Client
    {
        public static Func<Requests.ListRequest> ListForTest;
        public static Func<string, Requests.AddRequest> AddForTest;
        public static Requests.ListRequest List(bool offline, bool indirect) => ListForTest?.Invoke() ?? throw new NotSupportedException();
        public static Requests.AddRequest Add(string path) => AddForTest?.Invoke(path) ?? throw new NotSupportedException();
    }
}

namespace UnityEditor.PackageManager.Requests
{
    public class ListRequest
    {
        public bool IsCompleted;
        public UnityEditor.PackageManager.StatusCode Status;
        public UnityEditor.PackageManager.Error Error;
        public System.Collections.Generic.IEnumerable<UnityEditor.PackageManager.PackageInfo> Result;
    }

    public class AddRequest
    {
        public bool IsCompleted;
        public UnityEditor.PackageManager.StatusCode Status;
        public UnityEditor.PackageManager.Error Error;
    }
}

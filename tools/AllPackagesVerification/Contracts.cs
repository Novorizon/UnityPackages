// API and request-lifetime models only. No real Unity import, UPM backend or domain reload.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace UnityEngine
{
    public static class Application { public static string dataPath; }
    public static class Debug
    {
        public static readonly List<string> Errors = new List<string>();
        public static void Log(object message) { }
        public static void LogError(object message) => Errors.Add(message.ToString());
        public static void LogException(Exception exception) => Errors.Add(exception.Message);
    }
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
        public static string ToJson(object value) => JsonSerializer.Serialize(value, Options);
    }
}
namespace UnityEditor
{
    // Unity also exposes this legacy type; keep it present to catch ambiguous PackageInfo references.
    public sealed class PackageInfo
    {
    }
    [AttributeUsage(AttributeTargets.Class)] public sealed class InitializeOnLoadAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class MenuItem : Attribute { public MenuItem(string path) { } }
    public static class EditorApplication
    {
        public static bool isCompiling, isUpdating, isPlayingOrWillChangePlaymode;
        public static int? ExitCode;
        public static event Action update;
        public static void TickForTest() => update?.Invoke();
        public static void Exit(int code) { ExitCode = code; }
    }
}
namespace UnityEditor.PackageManager
{
    public enum StatusCode { InProgress, Success, Failure }
    public enum PackageSource { Git, LocalTarball, Embedded, Registry }
    public sealed class Error { public string message; }
    public sealed class PackageInfo
    {
        public string name, version, resolvedPath;
        public PackageSource source;
        public static PackageInfo Owner;
        public static PackageInfo FindForAssembly(Assembly assembly) => Owner;
        public static PackageInfo[] GetAllRegisteredPackages() => Client.Installed.Values.ToArray();
    }
    public static class Client
    {
        public static readonly Dictionary<string, PackageInfo> Sources = new Dictionary<string, PackageInfo>();
        public static readonly Dictionary<string, PackageInfo> Installed = new Dictionary<string, PackageInfo>();
        public static readonly List<string[]> Removes = new List<string[]>();
        public static readonly List<string[]> Adds = new List<string[]>();
        public static readonly List<string> Embeds = new List<string>();
        public static bool FailAdd, FailEmbed, HoldAdd, HoldEmbed;
        public static Requests.Request Active;
        public static Action CompleteAdd, CompleteEmbed;
        private static void EnsureIdle() { if (Active != null && !Active.IsCompleted) throw new InvalidOperationException("Overlapping UPM operations"); }
        public static Requests.ListRequest List(bool offlineMode, bool includeIndirectDependencies)
        {
            EnsureIdle();
            return new Requests.ListRequest { IsCompleted = true, Status = StatusCode.Success, Result = Installed.Values.ToArray() };
        }
        public static Requests.AddAndRemoveRequest AddAndRemove(string[] packagesToAdd = null, string[] packagesToRemove = null)
        {
            EnsureIdle(); Adds.Add(packagesToAdd); Removes.Add(packagesToRemove ?? Array.Empty<string>()); var request = new Requests.AddAndRemoveRequest(); Active = request;
            CompleteAdd = () =>
            {
                request.IsCompleted = true;
                request.Status = FailAdd ? StatusCode.Failure : StatusCode.Success;
                request.Error = new Error { message = "Simulated network failure" };
                if (!FailAdd)
                {
                    foreach (string name in packagesToRemove ?? Array.Empty<string>()) Installed.Remove(name);
                    foreach (string source in packagesToAdd) { var package = Sources[source]; Installed[package.name] = package; }
                }
            };
            if (!HoldAdd) CompleteAdd();
            return request;
        }
        public static Requests.EmbedRequest Embed(string name)
        {
            EnsureIdle(); Embeds.Add(name); var request = new Requests.EmbedRequest(); Active = request;
            CompleteEmbed = () =>
            {
                request.IsCompleted = true; request.Status = FailEmbed ? StatusCode.Failure : StatusCode.Success;
                request.Error = new Error { message = "Simulated embed failure" };
                if (!FailEmbed)
                {
                    var package = Installed[name]; package.source = PackageSource.Embedded;
                    package.resolvedPath = Path.Combine(Directory.GetParent(UnityEngine.Application.dataPath).FullName, "Packages", name);
                    Directory.CreateDirectory(package.resolvedPath);
                }
            };
            if (!HoldEmbed) CompleteEmbed();
            return request;
        }
        public static void Reset()
        {
            Sources.Clear(); Installed.Clear(); Adds.Clear(); Removes.Clear(); Embeds.Clear(); Active = null;
            FailAdd = FailEmbed = HoldAdd = HoldEmbed = false; CompleteAdd = CompleteEmbed = null;
        }
    }
}
namespace UnityEditor.PackageManager.Requests
{
    public class Request { public bool IsCompleted; public UnityEditor.PackageManager.StatusCode Status; public UnityEditor.PackageManager.Error Error; }
    public sealed class ListRequest : Request { public UnityEditor.PackageManager.PackageInfo[] Result; }
    public sealed class AddAndRemoveRequest : Request { }
    public sealed class EmbedRequest : Request { }
}
namespace Game.Framework.HotUpdate.Editor
{
    public static class GameBuild
    {
        public static int Calls;
        public static bool Fail;
        public static void Configure()
        {
            if (Fail) throw new InvalidOperationException("Simulated configuration failure");
            if (!UnityEditor.PackageManager.Client.Installed.Values.Any(p => p.name == "com.code-philosophy.hybridclr" && p.source == UnityEditor.PackageManager.PackageSource.Embedded)) throw new InvalidOperationException("Configure ran before embedding");
            Calls++;
            string path = Path.Combine(UnityEngine.Application.dataPath, "Resources/NovorizonHotUpdateSettings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (!File.Exists(path)) File.WriteAllText(path, "{}");
        }
    }
}

namespace UnityEditor.Compilation
{
    public enum CompilerMessageType { Error, Warning }
    public struct CompilerMessage { public CompilerMessageType type; }
    public static class CompilationPipeline
    {
        public static event Action<string, CompilerMessage[]> assemblyCompilationFinished;
        public static void FailForTest() => assemblyCompilationFinished?.Invoke("failed.dll", new[] { new CompilerMessage { type = CompilerMessageType.Error } });
    }
}

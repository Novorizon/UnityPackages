// Deterministic UPM request/assembly-reload model, not the Unity Package Manager implementation.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class InitializeOnLoadAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class MenuItem : Attribute { public MenuItem(string path) { } }
    public static class SessionState
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public static string GetString(string key, string fallback) => Values.TryGetValue(key, out var value) ? value : fallback;
        public static void SetString(string key, string value) { Values[key] = value; }
        public static void EraseString(string key) { Values.Remove(key); }
        public static void ClearForTest() => Values.Clear();
    }
    public static class EditorApplication
    {
        public static bool isCompiling, isUpdating, isPlayingOrWillChangePlaymode;
        public static event Action update;
        public static void TickForTest() => update?.Invoke();
    }
    public static class AssetDatabase { public static void Refresh() { } }
    public class EditorWindow
    {
        public static T GetWindow<T>(string title) where T : new() => new T();
        public void Repaint() { }
    }
    public enum MessageType { Info, Error }
    public static class EditorStyles { public static GUIStyle boldLabel = new GUIStyle(), wordWrappedLabel = new GUIStyle(); }
    public static class EditorGUILayout
    {
        public static void LabelField(string label, GUIStyle style) { }
        public static void HelpBox(string text, MessageType type) { }
        public static Vector2 BeginScrollView(Vector2 position) => position;
        public static void EndScrollView() { }
        public static bool ToggleLeft(string title, bool selected) => selected;
        public static void Space() { }
    }
    public static class EditorGUI
    {
        public sealed class DisabledScope : IDisposable { public DisabledScope(bool disabled) { } public void Dispose() { } }
    }
}
namespace UnityEditor.PackageManager
{
    public enum StatusCode { InProgress, Success, Failure }
    public sealed class Error { public string message; }
    public sealed class PackageInfo
    {
        public string name, version, resolvedPath;
        public static PackageInfo[] GetAllRegisteredPackages() => Client.Installed.Values.ToArray();
    }
    public static class Client
    {
        public static readonly Dictionary<string, PackageInfo> Installed = new Dictionary<string, PackageInfo>();
        public static readonly Dictionary<string, PackageInfo> Sources = new Dictionary<string, PackageInfo>();
        public static readonly List<string> Added = new List<string>();
        public static bool FailNext;
        public static Requests.EmbedRequest PendingEmbed;
        public static bool HoldEmbed;
        public static Requests.ListRequest List(bool offlineMode, bool includeIndirectDependencies) => new Requests.ListRequest
        { IsCompleted = true, Status = StatusCode.Success, Result = Installed.Values.ToArray() };
        public static Requests.AddRequest Add(string source)
        {
            Added.Add(source);
            if (FailNext) { FailNext = false; return new Requests.AddRequest { IsCompleted = true, Status = StatusCode.Failure, Error = new Error { message = "Simulated network failure" } }; }
            var p = Sources[source];
            Installed[p.name] = p;
            return new Requests.AddRequest { IsCompleted = true, Status = StatusCode.Success };
        }
        public static Requests.EmbedRequest Embed(string packageName)
        {
            var package = Installed[packageName];
            string target = Path.GetFullPath(Path.Combine("Packages", packageName));
            foreach (string file in Directory.GetFiles(package.resolvedPath, "*", SearchOption.AllDirectories))
            {
                string destination = Path.Combine(target, Path.GetRelativePath(package.resolvedPath, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(file, destination);
            }
            package.resolvedPath = target;
            PendingEmbed = new Requests.EmbedRequest { IsCompleted = !HoldEmbed, Status = StatusCode.Success };
            return PendingEmbed;
        }
        public static void ClearForTest() { Installed.Clear(); Sources.Clear(); Added.Clear(); FailNext = HoldEmbed = false; PendingEmbed = null; }
    }
}
namespace UnityEditor.PackageManager.Requests
{
    public class Request { public bool IsCompleted; public UnityEditor.PackageManager.StatusCode Status; public UnityEditor.PackageManager.Error Error; }
    public sealed class ListRequest : Request { public UnityEditor.PackageManager.PackageInfo[] Result; }
    public sealed class AddRequest : Request { }
    public sealed class EmbedRequest : Request { }
}

#nullable enable
using System;
using System.Threading.Tasks;
using Game.Resource;
using UnityEngine;

namespace Game.Framework.Samples.Resources
{
    /// <summary>Opt-in resource diagnostics for a dedicated demo scene. It never initializes on import or Start.</summary>
    public sealed class ResourceDemoController : MonoBehaviour
    {
        bool destroyed;
        int operation;

        public string Location { get; set; } = string.Empty;
        public bool AllowSharedRelease { get; set; }
        public bool IsBusy { get; private set; }
        public string Status { get; private set; } = "Prepare the project's DefaultPackage, then initialize explicitly.";
        public string Preview { get; private set; } = string.Empty;
        public string LoadedLocation { get; private set; } = string.Empty;

        public static string CurrentMode
        {
            get
            {
#if UNITY_EDITOR
                return "Editor simulation: requires project DefaultPackage collectors.";
#elif OFFLINE_PLAY_MODE
                return "Offline: requires built-in resources for this platform.";
#elif HOST_PLAY_MODE
                return "Host: clicking Initialize may contact the project's configured resource server.";
#else
                return "Offline (default): requires built-in resources for this platform.";
#endif
            }
        }

        public async Task InitializeAsync()
        {
            if (destroyed || IsBusy) return;
            IsBusy = true;
            int started = ++operation;
            Status = "Initializing DefaultPackage...";
            try
            {
                bool ready = await ResourceManager.Instance.InitializeAsync();
                if (!Current(started)) return;
                Status = ready ? "Ready. Enter a collected TextAsset location." : "Initialization failed. Check YooAsset Console and package configuration.";
            }
            catch (Exception error) { if (Current(started)) Status = Describe(error); }
            finally { if (Current(started)) IsBusy = false; }
        }

        public async Task LoadTextAsync()
        {
            if (destroyed || IsBusy) return;
            string key = (Location ?? string.Empty).Trim();
            if (key.Length == 0) { Status = "Enter a TextAsset location from the project's collectors."; return; }
            if (!ResourceManager.Instance.Initialized) { Status = "Initialize DefaultPackage first."; return; }
            IsBusy = true;
            int started = ++operation;
            Status = "Loading TextAsset...";
            try
            {
                TextAsset asset = await ResourceManager.Instance.LoadTextAssetAsync(key);
                if (!Current(started)) return;
                LoadedLocation = key;
                string text = asset.text ?? string.Empty;
                Preview = text.Length > 600 ? text.Substring(0, 600) + "..." : text;
                Status = "Loaded into the shared location cache. Repeating Load uses that cache.";
            }
            catch (Exception error) { if (Current(started)) Status = Describe(error); }
            finally { if (Current(started)) IsBusy = false; }
        }

        public void QueryCache()
        {
            if (destroyed || IsBusy) return;
            string key = (Location ?? string.Empty).Trim();
            if (key.Length == 0) { Status = "Enter a location before querying the cache."; return; }
            try
            {
                bool found = ResourceManager.Instance.TryGetLoaded<TextAsset>(key, out var asset);
                Status = found ? "Cache hit: " + asset.name : "Cache miss. This query did not start loading.";
            }
            catch (Exception error) { Status = Describe(error); }
        }

        public void ReleaseLoadedCache()
        {
            if (destroyed || IsBusy) return;
            if (!AllowSharedRelease) { Status = "Confirm the shared-cache release checkbox first."; return; }
            if (LoadedLocation.Length == 0) { Status = "This demo has no successfully loaded location to release."; return; }
            try
            {
                // This command is deliberately explicit: ResourceManager owns a shared cache,
                // not one reference-counted handle per sample component.
                ResourceManager.Instance.ReleaseHandle(LoadedLocation);
                LoadedLocation = string.Empty;
                Preview = string.Empty;
                Status = "Released the last demo-loaded shared location. Query again to inspect the cache.";
            }
            catch (Exception error) { Status = Describe(error); }
        }

        bool Current(int started) => !destroyed && started == operation;
        static string Describe(Exception error) => error.GetType().Name + ": " + error.Message;

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, Math.Max(280, Screen.width - 32), Math.Max(400, Screen.height - 32)));
            bool previous = GUI.enabled;
            try
            {
                GUILayout.Label("Novorizon Resource Demo / YooAsset 2.3.18");
                GUILayout.Label(CurrentMode);
                GUILayout.Label("No resources or bundles are included. Use a dedicated test scene/package location.");
                GUILayout.Space(8);
                GUI.enabled = previous && !IsBusy;
                if (GUILayout.Button("1. Initialize DefaultPackage")) _ = InitializeAsync();
                GUILayout.Label("Collected TextAsset location:");
                Location = GUILayout.TextField(Location ?? string.Empty);
                if (GUILayout.Button("2. Load TextAsset")) _ = LoadTextAsync();
                if (GUILayout.Button("3. Query completed cache (no IO)")) QueryCache();
                AllowSharedRelease = GUILayout.Toggle(AllowSharedRelease, "I own this test location; no other consumer is using it.");
                if (GUILayout.Button("4. Release the last demo-loaded shared location")) ReleaseLoadedCache();
                GUI.enabled = previous;
                GUILayout.Space(8);
                GUILayout.Label("Status: " + Status);
                GUILayout.Label("Last loaded location: " + LoadedLocation);
                GUILayout.Label("Text preview (up to 600 characters):\n" + Preview);
                GUILayout.Label("Leaving the demo does not shut down the application's global ResourceManager.");
            }
            finally { GUI.enabled = previous; GUILayout.EndArea(); }
        }

        void OnDestroy()
        {
            destroyed = true; operation++; IsBusy = false;
            Preview = LoadedLocation = string.Empty;
            // An in-flight provider call cannot be aborted through this facade. Its result stays
            // in the shared cache. The application/package owner decides when it is safe to release.
        }
    }
}

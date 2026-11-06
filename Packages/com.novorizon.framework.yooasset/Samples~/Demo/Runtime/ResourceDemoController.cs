#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Resource;
using UnityEngine;

namespace Game.Framework.Samples.Resources
{
    /// <summary>Explicit TextAsset acquisition; the controller releases only its own lease.</summary>
    public sealed class ResourceDemoController : MonoBehaviour
    {
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private AssetLease<TextAsset>? loaded;
        private bool destroyed;
        public string Location { get; set; } = string.Empty;
        public bool IsBusy { get; private set; }
        public string Status { get; private set; } = "Prepare DefaultPackage, then initialize explicitly.";
        public string Preview { get; private set; } = string.Empty;
        public string LoadedLocation { get; private set; } = string.Empty;

        public async Task InitializeAsync()
        {
            if (destroyed || IsBusy)
            {
                return;
            }

            IsBusy = true;
            try
            {
                bool ready = await ResourceManager.Instance.InitializeAsync();
                if (!destroyed)
                {
                    Status = ready ? "Ready. Enter a TextAsset location." : "Initialization failed; check the Console.";
                }
            }
            catch (Exception error)
            {
                if (!destroyed)
                {
                    Status = error.Message;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task LoadTextAsync()
        {
            if (destroyed || IsBusy)
            {
                return;
            }

            string key = (Location ?? string.Empty).Trim();
            if (key.Length == 0)
            {
                Status = "Enter a TextAsset location.";
                return;
            }

            if (!ResourceManager.Instance.Initialized)
            {
                Status = "Initialize DefaultPackage first.";
                return;
            }

            IsBusy = true;
            try
            {
                var acquired = await ResourceManager.Instance.AcquireAsync<TextAsset>(key, lifetime.Token);
                if (destroyed)
                {
                    acquired.Dispose();
                    return;
                }

                loaded?.Dispose();
                loaded = acquired;
                LoadedLocation = key;
                string text = acquired.Asset.text ?? string.Empty;
                Preview = text.Length > 600 ? text.Substring(0, 600) + "..." : text;
                Status = "Loaded. This controller owns one independent lease.";
            }
            catch (OperationCanceledException)
            {
                if (!destroyed)
                {
                    Status = "Loading cancelled.";
                }
            }
            catch (Exception error)
            {
                if (!destroyed)
                {
                    Status = error.Message;
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void ReleaseLoaded()
        {
            loaded?.Dispose();
            loaded = null;
            Preview = LoadedLocation = string.Empty;
            Status = "Released this controller's lease.";
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16, 16, Math.Max(280, Screen.width - 32), Math.Max(400, Screen.height - 32)));
            bool previous = GUI.enabled;
            try
            {
                GUILayout.Label("Novorizon Resource Demo / YooAsset");
                GUILayout.Label("Uses the project's DefaultPackage configuration; initialization is explicit.");
                GUI.enabled = previous && !IsBusy;
                if (GUILayout.Button("1. Initialize DefaultPackage"))
                {
                    _ = InitializeAsync();
                }

                Location = GUILayout.TextField(Location ?? string.Empty);
                if (GUILayout.Button("2. Acquire TextAsset"))
                {
                    _ = LoadTextAsync();
                }

                if (GUILayout.Button("3. Release this lease"))
                {
                    ReleaseLoaded();
                }

                GUI.enabled = previous;
                GUILayout.Label("Status: " + Status);
                GUILayout.Label("Loaded location: " + LoadedLocation);
                GUILayout.Label(Preview);
            }
            finally
            {
                GUI.enabled = previous;
                GUILayout.EndArea();
            }
        }

        private void OnDestroy()
        {
            destroyed = true;
            lifetime.Cancel();
            ReleaseLoaded();
            lifetime.Dispose();
        }
    }
}

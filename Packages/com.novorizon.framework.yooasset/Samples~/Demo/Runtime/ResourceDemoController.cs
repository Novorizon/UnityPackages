#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Resource;
using UnityEngine;

namespace Game.Framework.Samples.Resources
{
    /// <summary>每次加载持有一个文本引用，结束预览后释放。</summary>
    /// <remarks>提供资源示例 Controller的实现。</remarks>
    public sealed class ResourceDemoController : MonoBehaviour
    {
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private TextAsset? loaded;
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
#if UNITY_EDITOR
                var mode = ResourceMode.EditorSimulation;
#else
                var mode = ResourceMode.Offline;
#endif
                bool ready = await ResourceManager.InitializeAsync(mode, lifetime.Token);
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

            if (!ResourceManager.IsReady)
            {
                Status = "Initialize DefaultPackage first.";
                return;
            }

            IsBusy = true;
            try
            {
                var asset = await ResourceManager.LoadAssetAsync<TextAsset>(key, lifetime.Token);
                if (destroyed)
                {
                    ResourceManager.Release(asset);
                    return;
                }

                if (asset == null)
                {
                    Status = "TextAsset unavailable.";
                    return;
                }

                ReleaseLoaded();
                loaded = asset;
                LoadedLocation = key;
                string text = asset.text ?? string.Empty;
                Preview = text.Length > 600 ? text.Substring(0, 600) + "..." : text;
                Status = "Loaded. This controller owns one reference.";
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
            if (!ReferenceEquals(loaded, null))
            {
                ResourceManager.Release(loaded);
                loaded = null;
            }
            Preview = LoadedLocation = string.Empty;
            Status = "Released this controller's reference.";
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
                if (GUILayout.Button("2. Load TextAsset"))
                {
                    _ = LoadTextAsync();
                }

                if (GUILayout.Button("3. Release this reference"))
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

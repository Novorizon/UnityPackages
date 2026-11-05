#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace UI.Demo
{
    /// <summary>Only run in the dedicated sample scene. Builds all UI and retains its own resources.</summary>
    public sealed class UIDemoLauncher : MonoBehaviour
    {
        public const string Home = "demo/home", Inventory = "demo/inventory", Popup = "demo/popup", Panel = "demo/panel", Confirm = "demo/confirm", Loading = "demo/loading", Toast = "demo/toast";
        readonly CancellationTokenSource lifetime = new();
        UIManager? root;
        UISettings? settings;
        GameObject? input;
        UIDemoAssetLoader? loader;
        CancellationTokenSource? overlayCancellation;
        bool stopping, starting, delayed;
        int pulses;
        string status = "Starting UI demo...";
        public UIManager Root => root != null ? root : throw new InvalidOperationException("Start this demo in its own scene first.");
        public bool IsStarted => root != null && !stopping;
        public string Status => status;
        public UIDemoAssetLoader? Loader => loader;

        async void Start() => await RunAsync(StartDemoAsync);
        public async Task StartDemoAsync()
        {
            if (starting || IsStarted || stopping)
            {
                return;
            }

            if (Object.FindObjectsOfType<UIManager>(true).Length != 0)
            {
                throw new InvalidOperationException("UI Demo refuses to replace an existing UIManager. Open its generated empty scene.");
            }

            if (Object.FindObjectsOfType<EventSystem>(true).Length != 0)
            {
                throw new InvalidOperationException("UI Demo needs its own empty scene; it does not replace a project's EventSystem.");
            }

            starting = true;
            try
            {
                input = CreateInput();
                root = UIManager.Instance;
                settings = ScriptableObject.CreateInstance<UISettings>();
                settings.referenceWidth = 1280;
                settings.referenceHeight = 900;
                settings.matchWidthOrHeight = .5f;
                settings.useMainCamera = false;
                root.SetSettings(settings);
                root.SetEventSystem(input.GetComponent<EventSystem>());
                loader = CreateLoader(false);
                root.SetAssetLoader(loader);
                await root.Pages.ResetToAsync(Home, this, lifetime.Token);
                Note("Ready. Every window is created from runtime templates; no resource package is required.");
            }
            catch
            {
                StopDemo();
                throw;
            }
            finally
            {
                starting = false;
            }
        }

        static GameObject CreateInput()
        {
            var go = new GameObject("UI Demo Input");
            try
            {
                go.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                Type? module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module == null) throw new InvalidOperationException("Provide the installed Input System UI module to run this sample.");
                Component instance = go.AddComponent(module);
                module.GetMethod("AssignDefaultActions", Type.EmptyTypes)?.Invoke(instance, null);
#else
                go.AddComponent<StandaloneInputModule>();
#endif
                return go;
            }
            catch
            {
                Object.Destroy(go);
                throw;
            }
        }

        static UIDemoAssetLoader CreateLoader(bool slow)
        {
            var value = new UIDemoAssetLoader(slow ? "Delayed UI Demo" : "Immediate UI Demo", slow ? 400 : 0);
            try
            {
                value.Register(Home, UIDemoWidgets.Template<UIDemoHomePage>("Home", UIDemoWidgets.Background));
                value.Register(Inventory, UIDemoWidgets.Template<UIDemoInventoryPage>("Inventory", UIDemoWidgets.Background));
                value.Register(Popup, UIDemoWidgets.Template<UIDemoPopup>("Popup", new Color(0, 0, 0, 0)));
                value.Register(Panel, UIDemoWidgets.Template<UIDemoPanel>("Panel", new Color(0, 0, 0, 0)));
                value.Register(Confirm, UIDemoWidgets.Template<UIDemoConfirmPopup>("Confirm", new Color(0, 0, 0, 0)));
                value.Register(Loading, UIDemoWidgets.Template<UIDemoLoadingOverlay>("Loading", new Color(0, 0, 0, .7f)));
                value.Register(Toast, UIDemoWidgets.Template<UIDemoToast>("Toast", new Color(0, 0, 0, 0)));
                return value;
            }
            catch
            {
                value.Retire();
                throw;
            }
        }

        public async Task ShowInventoryAsync() => await Root.Pages.PushAsync(Inventory, this, lifetime.Token);
        public async Task ShowPopupAsync() => await Root.Popups.OpenAsync(Popup, this, cancellationToken: lifetime.Token);
        public async Task ShowPanelAsync() => await Root.Panels.ToggleAsync(Panel, this, lifetime.Token);
        public async Task BackAsync()
        {
            await Root.HandleBackAsync(lifetime.Token);
        }

        public async Task ShowConfirmAsync()
        {
            var result = await AskAsync("A typed result: choose Yes or No.", lifetime.Token);
            Note(result.HasValue ? "Dialog choice: " + result.Value : "Dialog dismissed: " + result.CloseReason);
        }

        internal Task<UIDialogResult<bool>> AskAsync(string question, CancellationToken token) => Root.Dialogs.ShowAsync<bool>(Confirm, new UIDemoQuestion(this, question), cancellationToken: token);
        public async Task ShowOverlayAsync()
        {
            if (overlayCancellation != null)
            {
                Note("Overlay is already running.");
                return;
            }

            var cancel = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            overlayCancellation = cancel;
            try
            {
                using var owned = await Root.Overlays.ShowBlockingAsync(Loading, this, cancel.Token);
                Note("Overlay owns an input gate for 4 seconds. Its own Cancel button remains available.");
                await Task.Delay(4000, cancel.Token);
                Note("Overlay token released; Back works again.");
            }
            catch (OperationCanceledException)when (cancel.IsCancellationRequested)
            {
                Note("Overlay cancelled and its token released.");
            }
            finally
            {
                if (ReferenceEquals(overlayCancellation, cancel))
                {
                    overlayCancellation = null;
                }

                cancel.Dispose();
            }
        }

        public void CancelOverlay() => overlayCancellation?.Cancel();
        public void ShowToast() => Root.Toasts.Enqueue(Toast, "Toast: cached view, completion-driven queue.");
        public void PublishPulse()
        {
            pulses++;
            Root.Bus.Publish(new UIDemoPulse(pulses));
            Note("Published pulse " + pulses + ". Open pages receive it; closed pages are automatically unsubscribed.");
        }

        public async Task ResetDemoAsync()
        {
            CancelOverlay();
            Root.ResetAll();
            await Root.Pages.ResetToAsync(Home, this, lifetime.Token);
            Note("Reset created fresh managers; old scopes and UI handles ended.");
        }

        public async Task SwitchLoaderAsync()
        {
            CancelOverlay();
            UIDemoAssetLoader? previous = loader;
            delayed = !delayed;
            UIDemoAssetLoader replacement = CreateLoader(delayed);
            try
            {
                Root.SetAssetLoader(replacement);
                loader = replacement;
            }
            catch
            {
                replacement.Retire();
                throw;
            }

            previous?.Retire();
            await Root.Pages.ResetToAsync(Home, this, lifetime.Token);
            Note(delayed ? "Switched interface implementation: every load now waits 400 ms." : "Switched back to immediate in-memory loading.");
        }

        public void Note(string message)
        {
            if (!stopping)
            {
                status = message;
            }
        }

        public void Run(Func<Task> operation) => _ = RunAsync(operation);
        async Task RunAsync(Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (OperationCanceledException)when (lifetime.IsCancellationRequested || stopping)
            {
            }
            catch (Exception exception)
            {
                Note(exception.Message);
                Debug.LogException(exception);
            }
        }

        public void StopDemo()
        {
            if (stopping)
            {
                return;
            }

            stopping = true;
            lifetime.Cancel();
            CancelOverlay();
            if (root != null)
            {
                Object.Destroy(root.gameObject);
            }

            root = null;
            // The loader's independent inactive root survives until actual view leases return.
            loader?.Retire();
            loader = null;
            if (input != null)
            {
                Object.Destroy(input);
            }

            input = null;
            if (settings != null)
            {
                Object.Destroy(settings);
            }

            settings = null;
        }

        void OnDestroy()
        {
            StopDemo();
            lifetime.Dispose();
        }
    }

    internal readonly struct UIDemoPulse
    {
        internal readonly int Value;
        internal UIDemoPulse(int value)
        {
            Value = value;
        }
    }

    internal sealed class UIDemoQuestion
    {
        internal readonly UIDemoLauncher Owner;
        internal readonly string Message;
        internal UIDemoQuestion(UIDemoLauncher owner, string message)
        {
            Owner = owner;
            Message = message;
        }
    }
}
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
                // Validate resources before creating any owned input or UI objects.
                _ = UIDemoWidgets.Font;
                input = CreateInput();
                root = UIManager.Instance;
                settings = ScriptableObject.CreateInstance<UISettings>();
                settings.referenceWidth = 1280;
                settings.referenceHeight = 900;
                settings.matchWidthOrHeight = .5f;
                settings.useMainCamera = false;
                UIManager.SetSettings(settings);
                UIManager.SetEventSystem(input.GetComponent<EventSystem>());
                loader = CreateLoader(false);
                UIManager.SetAssetLoader(loader);
                await UIManager.Pages.ResetToAsync(UIDemoKeys.Home, this, lifetime.Token);
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
                value.Register(UIDemoKeys.Home.Path, UIDemoWidgets.Template<UIDemoHomePage>("Home", UIDemoWidgets.Background));
                value.Register(UIDemoKeys.Inventory.Path, UIDemoWidgets.Template<UIDemoInventoryPage>("Inventory", UIDemoWidgets.Background));
                value.Register(UIDemoKeys.Popup.Path, UIDemoWidgets.Template<UIDemoPopup>("Popup", new Color(0, 0, 0, 0)));
                value.Register(UIDemoKeys.Panel.Path, UIDemoWidgets.Template<UIDemoPanel>("Panel", new Color(0, 0, 0, 0)));
                value.Register(UIDemoKeys.Confirm.Path, UIDemoWidgets.Template<UIDemoConfirmPopup>("Confirm", new Color(0, 0, 0, 0)));
                value.Register(UIDemoKeys.Loading.Path, UIDemoWidgets.Template<UIDemoLoadingOverlay>("Loading", new Color(0, 0, 0, .7f)));
                value.Register(UIDemoKeys.Toast.Path, UIDemoWidgets.Template<UIDemoToast>("Toast", new Color(0, 0, 0, 0)));
                return value;
            }
            catch
            {
                value.Retire();
                throw;
            }
        }

        /// <summary>异步显示物品清单。</summary>
        public async Task ShowInventoryAsync() => await UIManager.Pages.PushAsync(UIDemoKeys.Inventory, this, lifetime.Token);
        /// <summary>异步显示弹窗。</summary>
        public async Task ShowPopupAsync() => await UIManager.Popups.OpenAsync(UIDemoKeys.Popup, this, cancellationToken: lifetime.Token);
        /// <summary>异步显示面板。</summary>
        public async Task ShowPanelAsync() => await UIManager.Panels.ToggleAsync(UIDemoKeys.Panel, this, lifetime.Token);
        /// <summary>取得返回 Async。</summary>
        public async Task BackAsync()
        {
            await UIManager.HandleBackAsync(lifetime.Token);
        }

        public async Task ShowConfirmAsync()
        {
            var result = await AskAsync("A typed result: choose Yes or No.", lifetime.Token);
            Note(result.HasValue ? "Dialog choice: " + result.Value : "Dialog dismissed: " + result.CloseReason);
        }

        /// <summary>根据传入参数取得AskAsync。</summary>
        internal Task<UIDialogResult<bool>> AskAsync(string question, CancellationToken token) => UIManager.Dialogs.ShowAsync<UIDemoConfirmPopup, bool>(UIDemoKeys.Confirm, new UIDemoQuestion(this, question), cancellationToken: token);
        /// <summary>异步显示覆盖层。</summary>
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
                using var owned = await UIManager.Overlays.ShowBlockingAsync(UIDemoKeys.Loading, this, cancel.Token);
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
        /// <summary>显示提示。</summary>
        public void ShowToast() => UIManager.Toasts.Enqueue(UIDemoKeys.Toast, "Toast: cached view, completion-driven queue.");
        /// <summary>发布时钟脉冲。</summary>
        public void PublishPulse()
        {
            pulses++;
            UIManager.Bus.Publish(new UIDemoPulse(pulses));
            Note("Published pulse " + pulses + ". Open pages receive it; closed pages are automatically unsubscribed.");
        }

        public async Task ResetDemoAsync()
        {
            CancelOverlay();
            UIManager.ResetAll();
            await UIManager.Pages.ResetToAsync(UIDemoKeys.Home, this, lifetime.Token);
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
                UIManager.SetAssetLoader(replacement);
                loader = replacement;
            }
            catch
            {
                replacement.Retire();
                throw;
            }

            previous?.Retire();
            await UIManager.Pages.ResetToAsync(UIDemoKeys.Home, this, lifetime.Token);
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

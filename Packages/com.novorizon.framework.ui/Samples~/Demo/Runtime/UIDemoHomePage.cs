#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    public sealed class UIDemoHomePage : UIPage
    {
        UIDemoLauncher? owner;
        TMP_Text? statusLabel, scopeLabel;
        string previousStatus = string.Empty;
        int previousLeases = -1, received;
        protected override void OnCreate()
        {
            RectTransform safe = UIDemoWidgets.SafeContent(transform);
            var title = UIDemoWidgets.Text("Title", safe, "Novorizon UI Demo", 34);
            UIDemoWidgets.Top((RectTransform)title.transform, 22, 50);
            var hint = UIDemoWidgets.Text("Hint", safe, "Standalone uGUI / interface-based resources / safe-area content", 19);
            UIDemoWidgets.Top((RectTransform)hint.transform, 78, 40);
            Add(safe, 0, "10,000-item inventory", Inventory);
            Add(safe, 1, "Modal popup", Popup);
            Add(safe, 2, "Toggle panel", Panel);
            Add(safe, 3, "Await dialog result", Confirm);
            Add(safe, 4, "Overlay + cancel", Overlay);
            Add(safe, 5, "Toast queue", Toast);
            Add(safe, 6, "Publish local pulse", Pulse);
            Add(safe, 7, "Switch resource loader", SwitchLoader);
            Add(safe, 8, "Reset owned UI", Reset);
            Add(safe, 9, "Back", Back);
            scopeLabel = UIDemoWidgets.Text("Scope", safe, string.Empty, 20);
            UIDemoWidgets.Top((RectTransform)scopeLabel.transform, 462, 70);
            statusLabel = UIDemoWidgets.Text("Status", safe, string.Empty, 20);
            UIDemoWidgets.Top((RectTransform)statusLabel.transform, 546, 120);
        }

        static void Add(RectTransform parent, int index, string caption, UnityEngine.Events.UnityAction action)
        {
            var button = UIDemoWidgets.Button("Action " + index, parent, caption, action);
            int column = index % 2;
            UIDemoWidgets.Top((RectTransform)button.transform, 126 + index / 2 * 66, 56, 24, 24, column * .5f, (column + 1) * .5f);
        }

        protected override void OnOpen(object? args)
        {
            owner = args as UIDemoLauncher ?? throw new ArgumentException("Demo controller is required.", nameof(args));
            previousStatus = string.Empty;
            previousLeases = -1;
            received = 0;
            Track(owner.Root.Bus.SubscribeScoped<UIDemoPulse>(OnPulse));
            if (scopeLabel != null)
            {
                scopeLabel.text = "OpenToken refresh pending for 1 second. Hide keeps the scope; Close cancels it.";
            }

            _ = RefreshAsync(OpenToken);
            Update();
        }

        async Task RefreshAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(1000, token);
                token.ThrowIfCancellationRequested();
                if (scopeLabel != null)
                {
                    scopeLabel.text = "Scoped async refresh completed. Received pulses: " + received;
                }
            }
            catch (OperationCanceledException)when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        void OnPulse(UIDemoPulse pulse)
        {
            received++;
            if (scopeLabel != null)
            {
                scopeLabel.text = "Current open received pulse " + pulse.Value + " (total " + received + ").";
            }
        }

        protected override void OnShow()
        {
            if (owner != null)
            {
                owner.Note("Home visible. SafeContent avoids the device safe-area edges.");
            }
        }

        protected override void OnHide()
        {
            if (owner != null)
            {
                owner.Note("Home hidden by another page; its OpenToken and subscription remain active.");
            }
        }

        protected override void OnClose()
        {
            owner = null;
        }

        void Update()
        {
            if (owner == null || statusLabel == null)
            {
                return;
            }

            int leases = owner.Loader?.OutstandingLeases ?? 0;
            if (previousStatus == owner.Status && previousLeases == leases)
            {
                return;
            }

            previousStatus = owner.Status;
            previousLeases = leases;
            statusLabel.text = owner.Status + "\nResource leases/pending: " + leases + "; acquisitions: " + (owner.Loader?.AcquireCount ?? 0) + "; releases: " + (owner.Loader?.ReleaseCount ?? 0);
        }

        void Inventory()
        {
            if (owner != null)
            {
                owner.Run(owner.ShowInventoryAsync);
            }
        }

        void Popup()
        {
            if (owner != null)
            {
                owner.Run(owner.ShowPopupAsync);
            }
        }

        void Panel()
        {
            if (owner != null)
            {
                owner.Run(owner.ShowPanelAsync);
            }
        }

        void Confirm()
        {
            if (owner != null)
            {
                owner.Run(owner.ShowConfirmAsync);
            }
        }

        void Overlay()
        {
            if (owner != null)
            {
                owner.Run(owner.ShowOverlayAsync);
            }
        }

        void Toast()
        {
            if (owner != null)
            {
                owner.ShowToast();
            }
        }

        void Pulse()
        {
            if (owner != null)
            {
                owner.PublishPulse();
            }
        }

        void SwitchLoader()
        {
            if (owner != null)
            {
                owner.Run(owner.SwitchLoaderAsync);
            }
        }

        void Reset()
        {
            if (owner != null)
            {
                owner.Run(owner.ResetDemoAsync);
            }
        }

        void Back()
        {
            if (owner != null)
            {
                owner.Run(owner.BackAsync);
            }
        }
    }
}

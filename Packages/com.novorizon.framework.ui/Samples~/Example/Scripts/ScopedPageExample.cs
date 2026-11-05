#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sample
{
    public readonly struct ExampleCoinsChanged
    {
        public readonly int Coins;
        public ExampleCoinsChanged(int coins)
        {
            Coins = coins;
        }
    }

    /// <summary>Demonstrates per-open subscriptions/work and an optional unsaved-changes confirmation.</summary>
    public sealed class ScopedPageExample : UIPage
    {
        [SerializeField]
        Text? status = null;
        [SerializeField]
        string confirmationPath = "UI/Popups/ConfirmPopup";
        bool hasUnsavedChanges;
        protected override void OnCreate() => CloseGuard = ConfirmCloseAsync;
        protected override void OnOpen(object? args)
        {
            hasUnsavedChanges = false;
            Track(UIManager.Instance.Bus.SubscribeScoped<ExampleCoinsChanged>(OnCoinsChanged));
            _ = RefreshPreviewAsync(OpenToken);
        }

        public void MarkChanged() => hasUnsavedChanges = true;
        public void MarkSaved() => hasUnsavedChanges = false;
        void OnCoinsChanged(ExampleCoinsChanged change)
        {
            if (status != null)
            {
                status.text = change.Coins.ToString();
            }
        }

        async Task RefreshPreviewAsync(CancellationToken token)
        {
            try
            {
                // Replace with a project service call that accepts this captured token.
                await Task.Delay(150, token);
                token.ThrowIfCancellationRequested();
                if (status != null)
                {
                    status.text = "Ready";
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

        async Task<bool> ConfirmCloseAsync(UICloseReason reason, CancellationToken token)
        {
            if (!hasUnsavedChanges)
            {
                return true;
            }

            UIDialogResult<bool> choice = await UIManager.Instance.Dialogs.ShowAsync<bool>(confirmationPath, "Discard unsaved changes?", cancellationToken: token);
            return choice.HasValue && choice.Value;
        }
    }
}
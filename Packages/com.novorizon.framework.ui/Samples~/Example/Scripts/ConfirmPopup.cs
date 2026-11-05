#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sample
{
    /// <summary>Create a prefab with this component, two buttons and an optional label.</summary>
    public sealed class ConfirmPopup : UIResultPopup<bool>
    {
        [SerializeField] Button? confirmButton = null;
        [SerializeField] Button? cancelButton = null;
        [SerializeField] Text? message = null;

        public override bool CloseOnBlockerClick => true;

        protected override void OnCreate()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        }

        protected override void OnOpen(object? args)
        {
            if (message != null) message.text = args as string ?? "Continue?";
        }

        void Confirm() => Complete(true);
        void Cancel() => Complete(false);

        protected override void OnDestroyed()
        {
            if (confirmButton != null) confirmButton.onClick.RemoveListener(Confirm);
            if (cancelButton != null) cancelButton.onClick.RemoveListener(Cancel);
        }
    }
}

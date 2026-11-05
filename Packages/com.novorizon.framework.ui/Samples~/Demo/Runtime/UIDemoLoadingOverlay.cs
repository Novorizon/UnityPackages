#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    public sealed class UIDemoLoadingOverlay : UIOverlay
    {
        UIDemoLauncher? owner;
        protected override void OnCreate()
        {
            // A blocking overlay needs an actual raycast target, not only reference counting.
            GetComponent<Image>().raycastTarget = true;
            RectTransform card = UIDemoWidgets.CenterCard(transform, "LoadingCard", 680, 310);
            Text label = UIDemoWidgets.Text("Message", card,
                "Loading for four seconds...\nBack is gated. This Cancel button cancels only this overlay owner.", 25);
            UIDemoWidgets.Top((RectTransform)label.transform, 28, 160);
            Button cancel = UIDemoWidgets.Button("Cancel", card, "Cancel overlay", Cancel);
            UIDemoWidgets.Top((RectTransform)cancel.transform, 224, 60);
        }
        protected override void OnOpen(object? args) => owner = args as UIDemoLauncher ?? throw new ArgumentException("Demo controller is required.", nameof(args));
        protected override void OnClose() => owner = null;
        void Cancel() => owner?.CancelOverlay();
    }
}

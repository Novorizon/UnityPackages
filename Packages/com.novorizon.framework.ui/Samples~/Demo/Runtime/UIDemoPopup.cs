#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    public sealed class UIDemoPopup : UIPopup
    {
        UIDemoLauncher? owner;
        public override bool CloseOnBlockerClick => true;
        protected override void OnCreate()
        {
            RectTransform card = UIDemoWidgets.CenterCard(transform, "PopupCard", 680, 320);
            Text title = UIDemoWidgets.Text("Message", card,
                "Modal popup\nThe blocker is above ordinary panels.\nClick outside or use Back to close.", 25);
            UIDemoWidgets.Top((RectTransform)title.transform, 28, 170);
            Button close = UIDemoWidgets.Button("Close", card, "Close top popup", Close);
            UIDemoWidgets.Top((RectTransform)close.transform, 228, 64);
        }
        protected override void OnOpen(object? args) => owner = args as UIDemoLauncher ?? throw new ArgumentException("Demo controller is required.", nameof(args));
        protected override void OnClose() => owner = null;
        void Close() { if (owner != null) owner.Run(owner.BackAsync); }
    }
}

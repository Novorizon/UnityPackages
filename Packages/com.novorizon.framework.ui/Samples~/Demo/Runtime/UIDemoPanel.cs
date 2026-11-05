#nullable enable
using System;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    public sealed class UIDemoPanel : UIPanel
    {
        UIDemoLauncher? owner;
        protected override void OnCreate()
        {
            RectTransform card = UIDemoWidgets.CenterCard(transform, "PanelCard", 620, 300);
            card.anchorMin = card.anchorMax = new Vector2(1, .5f); card.pivot = new Vector2(1, .5f);
            card.anchoredPosition = new Vector2(-24, 0);
            Text label = UIDemoWidgets.Text("Message", card, "Non-modal panel\nIt is below modal popups and can be toggled independently.", 25);
            UIDemoWidgets.Top((RectTransform)label.transform, 26, 150);
            Button close = UIDemoWidgets.Button("Close", card, "Hide this panel", Close);
            UIDemoWidgets.Top((RectTransform)close.transform, 210, 60);
        }
        protected override void OnOpen(object? args) => owner = args as UIDemoLauncher ?? throw new ArgumentException("Demo controller is required.", nameof(args));
        protected override void OnClose() => owner = null;
        void Close()
        {
            UIDemoLauncher? controller = owner;
            if (controller != null) controller.Run(() => controller.Root.Panels.HideAsync(UIDemoLauncher.Panel));
        }
    }
}

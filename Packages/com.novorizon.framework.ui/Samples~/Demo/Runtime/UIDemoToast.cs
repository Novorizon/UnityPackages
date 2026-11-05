#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    public sealed class UIDemoToast : UIToast
    {
        Text? label;
        protected override void OnCreate()
        {
            RectTransform card = UIDemoWidgets.CenterCard(transform, "ToastCard", 720, 110);
            card.anchorMin = card.anchorMax = new Vector2(.5f, 0); card.pivot = new Vector2(.5f, 0);
            card.anchoredPosition = new Vector2(0, 28);
            card.GetComponent<Image>().raycastTarget = false;
            label = UIDemoWidgets.Text("Message", card, string.Empty, 23);
            UIDemoWidgets.Stretch((RectTransform)label.transform); label.alignment = TextAnchor.MiddleCenter;
        }
        protected override void OnOpen(object? args)
        {
            if (label != null) label.text = args as string ?? "Toast";
            base.OnOpen(args);
        }
    }
}

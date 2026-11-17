#nullable enable
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    /// <summary>提供界面示例提示的实现。</summary>
    public sealed class UIDemoToast : UIToast
    {
        TMP_Text? label;
        /// <summary>响应 Create。</summary>
        protected override void OnCreate()
        {
            RectTransform card = UIDemoWidgets.CenterCard(transform, "ToastCard", 720, 110);
            card.anchorMin = card.anchorMax = new Vector2(.5f, 0);
            card.pivot = new Vector2(.5f, 0);
            card.anchoredPosition = new Vector2(0, 28);
            card.GetComponent<Image>().raycastTarget = false;
            label = UIDemoWidgets.Text("Message", card, string.Empty, 23);
            UIDemoWidgets.Stretch((RectTransform)label.transform);
            label.alignment = TextAlignmentOptions.Center;
        }

        /// <summary>响应打开。</summary>
        protected override void OnOpen(object? args)
        {
            if (label != null)
            {
                label.text = args as string ?? "Toast";
            }

            base.OnOpen(args);
        }
    }
}

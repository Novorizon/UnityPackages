#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    /// <summary>提供界面示例加载中覆盖层的实现。</summary>
    public sealed class UIDemoLoadingOverlay : UIOverlay
    {
        UIDemoLauncher? owner;
        /// <summary>响应 Create。</summary>
        protected override void OnCreate()
        {
            // A blocking overlay needs an actual raycast target, not only reference counting.
            GetComponent<Image>().raycastTarget = true;
            RectTransform card = UIDemoWidgets.CenterCard(transform, "LoadingCard", 680, 310);
            TMP_Text label = UIDemoWidgets.Text("Message", card, "Loading for four seconds...\nBack is gated. This Cancel button cancels only this overlay owner.", 25);
            UIDemoWidgets.Top((RectTransform)label.transform, 28, 160);
            Button cancel = UIDemoWidgets.Button("Cancel", card, "Cancel overlay", Cancel);
            UIDemoWidgets.Top((RectTransform)cancel.transform, 224, 60);
        }

        /// <summary>响应打开。</summary>
        protected override void OnOpen(object? args) => owner = args as UIDemoLauncher ?? throw new ArgumentException("Demo controller is required.", nameof(args));
        /// <summary>响应 Close。</summary>
        protected override void OnClose() => owner = null;
        /// <summary>取消界面示例加载中覆盖层。</summary>
        void Cancel() => owner?.CancelOverlay();
    }
}

#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    /// <summary>提供界面示例弹窗的实现。</summary>
    public sealed class UIDemoPopup : UIPopup
    {
        UIDemoLauncher? owner;
        public override bool CloseOnBlockerClick => true;

        /// <summary>响应 Create。</summary>
        protected override void OnCreate()
        {
            RectTransform card = UIDemoWidgets.CenterCard(transform, "PopupCard", 680, 320);
            TMP_Text title = UIDemoWidgets.Text("Message", card, "Modal popup\nThe blocker is above ordinary panels.\nClick outside or use Back to close.", 25);
            UIDemoWidgets.Top((RectTransform)title.transform, 28, 170);
            Button close = UIDemoWidgets.Button("Close", card, "Close top popup", Close);
            UIDemoWidgets.Top((RectTransform)close.transform, 228, 64);
        }

        /// <summary>响应打开。</summary>
        protected override void OnOpen(object? args) => owner = args as UIDemoLauncher ?? throw new ArgumentException("Demo controller is required.", nameof(args));
        /// <summary>响应 Close。</summary>
        protected override void OnClose() => owner = null;
        /// <summary>关闭界面示例弹窗。</summary>
        void Close()
        {
            if (owner != null)
            {
                owner.Run(owner.BackAsync);
            }
        }
    }
}

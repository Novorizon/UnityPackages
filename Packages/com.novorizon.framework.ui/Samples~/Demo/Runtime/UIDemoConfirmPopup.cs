#nullable enable
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Demo
{
    /// <summary>提供界面示例 Confirm 弹窗的实现。</summary>
    public sealed class UIDemoConfirmPopup : UIResultPopup<bool>
    {
        TMP_Text? message;
        public override bool CloseOnBlockerClick => true;

        /// <summary>响应 Create。</summary>
        protected override void OnCreate()
        {
            RectTransform card = UIDemoWidgets.CenterCard(transform, "ConfirmCard", 660, 320);
            message = UIDemoWidgets.Text("Question", card, string.Empty, 25);
            UIDemoWidgets.Top((RectTransform)message.transform, 30, 150);
            Button yes = UIDemoWidgets.Button("Yes", card, "Yes", Yes);
            UIDemoWidgets.Top((RectTransform)yes.transform, 220, 64, 24, 12, 0, .5f);
            Button no = UIDemoWidgets.Button("No", card, "No", No);
            UIDemoWidgets.Top((RectTransform)no.transform, 220, 64, 12, 24, .5f, 1);
        }

        /// <summary>响应打开。</summary>
        protected override void OnOpen(object? args)
        {
            var question = args as UIDemoQuestion ?? throw new ArgumentException("A demo question is required.", nameof(args));
            if (message != null)
            {
                message.text = question.Message;
            }
        }

        /// <summary>调用 Complete 完成Yes。</summary>
        void Yes() => Complete(true);
        /// <summary>调用 Complete 完成No。</summary>
        void No() => Complete(false);
    }
}

#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sample
{
    public sealed class SettingsPopup : UIPopup
    {
        [SerializeField]
        Button? closeButton = null;
        public override bool CloseOnBlockerClick => true;

        protected override void OnCreate()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(CloseSelf);
            }
        }

        void CloseSelf()
        {
            UIHandle? top = UIManager.Instance.Popups.Peek();
            if (top.HasValue && top.Value.View == this)
            {
                UIManager.Instance.Popups.Close(top.Value);
            }
        }
    }
}
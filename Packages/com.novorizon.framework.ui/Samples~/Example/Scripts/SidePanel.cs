#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace UI.Sample
{
    public sealed class SidePanel : UIPanel
    {
        [SerializeField] Button? closeButton = null;

        protected override void OnCreate()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(() => UIRoot.Instance.Panels.Hide("UI/Panels/SidePanel"));
            }
        }
    }
}

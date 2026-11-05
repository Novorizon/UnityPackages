#nullable enable
using UnityEngine.UI;

namespace UI.Sample
{
    public sealed class SimpleToast : UIToast
    {
        [UnityEngine.SerializeField] Text? text = null;

        public override float Duration => 1.5f;

        protected override void OnOpen(object? args)
        {
            if (text != null)
            {
                text.text = args as string ?? string.Empty;
            }

            base.OnOpen(args);
        }
    }
}

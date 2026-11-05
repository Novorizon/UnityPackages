#nullable enable
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    public abstract class UIToast : UIView
    {
        TaskCompletionSource<bool>? completion;
        public virtual float Duration => 2.0f;
        internal Task Completion => completion?.Task ?? Task.CompletedTask;

        internal void Prepare()
        {
            StopAllCoroutines();
            completion = new TaskCompletionSource<bool>();
            Closed -= OnToastClosed;
            Closed += OnToastClosed;
        }

        protected void Complete() => completion?.TrySetResult(true);

        void OnToastClosed(UIView view)
        {
            StopAllCoroutines();
            completion?.TrySetResult(true);
        }

        protected override void OnOpen(object? args)
        {
            float duration = Duration;
            if (duration > 0f) StartCoroutine(AutoComplete(duration));
        }

        IEnumerator AutoComplete(float duration)
        {
            yield return new WaitForSeconds(duration);
            Complete();
        }
    }
}

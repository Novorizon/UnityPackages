#nullable enable
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>提供界面提示的实现。</summary>
    public abstract class UIToast : UIView
    {
        TaskCompletionSource<bool>? completion;
        public virtual float Duration => 2.0f;
        internal Task Completion => completion?.Task ?? Task.CompletedTask;

        /// <summary>准备界面提示。</summary>
        internal void Prepare()
        {
            StopAllCoroutines();
            completion = new TaskCompletionSource<bool>();
            Closed -= OnToastClosed;
            Closed += OnToastClosed;
        }

        /// <summary>完成界面提示。</summary>
        protected void Complete() => completion?.TrySetResult(true);

        /// <summary>响应提示已关闭。</summary>
        void OnToastClosed(UIView view)
        {
            StopAllCoroutines();
            completion?.TrySetResult(true);
        }

        /// <summary>响应打开。</summary>
        protected override void OnOpen(object? args)
        {
            float duration = Duration;
            if (duration > 0f) StartCoroutine(AutoComplete(duration));
        }

        /// <summary>根据传入参数取得自动 Complete。</summary>
        IEnumerator AutoComplete(float duration)
        {
            yield return new WaitForSeconds(duration);
            Complete();
        }
    }
}

#nullable enable
using System;

namespace UI
{
    /// <summary>A completed selection, or a dismissal with its close reason.</summary>
    /// <remarks>界面对话框对应的结果。携带操作执行结果和相关数据。</remarks>
    public readonly struct UIDialogResult<TResult>
    {
        readonly TResult value;

        public bool HasValue { get; }
        public UICloseReason CloseReason { get; }
        public TResult Value => HasValue ? value : throw new InvalidOperationException("The dialog was dismissed without a result.");

        /// <summary>使用调用方提供的hasValue、value、reason创建 UIDialogResult。</summary>
        UIDialogResult(bool hasValue, TResult value, UICloseReason reason)
        {
            HasValue = hasValue;
            this.value = value;
            CloseReason = reason;
        }

        /// <summary>尝试取得值，返回是否成功。</summary>
        public bool TryGetValue(out TResult result)
        {
            result = value;
            return HasValue;
        }

        /// <summary>根据传入参数取得完成。</summary>
        internal static UIDialogResult<TResult> Completed(TResult result) => new(true, result, UICloseReason.Completed);
        /// <summary>根据传入参数取得Dismissed。</summary>
        internal static UIDialogResult<TResult> Dismissed(UICloseReason reason) => new(false, default!, reason);
    }
}

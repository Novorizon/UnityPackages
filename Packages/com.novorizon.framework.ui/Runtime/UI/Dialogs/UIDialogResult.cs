#nullable enable
using System;

namespace UI
{
    /// <summary>A completed selection, or a dismissal with its close reason.</summary>
    public readonly struct UIDialogResult<TResult>
    {
        readonly TResult value;

        public bool HasValue { get; }
        public UICloseReason CloseReason { get; }
        public TResult Value => HasValue ? value : throw new InvalidOperationException("The dialog was dismissed without a result.");

        UIDialogResult(bool hasValue, TResult value, UICloseReason reason)
        {
            HasValue = hasValue;
            this.value = value;
            CloseReason = reason;
        }

        public bool TryGetValue(out TResult result)
        {
            result = value;
            return HasValue;
        }

        internal static UIDialogResult<TResult> Completed(TResult result) => new(true, result, UICloseReason.Completed);
        internal static UIDialogResult<TResult> Dismissed(UICloseReason reason) => new(false, default!, reason);
    }
}

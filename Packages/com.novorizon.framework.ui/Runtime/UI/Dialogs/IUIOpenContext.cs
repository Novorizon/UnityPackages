#nullable enable

namespace UI
{
    // Framework-only envelope: capture the prepared open before user callbacks or
    // asynchronous manager continuations, while passing the original args to the view.
    /// <summary>定义IUI 打开上下文的调用契约。</summary>
    internal interface IUIOpenContext
    {
        object? Args { get; }
        /// <summary>返回演出或播放是否已经准备就绪。</summary>
        void Prepared(UIView view);
    }
}

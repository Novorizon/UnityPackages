#nullable enable

namespace UI
{
    // Called by UIView before the first user OnOpen callback for a result popup.
    /// <summary>定义IUI 结果来源的调用契约。</summary>
    internal interface IUIResultSource
    {
        /// <summary>准备结果。</summary>
        void PrepareResult();
    }
}

#nullable enable

namespace UI
{
    // Called by UIView before the first user OnOpen callback for a result popup.
    internal interface IUIResultSource
    {
        void PrepareResult();
    }
}

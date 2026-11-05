#nullable enable

namespace UI
{
    // Framework-only envelope: capture the prepared open before user callbacks or
    // asynchronous manager continuations, while passing the original args to the view.
    internal interface IUIOpenContext
    {
        object? Args { get; }
        void Prepared(UIView view);
    }
}

namespace UI
{
    /// <summary>定义界面 Close 原因可选的枚举值。</summary>
    public enum UICloseReason
    {
        Programmatic,
        Back,
        Blocker,
        Replaced,
        Reset,
        Disposed,
        Destroyed,
        Cancelled,
        Completed
    }
}

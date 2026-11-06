namespace UI
{
    /// <summary>非多实例窗口被重复打开时的行为。</summary>
    public enum UIDuplicatePolicy
    {
        Refresh,
        Focus,
        Reject
    }

    /// <summary>Push 的目标与栈顶路径相同时的行为；默认保留历史版本的 Allow 行为。</summary>
    public enum PageRepeatPolicy
    {
        Allow,
        ReuseTop,
        RefreshTop,
        RejectTop
    }
}

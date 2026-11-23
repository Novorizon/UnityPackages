namespace UI.Demo
{
    /// <summary>示例业务程序集声明的界面资源，加载器使用 key 的 Path。</summary>
    internal static class UIDemoKeys
    {
        internal static readonly UIKey<UIDemoHomePage> Home = new("demo/home");
        internal static readonly UIKey<UIDemoInventoryPage> Inventory = new("demo/inventory");
        internal static readonly UIKey<UIDemoPopup> Popup = new("demo/popup");
        internal static readonly UIKey<UIDemoPanel> Panel = new("demo/panel");
        internal static readonly UIKey<UIDemoConfirmPopup> Confirm = new("demo/confirm");
        internal static readonly UIKey<UIDemoLoadingOverlay> Loading = new("demo/loading");
        internal static readonly UIKey<UIDemoToast> Toast = new("demo/toast");
    }
}

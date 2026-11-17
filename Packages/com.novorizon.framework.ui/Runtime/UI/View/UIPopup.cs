namespace UI
{
    /// <summary>提供界面弹窗的实现。</summary>
    public abstract class UIPopup : UIView
    {
        public virtual bool CloseOnBlockerClick => false;
    }
}

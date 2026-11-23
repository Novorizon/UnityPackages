#nullable enable
using System;

namespace UI
{
    /// <summary>在业务程序集声明的界面资源地址。视图类型须与 prefab 根节点上的 UIView 一致。</summary>
    public readonly struct UIKey<TView> where TView : UIView
    {
        internal static readonly Predicate<UIView> MatchesView = view => view is TView;

        readonly string? path;

        public string Path => path ?? string.Empty;

        public UIKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A prefab path is required.", nameof(path));
            }

            this.path = path;
        }
    }
}

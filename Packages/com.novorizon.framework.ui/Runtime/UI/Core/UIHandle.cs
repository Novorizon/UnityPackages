#nullable enable
using UnityEngine;

namespace UI
{
    public readonly struct UIHandle
    {
        public readonly int Id;
        public readonly string PrefabPath;
        public readonly UIKind Kind;
        public readonly UILayer Layer;
        public readonly UIView? View;

        public bool IsValid => Id != 0 && View != null && !View.IsDestroyed && View.InstanceId == Id && View.IsOpen;

        /// <summary>使用调用方提供的id、prefabPath、kind创建 UIHandle。</summary>
        internal UIHandle(int id, string prefabPath, UIKind kind, UILayer layer, UIView? view)
        {
            Id = id;
            PrefabPath = prefabPath;
            Kind = kind;
            Layer = layer;
            View = view;
        }

        public override string ToString()
        {
            return $"UIHandle(Id={Id}, Kind={Kind}, Layer={Layer}, Path={PrefabPath}, View={(View != null ? View.name : "null")})";
        }
    }

    /// <summary>一次打开返回的类型化句柄。可传给关闭接口，缓存重开会使旧句柄失效。</summary>
    public readonly struct UIHandle<TView> where TView : UIView
    {
        readonly UIHandle handle;

        internal UIHandle(UIHandle handle)
        {
            this.handle = handle;
        }

        public int Id => handle.Id;
        public string Path => handle.PrefabPath;
        public UIKind Kind => handle.Kind;
        public UILayer Layer => handle.Layer;
        public TView? View => handle.View as TView;
        public bool IsValid => handle.IsValid;

        public static implicit operator UIHandle(UIHandle<TView> handle) => handle.handle;
    }
}

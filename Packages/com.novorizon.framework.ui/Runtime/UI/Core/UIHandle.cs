#nullable enable
using UnityEngine;

namespace UI
{
    /// <summary>界面对应的句柄。保存访问本次操作或对象所需的标识。</summary>
    public readonly struct UIHandle
    {
        public readonly int Id;
        public readonly string PrefabPath;
        public readonly UIKind Kind;
        public readonly UILayer Layer;
        public readonly UIView? View;

        public bool IsValid => Id != 0 && View != null && !View.IsDestroyed && View.InstanceId == Id && View.IsOpen;

        /// <summary>使用调用方提供的id、prefabPath、kind创建 UIHandle。</summary>
        public UIHandle(int id, string prefabPath, UIKind kind, UILayer layer, UIView? view)
        {
            Id = id;
            PrefabPath = prefabPath;
            Kind = kind;
            Layer = layer;
            View = view;
        }

        /// <summary>格式化当前值，供显示或诊断使用。</summary>
        public override string ToString()
        {
            return $"UIHandle(Id={Id}, Kind={Kind}, Layer={Layer}, Path={PrefabPath}, View={(View != null ? View.name : "null")})";
        }
    }
}

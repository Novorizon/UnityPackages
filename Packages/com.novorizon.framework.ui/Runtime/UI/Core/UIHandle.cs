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

        public UIHandle(int id, string prefabPath, UIKind kind, UILayer layer, UIView? view)
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
}

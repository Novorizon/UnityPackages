using UnityEngine;

namespace ECS
{
    /// <summary>提供CopyFrom 变换系统的实现。</summary>
    public class CopyFromTransformSystem : HybridSystem<Transform, LocalToWorld, CopyFromTransformComponent>
    {
        /// <summary>响应更新。</summary>
        protected override void OnUpdate(int index, Entity entity, Transform component1, LocalToWorld component2, CopyFromTransformComponent component3)
        {
            if (component1 != null)
                component2.Value = component1.localToWorldMatrix;
        }
    }
}

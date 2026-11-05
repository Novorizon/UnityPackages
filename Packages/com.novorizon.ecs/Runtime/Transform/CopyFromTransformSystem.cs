using UnityEngine;

namespace ECS
{
    public class CopyFromTransformSystem : HybridSystem<Transform, LocalToWorld, CopyFromTransformComponent>
    {
        protected override void OnUpdate(int index, Entity entity, Transform component1, LocalToWorld component2, CopyFromTransformComponent component3)
        {
            if (component1 != null)
                component2.Value = component1.localToWorldMatrix;
        }
    }
}

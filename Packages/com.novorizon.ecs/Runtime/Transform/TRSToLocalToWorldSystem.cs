using Unity.Mathematics;
using UnityEngine;

namespace ECS
{
    /// <summary>提供TRSTo 本地 To 世界系统的实现。</summary>
    public class TRSToLocalToWorldSystem : ComponentSystem<LocalToWorld, Position, Rotation, Scale>
    {
        /// <summary>响应更新。</summary>
        protected override void OnUpdate(int index, Entity entity, LocalToWorld c1, Position c2, Rotation c3, Scale c4)
        {
            c1.Value = Matrix4x4.TRS(c2.Value, math.normalize(c3.Value), c4.Value);

#if UNITY_EDITOR
            if (EntityManager.Instance.IsDataMode)
            {
                Debug.DrawLine(c1.Position, c1.Position + math.mul(c1.Rotation, math.right()) * 1, Color.red);
                Debug.DrawLine(c1.Position, c1.Position + math.mul(c1.Rotation, math.up()) * 1, Color.green);
                Debug.DrawLine(c1.Position, c1.Position + math.mul(c1.Rotation, math.forward()) * 1, Color.blue);
            }
#endif
        }
    }
}

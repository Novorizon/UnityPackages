using Unity.Mathematics;
using UnityEngine;

#if ENABLE_JOBS
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.Jobs;
#endif

namespace ECS
{
    public class CopyToTransformSystem : HybridSystem<Transform, LocalToWorld, CopyToTransformComponent>
    {
#if ENABLE_JOBS
        [BurstCompile]
        struct CopyTransforms : IJobParallelForTransform
        {
            [DeallocateOnJobCompletion]
            [ReadOnly] public NativeArray<float3> positions;
            [DeallocateOnJobCompletion]
            [ReadOnly] public NativeArray<quaternion> rotations;

            public void Execute(int index, TransformAccess transform)
            {
                transform.position = positions[index];
                transform.rotation = rotations[index];
            }
        }

        private JobHandle handle;
        private CopyTransforms Job;

        private TransformAccessArray transofrms;

        public override void PreUpdate()
        {
            if (Entities.Count > 0)
            {
                handle = new JobHandle();
                Job = new CopyTransforms
                {
                    positions = new NativeArray<float3>(Entities.Count, Allocator.TempJob),
                    rotations = new NativeArray<quaternion>(Entities.Count, Allocator.TempJob),
                };

                transofrms = new TransformAccessArray(Entities.Count);
            }

        }

        public override void PostUpdate()
        {
            if (Entities.Count > 0)
            {
                handle = Job.Schedule(transofrms, handle);
                handle.Complete();

                transofrms.Dispose();
            }
        }
#endif

        protected override void OnUpdate(int index, Entity entity, Transform component1, LocalToWorld component2, CopyToTransformComponent component3)
        {
#if ENABLE_JOBS
            Job.positions[index] = component2.Position;
            Job.rotations[index] = component2.Rotation;
            transofrms.Add(component1);
#else
            if (component1 != null)
            {
                component1.SetPositionAndRotation(component2.Position, component2.Rotation);
            }
#endif

        }
    }
}

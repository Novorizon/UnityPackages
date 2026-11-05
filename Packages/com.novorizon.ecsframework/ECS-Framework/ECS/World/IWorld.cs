using System.Collections.Generic;

namespace ECS
{
    public interface IWorld
    {
        public float TimePerFrame { get; set; }

        void InjectEntity(Entity entity);
        void Initialize();
        void Destroy();
    }
}

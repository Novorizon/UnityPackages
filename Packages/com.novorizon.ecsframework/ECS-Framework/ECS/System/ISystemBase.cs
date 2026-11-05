using System.Collections.Generic;
using UnityEngine;


namespace ECS
{
    public interface ISystemBase : IRecycleable, IInitializeable
    {
        void InjectEntity(Entity entity);
        void Update();
        void PreUpdate();
        void PostUpdate();

        IWorld World { get; set; }
    }
}

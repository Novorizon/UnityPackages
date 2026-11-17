using System.Collections.Generic;
using UnityEngine;


namespace ECS
{
    /// <summary>定义系统基础的调用契约。</summary>
    public interface ISystemBase : IRecycleable, IInitializeable
    {
        /// <summary>注入实体。</summary>
        void InjectEntity(Entity entity);
        /// <summary>推进该对象的一次更新。</summary>
        void Update();
        /// <summary>处理Pre 更新。</summary>
        void PreUpdate();
        /// <summary>投递更新。</summary>
        void PostUpdate();

        IWorld World { get; set; }
    }
}

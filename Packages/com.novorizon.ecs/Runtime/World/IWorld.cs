using System.Collections.Generic;

namespace ECS
{
    /// <summary>定义世界的调用契约。</summary>
    public interface IWorld
    {
        public float TimePerFrame { get; set; }

        /// <summary>注入实体。</summary>
        void InjectEntity(Entity entity);
        /// <summary>初始化世界。</summary>
        void Initialize();
        /// <summary>销毁世界。</summary>
        void Destroy();
    }
}

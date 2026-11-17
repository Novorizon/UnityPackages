using System.Collections.Generic;
using UnityEngine;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace ECS
{
    /// <summary>Optional component dependencies used to route structural changes.</summary>
    /// <remarks>定义系统筛选器的调用契约。</remarks>
    public interface ISystemFilter
    {
        IReadOnlyList<int> ComponentTypeIds { get; }
        bool RequiresGameObject { get; }
    }

    /// <summary>提供系统基础的实现。</summary>
    public abstract class SystemBase : ISystemBase
    {
#if ODIN_INSPECTOR && UNITY_EDITOR
        [SerializeField]
        protected bool showOdinInfo;
#endif

#if ODIN_INSPECTOR
        [ShowInInspector, ShowIf("showOdinInfo"), ListDrawerSettings(IsReadOnly = true)]
#endif
        protected readonly List<Entity> Entities = new List<Entity>();

        // Component lists in derived systems use exactly the same dense indices.
        protected readonly Dictionary<Entity, int> EntityIndices = new Dictionary<Entity, int>();

        public IWorld World { get; set; }

        /// <summary>添加实体。</summary>
        protected void AddEntity(Entity entity)
        {
            EntityIndices.Add(entity, Entities.Count);
            Entities.Add(entity);
        }

        /// <summary>Removes membership after derived component lists have been swapped.</summary>
        /// <remarks>移除实体 At。</remarks>
        protected void RemoveEntityAt(int index)
        {
            var removed = Entities[index];
            int last = Entities.Count - 1;
            if (index != last)
            {
                var moved = Entities[last];
                Entities[index] = moved;
                EntityIndices[moved] = index;
            }
            Entities.RemoveAt(last);
            EntityIndices.Remove(removed);
        }

        /// <summary>Overrides must call base to release framework-owned cached references.</summary>
        /// <remarks>响应 Recycle。</remarks>
        public virtual void OnRecycle()
        {
            try
            {
                ClearComponents();
            }
            finally
            {
                Entities.Clear();
                EntityIndices.Clear();
                World = null;
            }
        }

        /// <summary>清空组件。</summary>
        protected virtual void ClearComponents() { }

        /// <summary>响应已初始化。</summary>
        public virtual void OnInitialized() { }

        /// <summary>注入实体。</summary>
        public abstract void InjectEntity(Entity entity);

        // Drive systems through World to defer structural changes until the frame boundary.
        // Dense membership uses swap-remove; iteration order and indices are not stable.
        /// <summary>推进该对象的一次更新。</summary>
        public abstract void Update();

        /// <summary>处理Pre 更新。</summary>
        public virtual void PreUpdate() { }

        /// <summary>投递更新。</summary>
        public virtual void PostUpdate() { }
    }
}

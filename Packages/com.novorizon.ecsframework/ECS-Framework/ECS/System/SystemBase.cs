using System.Collections.Generic;
using UnityEngine;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif

namespace ECS
{
    /// <summary>Optional component dependencies used to route structural changes.</summary>
    public interface ISystemFilter
    {
        IReadOnlyList<int> ComponentTypeIds { get; }
        bool RequiresGameObject { get; }
    }

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

        protected void AddEntity(Entity entity)
        {
            EntityIndices.Add(entity, Entities.Count);
            Entities.Add(entity);
        }

        /// <summary>Removes membership after derived component lists have been swapped.</summary>
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

        protected virtual void ClearComponents() { }

        public virtual void OnInitialized() { }

        public abstract void InjectEntity(Entity entity);

        // Drive systems through World to defer structural changes until the frame boundary.
        // Dense membership uses swap-remove; iteration order and indices are not stable.
        public abstract void Update();

        public virtual void PreUpdate() { }

        public virtual void PostUpdate() { }
    }
}

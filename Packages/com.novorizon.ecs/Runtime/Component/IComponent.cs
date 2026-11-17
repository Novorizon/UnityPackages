using UnityEngine;

namespace ECS
{
    /// <summary>定义组件的调用契约。</summary>
    public interface IComponent
    {
    }

    /// <summary>提供组件包装器的实现。</summary>
    public class ComponentWrapper<T> : MonoBehaviour where T : class, IComponent, new()
    {
        private bool started;
        protected T self;
        protected Entity entity;

        /// <summary>启动组件包装器。</summary>
        protected virtual void Start()
        {
            entity = EntityManager.Create(gameObject);
            self = entity.GetOrAddComponentData<T>();
            started = true;
        }

        /// <summary>推进该对象的一次更新。</summary>
        private void Update()
        {
            if (!started)
                Start();
        }

        /// <summary>响应 Unity 组件停用。</summary>
        private void OnDisable()
        {
            started = false;
        }
    }
}

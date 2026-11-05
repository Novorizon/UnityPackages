using UnityEngine;

namespace ECS
{
    public interface IComponent
    {
    }

    public class ComponentWrapper<T> : MonoBehaviour where T : class, IComponent, new()
    {
        private bool started;
        protected T self;
        protected Entity entity;

        protected virtual void Start()
        {
            entity = EntityManager.Create(gameObject);
            self = entity.GetOrAddComponentData<T>();
            started = true;
        }

        private void Update()
        {
            if (!started)
                Start();
        }

        private void OnDisable()
        {
            started = false;
        }
    }
}

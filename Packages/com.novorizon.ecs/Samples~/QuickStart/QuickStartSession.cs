using System;

namespace ECS.Samples.QuickStart
{
    /// <summary>Owns one demo entity and one system; the shared default World owns the frame loop.</summary>
    public sealed class QuickStartSession : IDisposable
    {
        private static long nextOwnerId;
        private readonly long ownerId;
        private readonly float speed;
        private QuickStartMovementSystem movement;
        private bool disposed;
        public World World { get; private set; }
        public ulong EntityId { get; private set; }

        public QuickStartSession(float speed = 1)
        {
            if (float.IsNaN(speed) || float.IsInfinity(speed))
            {
                throw new ArgumentOutOfRangeException(nameof(speed));
            }

            this.speed = speed;
            ownerId = checked(++nextOwnerId);
            World = Ecs.Initialize();
            try
            {
                movement = World.RegisterSystem<QuickStartMovementSystem>();
                movement.OwnerId = ownerId;
                using (WorldManager.Instance.Batch())
                {
                    var entity = EntityManager.Create();
                    EntityId = entity.GUID;
                    EntityManager.Instance.AddComponentData<DemoOwner>(entity).Id = ownerId;
                    EntityManager.Instance.AddComponentData<DemoPosition>(entity).X = 0;
                    EntityManager.Instance.AddComponentData<DemoVelocity>(entity).Speed = speed;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public float Position
        {
            get
            {
                return EntityManager.Instance.GetComponentData<DemoPosition>(GetEntity()).X;
            }
        }

        public bool IsPaused
        {
            get
            {
                return EntityManager.Instance.GetComponentData<DemoVelocity>(GetEntity()) == null;
            }
        }

        public void Pause()
        {
            EntityManager.Instance.RemoveComponentData<DemoVelocity>(GetEntity());
        }

        public void Resume()
        {
            var entity = GetEntity();
            if (EntityManager.Instance.GetComponentData<DemoVelocity>(entity) == null)
            {
                EntityManager.Instance.AddComponentData<DemoVelocity>(entity).Speed = speed;
            }
        }

        private Entity GetEntity()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(QuickStartSession));
            }

            if (!EntityManager.Instance.TryGetEntity(EntityId, out var entity))
            {
                throw new InvalidOperationException("This demo entity has been removed.");
            }

            return entity;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            // Unity shuts down managers in unspecified order; do not create them during shutdown.
            if (Ecs.IsQuitting || EntityManager.applicationIsQuitting || WorldManager.applicationIsQuitting || ReferencePool.applicationIsQuitting)
            {
                return;
            }

            try
            {
                if (EntityId != 0)
                {
                    EntityManager.Instance.DestroyEntity(EntityId);
                }
            }
            finally
            {
                // A GUID identifies our entity; the owner token also guards a reused system reference.
                if (movement != null && movement.OwnerId == ownerId)
                {
                    World.RemoveSystem(movement);
                }

                movement = null;
            }
        }
    }
}

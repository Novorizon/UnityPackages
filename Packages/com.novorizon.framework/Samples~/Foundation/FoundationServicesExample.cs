using System;
using Game.Framework.Pooling;
using Game.Framework.Timing;
using UnityEngine;

namespace Game.Framework.Samples
{
    public sealed class ExampleDamageContext : IReferencePoolable
    {
        public GameObject Target;
        public int Damage;
        public void Reset() { Target = null; Damage = 0; }
    }

    // Opt-in sample: call SpawnExample explicitly. No automatic spawning or network access.
    public sealed class FoundationServicesExample : MonoBehaviour
    {
        [SerializeField] private GameObject prefab = null;
        private GameObjectPool pool;
        private GameObject spawned;
        private TimerHandle despawnTimer;
        private Action despawnAction;

        public void SpawnExample()
        {
            if (prefab == null) throw new InvalidOperationException("Assign a prefab first.");
            if (pool == null) { pool = new GameObjectPool(prefab, maximumRetained: 32, prewarm: 8); despawnAction = Despawn; }
            despawnTimer.Cancel(); Despawn();
            spawned = pool.Spawn();
            var context = ReferencePool<ExampleDamageContext>.Shared.Rent();
            try { context.Target = spawned; context.Damage = 10; /* Call the game's damage logic here. */ }
            finally { ReferencePool<ExampleDamageContext>.Shared.Return(context); }
            despawnTimer = TimerManager.Instance.Schedule(2, despawnAction);
        }
        private void Despawn()
        {
            if (spawned == null) return;
            var value = spawned; spawned = null; pool.Despawn(value);
        }
        private void OnDestroy()
        {
            despawnTimer.Cancel(); pool?.Dispose(); spawned = null;
        }
    }
}

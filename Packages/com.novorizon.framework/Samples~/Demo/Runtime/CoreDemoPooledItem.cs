using Game.Framework.Pooling;
using UnityEngine;

namespace Game.Framework.Samples.Demo
{
    public sealed class CoreDemoPooledItem : MonoBehaviour, IGameObjectPoolCallbacks
    {
        [SerializeField]
        int spawnCount = 0;
        [SerializeField]
        int despawnCount = 0;
        [SerializeField]
        bool activeForUse = false;
        public int SpawnCount => spawnCount;
        public int DespawnCount => despawnCount;
        public bool ActiveForUse => activeForUse;

        public void OnSpawn()
        {
            spawnCount++;
            activeForUse = true;
        }

        public void OnDespawn()
        {
            despawnCount++;
            activeForUse = false;
        }
    }
}

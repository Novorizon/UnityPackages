using Game.Framework.Pooling;
using UnityEngine;

namespace Game.Framework.Samples.Demo
{
    /// <summary>提供核心示例已入池条目的实现。</summary>
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

        /// <summary>响应 Spawn。</summary>
        public void OnSpawn()
        {
            spawnCount++;
            activeForUse = true;
        }

        /// <summary>响应 Despawn。</summary>
        public void OnDespawn()
        {
            despawnCount++;
            activeForUse = false;
        }
    }
}

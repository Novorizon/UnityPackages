using Game.Framework.Pooling;
using UnityEngine;

internal static partial class Program
{
    private static void PoolDiagnosticScenarios()
    {
        Run("pool diagnostics follow prewarm, spawn, return and disposal", () =>
        {
            int pools = GameObjectPool.PoolCount;
            long active = GameObjectPool.TotalActiveCount;
            long cached = GameObjectPool.TotalInactiveCount;
            var prefab = new GameObject("Pool diagnostics prefab");
            using (var first = new GameObjectPool(prefab, 2, 2))
            using (var second = new GameObjectPool(prefab, 1, 1))
            {
                Equal(pools + 2, GameObjectPool.PoolCount);
                Equal(cached + 3, GameObjectPool.TotalInactiveCount);
                var item = first.Spawn();
                Equal(active + 1, GameObjectPool.TotalActiveCount);
                Equal(cached + 2, GameObjectPool.TotalInactiveCount);
                first.Despawn(item);
                Equal(active, GameObjectPool.TotalActiveCount);
                first.Dispose();
                Equal(pools + 1, GameObjectPool.PoolCount);
                Equal(cached + 1, GameObjectPool.TotalInactiveCount);
            }

            Equal(pools, GameObjectPool.PoolCount);
            Equal(active, GameObjectPool.TotalActiveCount);
            Equal(cached, GameObjectPool.TotalInactiveCount);
        });
    }
}

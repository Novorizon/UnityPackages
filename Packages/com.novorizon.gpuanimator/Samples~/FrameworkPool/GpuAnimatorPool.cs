using System;
using System.Collections.Generic;
using Game.Framework.Pooling;
using UnityEngine;

namespace GpuAnimatorSamples
{
    // Optional sample: Framework owns the generic pool; this adapter owns playback references.
    public sealed class GpuAnimatorPool : IDisposable
    {
        private GameObjectPool pool;
        private AnimationData data;
        private readonly Dictionary<GpuAnimator, GameObject> active = new Dictionary<GpuAnimator, GameObject>();
        public int ActiveCount => active.Count;

        public GpuAnimatorPool(GameObject loadedPrefab, AnimationData sharedData, int maximumRetained = 32)
        {
            data = sharedData ?? throw new ArgumentNullException(nameof(sharedData));
            pool = new GameObjectPool(loadedPrefab, maximumRetained);
        }

        public GpuAnimator Spawn(string clipName, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (pool == null)
            {
                throw new ObjectDisposedException(nameof(GpuAnimatorPool));
            }

            GameObject instance = pool.Spawn(position, rotation, parent);
            GpuAnimator player = null;
            try
            {
                Renderer renderer = instance.GetComponentInChildren<Renderer>(true);
                if (renderer == null)
                {
                    throw new ArgumentException("The loaded prefab needs its baked Renderer.");
                }

                player = renderer.GetComponent<GpuAnimator>() ?? renderer.gameObject.AddComponent<GpuAnimator>();
                if (!player.Initialize(renderer, data) || !player.TryPlay(clipName))
                {
                    throw new ArgumentException("The baked data or requested clip is invalid.");
                }

                player.AutomaticUpdate = true;
                active.Add(player, instance);
                return player;
            }
            catch
            {
                if (player != null)
                {
                    player.Clear();
                }

                pool.Despawn(instance);
                throw;
            }
        }

        public void Recycle(GpuAnimator player)
        {
            if (pool == null)
            {
                throw new ObjectDisposedException(nameof(GpuAnimatorPool));
            }

            if (player == null || !active.TryGetValue(player, out GameObject instance))
            {
                throw new InvalidOperationException("Recycle one active player through its owning pool.");
            }

            active.Remove(player);
            player.Clear();
            pool.Despawn(instance);
        }

        public void Dispose()
        {
            if (pool == null)
            {
                return;
            }

            foreach (GpuAnimator player in active.Keys)
            {
                if (player != null)
                {
                    player.Clear();
                }
            }

            active.Clear();
            data = null;
            var previous = pool;
            pool = null;
            previous.Dispose();
        }
    }
}

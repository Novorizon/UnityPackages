using System;
using System.Threading;
using UnityEngine;

namespace ECS
{
    public enum GameObjectOwnership { Owned, Borrowed }

    /// <summary>A single instantiated object and the exact action that releases it.</summary>
    public sealed class GameObjectLease : IDisposable
    {
        private readonly Action<GameObject> release;
        private int disposed;

        private GameObjectLease(GameObject gameObject, Action<GameObject> release)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            GameObject = gameObject;
            this.release = release;
        }

        public GameObject GameObject { get; private set; }
        public bool IsDisposed { get { return Volatile.Read(ref disposed) != 0; } }

        public static GameObjectLease Owned(GameObject gameObject, Action<GameObject> release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            return new GameObjectLease(gameObject, release);
        }

        public static GameObjectLease Borrowed(GameObject gameObject)
        {
            return new GameObjectLease(gameObject, null);
        }

        internal static GameObjectLease Owned(GameObject gameObject)
        {
            return Owned(gameObject, value => UnityEngine.Object.Destroy(value));
        }

        /// <summary>Invoke on Unity's main thread. Release is attempted at most once, even if it throws.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) release?.Invoke(GameObject);
        }
    }

    /// <summary>The provider applies either Parent or the requested world-space pose when instantiating.</summary>
    public struct GameObjectRequest
    {
        public string AssetPath { get; private set; }
        public Transform Parent { get; private set; }
        public bool HasPose { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }

        public GameObjectRequest(string assetPath, Transform parent)
        {
            if (string.IsNullOrEmpty(assetPath)) throw new ArgumentException("An asset path is required.", nameof(assetPath));
            AssetPath = assetPath;
            Parent = parent;
            HasPose = false;
            Position = default(Vector3);
            Rotation = default(Quaternion);
        }

        public GameObjectRequest(string assetPath, Vector3 position, Quaternion rotation)
        {
            if (string.IsNullOrEmpty(assetPath)) throw new ArgumentException("An asset path is required.", nameof(assetPath));
            AssetPath = assetPath;
            Parent = null;
            HasPose = true;
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Creates an independent instance and calls completed once on Unity's main thread.
    /// Pass null on failure. Calling completed on the main thread transfers the lease to ECS,
    /// including when a subsequent ECS/user callback throws; the provider must not release it again.
    /// A worker-thread call without a captured Unity synchronization context is rejected before transfer.
    /// The release action must capture the creating provider, rather than a mutable singleton.
    /// </summary>
    public interface IGameObjectProvider
    {
        void LoadAsync(GameObjectRequest request, Action<GameObjectLease> completed);
    }
}

using System;
using System.Threading;
using UnityEngine;

namespace ECS
{
    /// <summary>定义GameObject 所有权可选的枚举值。</summary>
    public enum GameObjectOwnership { Owned, Borrowed }

    /// <summary>A single instantiated object and the exact action that releases it.</summary>
    /// <remarks>GameObject对应的租约。记录资源的使用权，由持有者在使用结束时归还。</remarks>
    public sealed class GameObjectLease : IDisposable
    {
        private readonly Action<GameObject> release;
        private int disposed;

        /// <summary>使用调用方提供的gameObject、release创建 GameObjectLease。</summary>
        private GameObjectLease(GameObject gameObject, Action<GameObject> release)
        {
            if (gameObject == null) throw new ArgumentNullException(nameof(gameObject));
            GameObject = gameObject;
            this.release = release;
        }

        public GameObject GameObject { get; private set; }
        public bool IsDisposed { get { return Volatile.Read(ref disposed) != 0; } }

        /// <summary>根据传入参数取得自有。</summary>
        public static GameObjectLease Owned(GameObject gameObject, Action<GameObject> release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            return new GameObjectLease(gameObject, release);
        }

        /// <summary>根据传入参数取得Borrowed。</summary>
        public static GameObjectLease Borrowed(GameObject gameObject)
        {
            return new GameObjectLease(gameObject, null);
        }

        /// <summary>根据传入参数取得自有。</summary>
        internal static GameObjectLease Owned(GameObject gameObject)
        {
            return Owned(gameObject, value => UnityEngine.Object.Destroy(value));
        }

        /// <summary>Invoke on Unity's main thread. Release is attempted at most once, even if it throws.</summary>
        /// <remarks>结束GameObject 租约对相关资源的持有，并执行该类型的清理流程。</remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) release?.Invoke(GameObject);
        }
    }

    /// <summary>The provider applies either Parent or the requested world-space pose when instantiating.</summary>
    /// <remarks>保存GameObject 请求所需的数据与配套操作。</remarks>
    public struct GameObjectRequest
    {
        public string AssetPath { get; private set; }
        public Transform Parent { get; private set; }
        public bool HasPose { get; private set; }
        public Vector3 Position { get; private set; }
        public Quaternion Rotation { get; private set; }

        /// <summary>使用调用方提供的assetPath、parent创建 GameObjectRequest。</summary>
        public GameObjectRequest(string assetPath, Transform parent)
        {
            if (string.IsNullOrEmpty(assetPath)) throw new ArgumentException("An asset path is required.", nameof(assetPath));
            AssetPath = assetPath;
            Parent = parent;
            HasPose = false;
            Position = default(Vector3);
            Rotation = default(Quaternion);
        }

        /// <summary>使用调用方提供的assetPath、position、rotation创建 GameObjectRequest。</summary>
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
    /// <remarks>定义GameObject 提供器的调用契约。</remarks>
    public interface IGameObjectProvider
    {
        /// <summary>异步加载 GameObject 提供器。</summary>
        void LoadAsync(GameObjectRequest request, Action<GameObjectLease> completed);
    }
}

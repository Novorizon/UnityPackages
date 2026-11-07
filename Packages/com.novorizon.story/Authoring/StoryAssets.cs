using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Framework.Story.Unity
{
    /// <summary>Project asset providers acquire one independent lease per request.</summary>
    public interface IStoryAssets
    {
        Task<StoryAssetLease<T>> LoadAsync<T>(string id, CancellationToken cancellationToken) where T : UnityEngine.Object;
    }

    public interface IStoryVideo
    {
        Task PlayAsync(string resourceId, float prepareTimeoutSeconds, bool canSkip, CancellationToken cancellationToken);
        bool SkipCurrent();
        void Stop();
    }

    /// <summary>Dispose on Unity's main thread. The project decides how its underlying handle is released.</summary>
    public sealed class StoryAssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        private T asset;
        private Action release;
        public bool IsDisposed { get; private set; }
        public T Asset => !IsDisposed && asset != null ? asset : throw new ObjectDisposedException(nameof(StoryAssetLease<T>));

        public StoryAssetLease(T value, Action release = null)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            asset = value;
            this.release = release;
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }
            IsDisposed = true;
            asset = null;
            Action callback = Interlocked.Exchange(ref release, null);
            callback?.Invoke();
        }
    }

}

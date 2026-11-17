using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Framework.Story.Unity
{
    /// <summary>Project asset providers acquire one independent lease per request.</summary>
    /// <remarks>定义剧情资源的调用契约。</remarks>
    public interface IStoryAssets
    {
        /// <summary>异步加载剧情资源。</summary>
        Task<StoryAssetLease<T>> LoadAsync<T>(string id, CancellationToken cancellationToken) where T : UnityEngine.Object;
    }

    /// <summary>定义剧情视频的调用契约。</summary>
    public interface IStoryVideo
    {
        /// <summary>异步播放剧情视频。</summary>
        Task PlayAsync(string resourceId, float prepareTimeoutSeconds, bool canSkip, CancellationToken cancellationToken);
        /// <summary>跳过当前。</summary>
        bool SkipCurrent();
        /// <summary>停止剧情视频。</summary>
        void Stop();
    }

    /// <summary>Dispose on Unity's main thread. The project decides how its underlying handle is released.</summary>
    /// <remarks>剧情资源对应的租约。记录资源的使用权，由持有者在使用结束时归还。</remarks>
    public sealed class StoryAssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        private T asset;
        private Action release;
        public bool IsDisposed { get; private set; }
        public T Asset => !IsDisposed && asset != null ? asset : throw new ObjectDisposedException(nameof(StoryAssetLease<T>));

        /// <summary>使用调用方提供的value、release创建 StoryAssetLease。</summary>
        public StoryAssetLease(T value, Action release = null)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }
            asset = value;
            this.release = release;
        }

        /// <summary>结束剧情资源租约对相关资源的持有，并执行该类型的清理流程。</summary>
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

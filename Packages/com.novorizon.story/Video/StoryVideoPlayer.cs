using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Story.Unity;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;
using Debug = UnityEngine.Debug;

namespace Game.Framework.Story.Video
{
    /// <summary>One owned native video operation at a time. Configure with a project asset provider.</summary>
    /// <remarks>
    /// Keep this component active while a video runs; the output RawImage may be shown/hidden separately.
    /// Stop cancels the operation. Await the returned task before starting another video or releasing its owner.
    /// The default direct audio output has its own volume; projects needing mixer routing can supply another IStoryVideo.
    /// </remarks>
    /// <remarks>提供剧情视频 Player的实现。</remarks>
    [DisallowMultipleComponent]
    public sealed class StoryVideoPlayer : MonoBehaviour, IStoryVideo
    {
        /// <summary>表示剧情视频 Player中的操作。</summary>
        private sealed class Operation
        {
            internal readonly CancellationTokenSource Cancellation;
            internal readonly CancellationToken CallerToken;
            internal readonly TaskCompletionSource<bool> Prepared = Signal();
            internal readonly TaskCompletionSource<bool> FirstFrame = Signal();
            internal readonly TaskCompletionSource<bool> Finished = Signal();
            internal readonly bool CanSkip;
            internal StoryAssetLease<VideoClip> Lease;
            internal GameObject Object;
            internal VideoPlayer Player;
            internal RenderTexture Texture;
            internal Texture PreviousImage;
            internal float PreviousAspect;
            internal bool HadAspect, ImagePublished, Playing, SkipRequested, StopRequested, Cleaned;
            internal long FrameCount;
            internal double Deadline;
            internal string TimeoutMessage;
            internal Exception Failure, CleanupFailure;
            internal VideoPlayer.EventHandler PreparedHandler, EndHandler;
            internal VideoPlayer.ErrorEventHandler ErrorHandler;
            internal VideoPlayer.FrameReadyEventHandler FrameHandler;

            /// <summary>使用调用方提供的token、canSkip创建 Operation。</summary>
            internal Operation(CancellationToken token, bool canSkip)
            {
                CallerToken = token;
                CanSkip = canSkip;
                Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            }

            /// <summary>取得信号。</summary>
            private static TaskCompletionSource<bool> Signal()
            {
                return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        [SerializeField] private RawImage output;
        [SerializeField] private AspectRatioFitter aspect;
        [SerializeField] private StoryAssetLibrary assetLibrary;
        [SerializeField, Range(0, 1)] private float volume = 1;
        [SerializeField] private bool mute;
        [SerializeField, Min(16)] private int maximumTextureDimension = 4096;
        private IStoryAssets assets;
        private Operation current;
        private bool destroyed;

        public bool IsBusy => current != null;
        public RenderTexture OutputTexture => current?.Texture;
        public long DecodedFrameCount => current?.FrameCount ?? 0;

        /// <summary>配置剧情视频 Player。</summary>
        public void Configure(IStoryAssets provider)
        {
            MainThread();
            ThrowIfDestroyed();
            if (current != null)
            {
                throw new InvalidOperationException("Stop and await the current video before reconfiguring it.");
            }
            assets = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>配置图像。</summary>
        public void ConfigureImage(RawImage image, AspectRatioFitter ratio = null)
        {
            MainThread();
            ThrowIfDestroyed();
            if (current != null)
            {
                throw new InvalidOperationException("Stop and await the current video before replacing its output.");
            }
            output = image;
            aspect = ratio;
        }

        /// <summary>异步播放剧情视频 Player。</summary>
        public async Task PlayAsync(
            string resourceId,
            float prepareTimeoutSeconds,
            bool canSkip,
            CancellationToken cancellationToken)
        {
            MainThread();
            ThrowIfDestroyed();
            if (!isActiveAndEnabled)
            {
                throw new InvalidOperationException("Keep the story video component active while playing.");
            }
            IStoryAssets provider = assets ?? assetLibrary;
            if (provider == null)
            {
                throw new InvalidOperationException("Configure story video assets before playing.");
            }
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ArgumentException("A video asset ID is required.", nameof(resourceId));
            }
            if (!Finite(prepareTimeoutSeconds) || prepareTimeoutSeconds <= 0 || prepareTimeoutSeconds > 86400)
            {
                throw new ArgumentOutOfRangeException(nameof(prepareTimeoutSeconds));
            }
            if (current != null)
            {
                throw new InvalidOperationException("A story video is already active. Stop and await it before replaying.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            var operation = new Operation(cancellationToken, canSkip);
            current = operation;
            Exception failure = null;
            try
            {
                var acquired = await AcquireAsync(provider, resourceId, operation.Cancellation.Token);
                try
                {
                    // Destruction can run after the provider completes but before this Unity
                    // continuation. Never attach a late lease to an already cleaned operation.
                    operation.Cancellation.Token.ThrowIfCancellationRequested();
                    if (operation.Cleaned)
                    {
                        throw new OperationCanceledException(operation.Cancellation.Token);
                    }
                    operation.Lease = acquired;
                    acquired = null;
                }
                finally
                {
                    acquired?.Dispose();
                }
                CreatePlayer(operation);
                operation.Player.clip = operation.Lease.Asset;
                SetDeadline(operation, prepareTimeoutSeconds, "Video preparation or first frame timed out: " + resourceId);
                operation.Player.Prepare();
                ThrowVideoFailure(operation);
                await TaskUtil.WaitAsync(operation.Prepared.Task, operation.Cancellation.Token);
                ThrowVideoFailure(operation);
                CreateTarget(operation);
                ThrowVideoFailure(operation);
                operation.Playing = true;
                operation.Player.Play();
                await TaskUtil.WaitAsync(operation.FirstFrame.Task, operation.Cancellation.Token);
                ThrowVideoFailure(operation);
                operation.Deadline = 0;
                if (operation.FrameCount == 0)
                {
                    throw new InvalidOperationException("Video ended before decoding a frame: " + resourceId);
                }
                double length = operation.Player.length;
                if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0)
                {
                    throw new InvalidOperationException("Video has no finite playback duration: " + resourceId);
                }
                // A local clip has a known length. Bound stalled native playback without relying
                // on a completion callback that a failed decoder may never deliver.
                double watchdogSeconds = Math.Min(length + prepareTimeoutSeconds + 5, 2147480);
                SetDeadline(operation, watchdogSeconds, "Video playback stalled: " + resourceId);
                PublishImage(operation);
                await TaskUtil.WaitAsync(operation.Finished.Task, operation.Cancellation.Token);
                ThrowVideoFailure(operation);
            }
            catch (OperationCanceledException error)
            {
                if (!operation.SkipRequested || operation.StopRequested || destroyed || operation.CallerToken.IsCancellationRequested)
                {
                    failure = error;
                }
                else if (operation.Failure != null)
                {
                    failure = operation.Failure;
                }
            }
            catch (Exception error)
            {
                failure = error;
            }
            finally
            {
                Cleanup(operation);
                if (ReferenceEquals(current, operation))
                {
                    current = null;
                }
                operation.Cancellation.Dispose();
                if (operation.CleanupFailure != null)
                {
                    failure = failure == null
                        ? operation.CleanupFailure
                        : new AggregateException(failure, operation.CleanupFailure);
                }
            }
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        /// <summary>跳过当前。</summary>
        public bool SkipCurrent()
        {
            MainThread();
            Operation operation = current;
            if (operation == null || !operation.CanSkip || operation.StopRequested || operation.CallerToken.IsCancellationRequested)
            {
                return false;
            }
            operation.SkipRequested = true;
            Cancel(operation);
            return true;
        }

        /// <summary>停止剧情视频 Player。</summary>
        public void Stop()
        {
            MainThread();
            RequestStop();
        }

        /// <summary>推进该对象的一次更新。</summary>
        private void Update()
        {
            if (current != null)
            {
                ExpireDeadline(current);
            }
        }

        /// <summary>响应 Unity 组件停用。</summary>
        private void OnDisable()
        {
            RequestStop();
        }

        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        private void OnDestroy()
        {
            destroyed = true;
            RequestStop();
            if (current != null)
            {
                Cleanup(current);
            }
            assets = null;
        }

        /// <summary>调用 Cancel 完成请求 Stop。</summary>
        private void RequestStop()
        {
            if (current == null)
            {
                return;
            }
            current.StopRequested = true;
            Cancel(current);
        }

        /// <summary>取消剧情视频 Player。</summary>
        private static void Cancel(Operation operation)
        {
            try
            {
                operation.Cancellation.Cancel();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        /// <summary>异步取得剧情视频 Player。</summary>
        private static async Task<StoryAssetLease<VideoClip>> AcquireAsync(
            IStoryAssets provider,
            string resourceId,
            CancellationToken token)
        {
            Task<StoryAssetLease<VideoClip>> loading = provider.LoadAsync<VideoClip>(resourceId, token);
            if (loading == null)
            {
                throw new InvalidOperationException("Story asset provider returned no video task.");
            }
            try
            {
                var lease = await TaskUtil.WaitAsync(loading, token);
                return lease ?? throw new InvalidOperationException("Story asset provider returned no video lease.");
            }
            catch
            {
                ReleaseLateAsync(loading).Forget();
                throw;
            }
        }

        /// <summary>异步释放延后。</summary>
        private static async Task ReleaseLateAsync(Task<StoryAssetLease<VideoClip>> loading)
        {
            StoryAssetLease<VideoClip> lease;
            try
            {
                lease = await loading;
            }
            catch
            {
                return;
            }
            lease?.Dispose();
        }

        /// <summary>创建 Player。</summary>
        private void CreatePlayer(Operation operation)
        {
            operation.Object = new GameObject("Story video operation");
            operation.Object.hideFlags = HideFlags.DontSave;
            operation.Object.transform.SetParent(transform, false);
            operation.Player = operation.Object.AddComponent<VideoPlayer>();
            var player = operation.Player;
            player.playOnAwake = false;
            player.isLooping = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = mute ? VideoAudioOutputMode.None : VideoAudioOutputMode.Direct;
            player.sendFrameReadyEvents = true;
            operation.PreparedHandler = value =>
            {
                if (IsCurrent(operation, value))
                {
                    operation.Prepared.TrySetResult(true);
                }
            };
            operation.EndHandler = value =>
            {
                if (IsCurrent(operation, value) && operation.Playing)
                {
                    operation.FirstFrame.TrySetResult(true);
                    operation.Finished.TrySetResult(true);
                }
            };
            operation.ErrorHandler = (value, message) =>
            {
                if (IsCurrent(operation, value))
                {
                    operation.Failure = operation.Failure ?? new InvalidOperationException("VideoPlayer failed: " + message);
                    operation.Prepared.TrySetResult(true);
                    operation.FirstFrame.TrySetResult(true);
                    operation.Finished.TrySetResult(true);
                }
            };
            operation.FrameHandler = (value, frame) =>
            {
                if (IsCurrent(operation, value) && operation.Playing)
                {
                    operation.FrameCount++;
                    operation.FirstFrame.TrySetResult(true);
                }
            };
            player.prepareCompleted += operation.PreparedHandler;
            player.loopPointReached += operation.EndHandler;
            player.errorReceived += operation.ErrorHandler;
            player.frameReady += operation.FrameHandler;
        }

        /// <summary>检查输入是否仍是当前有效值。</summary>
        private bool IsCurrent(Operation operation, VideoPlayer player)
        {
            return !destroyed && ReferenceEquals(current, operation) && !operation.Cleaned
                && !operation.Cancellation.IsCancellationRequested && ReferenceEquals(operation.Player, player);
        }

        /// <summary>创建目标。</summary>
        private void CreateTarget(Operation operation)
        {
            var player = operation.Player;
            ulong width = player.width;
            ulong height = player.height;
            int limit = Math.Min(Math.Max(16, maximumTextureDimension), SystemInfo.maxTextureSize);
            if (width == 0 || height == 0 || width > (ulong)limit || height > (ulong)limit)
            {
                throw new InvalidOperationException("Video dimensions exceed the configured render target limit.");
            }
            operation.Texture = new RenderTexture((int)width, (int)height, 0, RenderTextureFormat.ARGB32);
            operation.Texture.name = "Story video texture";
            operation.Texture.hideFlags = HideFlags.DontSave;
            if (!operation.Texture.Create())
            {
                throw new InvalidOperationException("Could not create the story video render target.");
            }
            player.targetTexture = operation.Texture;
            if (!mute && player.audioTrackCount != 0 && player.canSetDirectAudioVolume)
            {
                player.SetDirectAudioVolume(0, Mathf.Clamp01(volume));
            }
        }

        /// <summary>发布图像。</summary>
        private void PublishImage(Operation operation)
        {
            if (output == null)
            {
                return;
            }
            operation.PreviousImage = output.texture;
            output.texture = operation.Texture;
            operation.ImagePublished = true;
            if (aspect != null)
            {
                operation.HadAspect = true;
                operation.PreviousAspect = aspect.aspectRatio;
                aspect.aspectRatio = operation.Texture.width / (float)operation.Texture.height;
            }
        }

        /// <summary>调用 Attempt 完成清理。</summary>
        private void Cleanup(Operation operation)
        {
            if (operation.Cleaned)
            {
                return;
            }
            operation.Cleaned = true;
            var failures = new List<Exception>();
            if (operation.Player != null)
            {
                Attempt(() =>
                {
                    operation.Player.prepareCompleted -= operation.PreparedHandler;
                    operation.Player.loopPointReached -= operation.EndHandler;
                    operation.Player.errorReceived -= operation.ErrorHandler;
                    operation.Player.frameReady -= operation.FrameHandler;
                }, failures);
                Attempt(() => operation.Player.Stop(), failures);
                Attempt(() => operation.Player.targetTexture = null, failures);
                Attempt(() => operation.Player.clip = null, failures);
            }
            if (operation.ImagePublished && output != null && output.texture == operation.Texture)
            {
                Attempt(() => output.texture = operation.PreviousImage, failures);
                if (operation.HadAspect && aspect != null)
                {
                    Attempt(() => aspect.aspectRatio = operation.PreviousAspect, failures);
                }
            }
            if (operation.Texture != null)
            {
                Attempt(() => operation.Texture.Release(), failures);
                Attempt(() => Object.Destroy(operation.Texture), failures);
                operation.Texture = null;
            }
            if (operation.Object != null)
            {
                Attempt(() => Object.Destroy(operation.Object), failures);
            }
            if (operation.Lease != null)
            {
                Attempt(operation.Lease.Dispose, failures);
                operation.Lease = null;
            }
            if (failures.Count != 0)
            {
                operation.CleanupFailure = new AggregateException("Story video cleanup failed.", failures);
            }
        }

        /// <summary>调用 action 完成Attempt。</summary>
        private static void Attempt(Action action, List<Exception> failures)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }

        /// <summary>设置截止时间。</summary>
        private static void SetDeadline(Operation operation, double seconds, string message)
        {
            operation.Deadline = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency + seconds;
            operation.TimeoutMessage = message;
        }

        /// <summary>调用 ReferenceEquals 完成Expire 截止时间。</summary>
        private void ExpireDeadline(Operation operation)
        {
            if (!ReferenceEquals(current, operation) || operation.Cleaned || operation.Cancellation.IsCancellationRequested ||
                operation.Failure != null || operation.Deadline <= 0 ||
                Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency < operation.Deadline)
            {
                return;
            }
            operation.Deadline = 0;
            operation.Failure = new TimeoutException(operation.TimeoutMessage);
            // Native callbacks and managed timers can both be delayed. A monotonic deadline
            // wakes every possible phase without changing operation/resource ownership.
            operation.Prepared.TrySetResult(true);
            operation.FirstFrame.TrySetResult(true);
            operation.Finished.TrySetResult(true);
        }

        /// <summary>抛出异常视频失败原因。</summary>
        private void ThrowVideoFailure(Operation operation)
        {
            operation.Cancellation.Token.ThrowIfCancellationRequested();
            ExpireDeadline(operation);
            if (operation.Failure != null)
            {
                ExceptionDispatchInfo.Capture(operation.Failure).Throw();
            }
        }

        /// <summary>返回Finite的判断结果。</summary>
        private static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>校验当前调用位于 Unity 主线程。</summary>
        private static void MainThread()
        {
            if (!UnityThread.IsInitialized || !UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use StoryVideoPlayer on the initialized Unity main thread.");
            }
        }

        /// <summary>在已销毁时抛出异常。</summary>
        private void ThrowIfDestroyed()
        {
            if (destroyed)
            {
                throw new ObjectDisposedException(nameof(StoryVideoPlayer));
            }
        }
    }
}

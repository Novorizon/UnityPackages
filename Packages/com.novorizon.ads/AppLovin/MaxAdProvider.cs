using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Ads.AppLovin
{
    /// <summary>MAX 8.6.6 rewarded/interstitial adapter. Configure the SDK key with MAX Integration Manager.</summary>
    /// <remarks>上限广告对应的厂商适配器。提供此模块需要的能力或数据。</remarks>
    public sealed class MaxAdProvider : IAdProvider
    {
        private static MaxAdProvider owner;
        private readonly bool userConsent;
        private readonly string userId;
        /// <summary>表示上限广告厂商适配器中的Show。</summary>
        private sealed class Show
        {
            internal AdPlacement Placement;
            internal bool Rewarded;
        }
        private readonly Dictionary<string, Show> shows = new Dictionary<string, Show>(StringComparer.Ordinal);
        private readonly Queue<string> history = new Queue<string>();
        private TaskCompletionSource<bool> initialization, loading, showing;
        private AdPlacement loadingPlacement, showingPlacement;
        private string showId;
        private bool disposed, subscribed;
        public string Id => "applovin-max";
        public event Action<AdReward> RewardEarned;

        /// <summary>使用调用方提供的hasUserConsent、rewardUserId创建 MaxAdProvider。</summary>
        public MaxAdProvider(bool hasUserConsent, string rewardUserId = "")
        {
            userConsent = hasUserConsent;
            userId = rewardUserId ?? "";
        }

        /// <summary>异步初始化上限广告厂商适配器。</summary>
        public async Task InitializeAsync(CancellationToken token)
        {
            ReadyForCall();
            token.ThrowIfCancellationRequested();
            if (owner != null && !ReferenceEquals(owner, this))
            {
                throw new InvalidOperationException("Another adapter owns the shared MAX SDK.");
            }
            owner = this;
            Subscribe();
            MaxSdk.SetHasUserConsent(userConsent);
            MaxSdk.SetUserId(userId);
            if (MaxSdk.IsInitialized())
            {
                return;
            }
            if (initialization == null)
            {
                initialization = Signal();
                MaxSdk.InitializeSdk();
            }
            await initialization.Task;
            token.ThrowIfCancellationRequested();
        }

        /// <summary>异步加载上限广告厂商适配器。</summary>
        public async Task LoadAsync(AdPlacement placement, CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (placement == null || loading != null || showing != null)
            {
                throw new InvalidOperationException("Provide a placement and wait for the active MAX operation.");
            }
            loadingPlacement = placement;
            loading = Signal();
            try
            {
                if (placement.Format == AdFormat.Rewarded)
                {
                    MaxSdk.LoadRewardedAd(placement.UnitId);
                }
                else
                {
                    MaxSdk.LoadInterstitial(placement.UnitId);
                }
                await loading.Task;
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                loading = null;
                loadingPlacement = null;
            }
        }

        /// <summary>检查当前资源或操作是否已就绪。</summary>
        public bool IsReady(AdPlacement placement)
        {
            Ready();
            if (placement == null)
            {
                throw new ArgumentNullException(nameof(placement));
            }
            return placement.Format == AdFormat.Rewarded ? MaxSdk.IsRewardedAdReady(placement.UnitId) : MaxSdk.IsInterstitialReady(placement.UnitId);
        }

        /// <summary>异步显示上限广告厂商适配器。</summary>
        public async Task ShowAsync(AdPlacement placement, CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (showing != null || loading != null || !IsReady(placement))
            {
                throw new InvalidOperationException("MAX ad is busy or not ready.");
            }
            showingPlacement = placement;
            showId = Guid.NewGuid().ToString("N");
            shows.Add(showId, new Show { Placement = placement });
            showing = Signal();
            try
            {
                if (placement.Format == AdFormat.Rewarded)
                {
                    // MAX callback AdInfo exposes placement, but not customData. Use a unique placement
                    // to correlate a late reward to this show; business placement stays in our event.
                    MaxSdk.ShowRewardedAd(placement.UnitId, showId, showId);
                }
                else
                {
                    MaxSdk.ShowInterstitial(placement.UnitId, showId, showId);
                }
                await showing.Task;
            }
            finally
            {
                history.Enqueue(showId);
                if (history.Count > 256)
                {
                    shows.Remove(history.Dequeue());
                }
                showing = null;
                showingPlacement = null;
                showId = null;
            }
        }

        /// <summary>订阅上限广告厂商适配器。</summary>
        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }
            subscribed = true;
            MaxSdkCallbacks.OnSdkInitializedEvent += Initialized;
            MaxSdkCallbacks.Rewarded.OnAdLoadedEvent += Loaded;
            MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent += LoadFailed;
            MaxSdkCallbacks.Rewarded.OnAdHiddenEvent += Hidden;
            MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent += DisplayFailed;
            MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent += Reward;
            MaxSdkCallbacks.Interstitial.OnAdLoadedEvent += Loaded;
            MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent += LoadFailed;
            MaxSdkCallbacks.Interstitial.OnAdHiddenEvent += Hidden;
            MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent += DisplayFailed;
        }

        /// <summary>调用 .TrySetResult 完成已初始化。</summary>
        private void Initialized(MaxSdkBase.SdkConfiguration _) => initialization?.TrySetResult(true);
        /// <summary>调用 ResolveLoad 完成已加载。</summary>
        private void Loaded(string unit, MaxSdkBase.AdInfo _) => ResolveLoad(unit, null);
        /// <summary>加载失败。</summary>
        private void LoadFailed(string unit, MaxSdkBase.ErrorInfo error) => ResolveLoad(unit, new InvalidOperationException("MAX load failed: " + error.Code));

        /// <summary>解析 Load。</summary>
        private void ResolveLoad(string unit, Exception error)
        {
            if (disposed || unit != loadingPlacement?.UnitId)
            {
                return;
            }
            if (error == null)
            {
                loading?.TrySetResult(true);
            }
            else
            {
                loading?.TrySetException(error);
            }
        }

        /// <summary>调用 .TrySetResult 完成已隐藏。</summary>
        private void Hidden(string unit, MaxSdkBase.AdInfo info)
        {
            if (!disposed && unit == showingPlacement?.UnitId && info?.Placement == showId)
            {
                showing?.TrySetResult(true);
            }
        }

        /// <summary>调用 string.IsNullOrEmpty 完成显示失败。</summary>
        private void DisplayFailed(string unit, MaxSdkBase.ErrorInfo error, MaxSdkBase.AdInfo info)
        {
            if (!disposed && unit == showingPlacement?.UnitId && (string.IsNullOrEmpty(info?.Placement) || info.Placement == showId))
            {
                shows.Remove(showId);
                showing?.TrySetException(new InvalidOperationException("MAX display failed: " + error.Code));
            }
        }

        /// <summary>调用 string.IsNullOrEmpty 完成奖励。</summary>
        private void Reward(string unit, MaxSdkBase.Reward reward, MaxSdkBase.AdInfo info)
        {
            if (!disposed && info != null && !string.IsNullOrEmpty(info.Placement) && shows.TryGetValue(info.Placement, out var show) &&
                unit == show.Placement.UnitId && show.Placement.Format == AdFormat.Rewarded && !show.Rewarded && reward.Amount >= 0)
            {
                show.Rewarded = true;
                var value = new AdReward(Id, info.Placement, show.Placement.Id, reward.Label, reward.Amount);
                var handlers = RewardEarned;
                if (handlers == null)
                {
                    return;
                }
                foreach (Action<AdReward> handler in handlers.GetInvocationList())
                {
                    try
                    {
                        handler(value);
                    }
                    catch (Exception error)
                    {
                        Logger.Exception(error);
                    }
                }
            }
        }

        /// <summary>调用 nameof 完成就绪 ForCall。</summary>
        private void ReadyForCall()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use MAX on the initialized Unity main thread.");
            }
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MaxAdProvider));
            }
        }

        /// <summary>调用 ReadyForCall 完成就绪。</summary>
        private void Ready()
        {
            ReadyForCall();
            if (!ReferenceEquals(owner, this) || !MaxSdk.IsInitialized())
            {
                throw new InvalidOperationException("Await MAX initialization first.");
            }
        }

        /// <summary>取得信号。</summary>
        private static TaskCompletionSource<bool> Signal() => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>结束上限广告厂商适配器对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            ReadyForCall();
            if (loading != null || showing != null || (initialization != null && !initialization.Task.IsCompleted))
            {
                throw new InvalidOperationException("Wait for the native MAX operation before disposing its adapter.");
            }
            disposed = true;
            if (subscribed)
            {
                MaxSdkCallbacks.OnSdkInitializedEvent -= Initialized;
                MaxSdkCallbacks.Rewarded.OnAdLoadedEvent -= Loaded;
                MaxSdkCallbacks.Rewarded.OnAdLoadFailedEvent -= LoadFailed;
                MaxSdkCallbacks.Rewarded.OnAdHiddenEvent -= Hidden;
                MaxSdkCallbacks.Rewarded.OnAdDisplayFailedEvent -= DisplayFailed;
                MaxSdkCallbacks.Rewarded.OnAdReceivedRewardEvent -= Reward;
                MaxSdkCallbacks.Interstitial.OnAdLoadedEvent -= Loaded;
                MaxSdkCallbacks.Interstitial.OnAdLoadFailedEvent -= LoadFailed;
                MaxSdkCallbacks.Interstitial.OnAdHiddenEvent -= Hidden;
                MaxSdkCallbacks.Interstitial.OnAdDisplayFailedEvent -= DisplayFailed;
            }
            if (ReferenceEquals(owner, this))
            {
                owner = null;
            }
            RewardEarned = null;
            shows.Clear();
            history.Clear();
        }
    }
}

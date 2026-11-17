using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Ads
{
    /// <summary>定义广告 Format可选的枚举值。</summary>
    public enum AdFormat
    {
        Rewarded,
        Interstitial
    }

    /// <summary>提供广告广告位的实现。</summary>
    public sealed class AdPlacement
    {
        public string Id { get; }
        public string UnitId { get; }
        public AdFormat Format { get; }

        /// <summary>使用调用方提供的id、unitId、format创建 AdPlacement。</summary>
        public AdPlacement(string id, string unitId, AdFormat format)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(unitId) || !Enum.IsDefined(typeof(AdFormat), format))
            {
                throw new ArgumentException("Placement, SDK unit ID and format are required.");
            }
            Id = id;
            UnitId = unitId;
            Format = format;
        }
    }

    /// <summary>提供广告奖励的实现。</summary>
    public sealed class AdReward
    {
        public string Provider { get; }
        public string ShowId { get; }
        public string PlacementId { get; }
        public string Label { get; }
        public double Amount { get; }
        public string IdempotencyKey => Provider + ":" + ShowId;

        /// <summary>使用调用方提供的provider、showId、placementId创建 AdReward。</summary>
        public AdReward(string provider, string showId, string placementId, string label, double amount)
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(showId) || string.IsNullOrWhiteSpace(placementId))
            {
                throw new ArgumentException("Reward identity is required.");
            }
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }
            Provider = provider;
            ShowId = showId;
            PlacementId = placementId;
            Label = label;
            Amount = amount;
        }
    }

    /// <summary>定义广告厂商适配器的调用契约。</summary>
    public interface IAdProvider : IDisposable
    {
        string Id { get; }
        event Action<AdReward> RewardEarned;
        /// <summary>异步初始化广告厂商适配器。</summary>
        Task InitializeAsync(CancellationToken cancellationToken);
        /// <summary>异步加载广告厂商适配器。</summary>
        Task LoadAsync(AdPlacement placement, CancellationToken cancellationToken);
        /// <summary>检查当前资源或操作是否已就绪。</summary>
        bool IsReady(AdPlacement placement);
        /// <summary>Completes on native close/failure, not when a caller stops waiting. Close never grants a reward.</summary>
        /// <remarks>异步显示广告厂商适配器。</remarks>
        Task ShowAsync(AdPlacement placement, CancellationToken cancellationToken);
    }

    /// <summary>Use one explicitly configured provider per client. Reward delivery is independent of closing.</summary>
    /// <remarks>管理一个广告渠道的初始化、缓存和展示。广告位 ID 由调用方提供。</remarks>
    public sealed class AdManager : IDisposable
    {
        private readonly IAdProvider provider;
        private readonly bool ownsProvider;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<string> rewarded = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> recent = new Queue<string>();
        private bool busy, initialized, disposed, released;
        public bool IsBusy => busy;
        public event Action<AdReward> RewardEarned;

        /// <summary>配置单个广告适配器并订阅奖励事件。ownsProvider 为 true 时由管理器释放适配器。</summary>
        public AdManager(IAdProvider provider, bool ownsProvider = false)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.ownsProvider = ownsProvider;
            provider.RewardEarned += OnReward;
        }

        /// <summary>等待广告适配器初始化完成，随后才可加载和展示广告位。</summary>
        public Task InitializeAsync(CancellationToken token = default)
        {
            return Run(async stop =>
            {
                await provider.InitializeAsync(stop);
                stop.ThrowIfCancellationRequested();
                initialized = true;
            }, token);
        }

        /// <summary>预加载指定广告位。展示前用 IsReady 确认广告已可用。</summary>
        public Task LoadAsync(AdPlacement placement, CancellationToken token = default)
        {
            RequireInitialized(placement);
            return Run(stop => provider.LoadAsync(placement, stop), token);
        }

        /// <summary>查询指定广告位是否有可展示的广告，需要先完成初始化。</summary>
        public bool IsReady(AdPlacement placement)
        {
            RequireInitialized(placement);
            return provider.IsReady(placement);
        }

        /// <summary>展示已就绪的广告。激励奖励通过 RewardEarned 单独通知，不能用展示任务完成来判断。</summary>
        public Task ShowAsync(AdPlacement placement, CancellationToken token = default)
        {
            RequireInitialized(placement);
            if (!provider.IsReady(placement))
            {
                throw new InvalidOperationException("Load the ad and await readiness before showing it.");
            }
            return Run(stop => provider.ShowAsync(placement, stop), token);
        }

        // 验证 Unity 主线程、当前生命周期和广告位参数。
        private void RequireInitialized(AdPlacement placement)
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AdManager));
            }
            if (!initialized || placement == null)
            {
                throw new InvalidOperationException("Initialize ads and provide a placement first.");
            }
        }

        // 在一次操作退出之前占住操作位，并把调用方取消和管理器销毁合并传给适配器。
        private Task Run(Func<CancellationToken, Task> action, CancellationToken caller)
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AdManager));
            }
            if (busy)
            {
                throw new InvalidOperationException("An advertising operation is already active.");
            }
            caller.ThrowIfCancellationRequested();
            busy = true;
            // 取消调用方的等待后，底层操作仍要退出，才能接受下一次广告请求。
            return TaskUtil.WaitAsync(Execute(action, CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, caller)), caller);
        }

        // 在底层操作真正结束后归还操作位，若此时已销毁则继续释放适配器。
        private async Task Execute(Func<CancellationToken, Task> action, CancellationTokenSource stop)
        {
            try
            {
                await action(stop.Token);
            }
            finally
            {
                stop.Dispose();
                busy = false;
                if (disposed)
                {
                    Release();
                }
            }
        }

        // 校验奖励来源和幂等键，逐个通知业务监听器。
        private void OnReward(AdReward reward)
        {
            if (disposed || reward == null || reward.Provider != provider.Id || !rewarded.Add(reward.IdempotencyKey))
            {
                return;
            }
            recent.Enqueue(reward.IdempotencyKey);
            // 只保留最近 1024 条奖励键，限制内存占用。长期幂等应由发奖方处理。
            if (recent.Count > 1024)
            {
                rewarded.Remove(recent.Dequeue());
            }
            var handlers = RewardEarned;
            if (handlers == null)
            {
                return;
            }
            foreach (Action<AdReward> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(reward);
                }
                catch (Exception error)
                {
                    Logger.Exception(error);
                }
            }
        }

        // 广告适配器需要在已初始化的 Unity 主线程上调用。
        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use platform services on the initialized Unity main thread.");
            }
        }

        /// <summary>取消运行中的广告操作并解除奖励订阅。底层操作结束后才释放所持有的适配器。</summary>
        public void Dispose()
        {
            MainThread();
            if (disposed)
            {
                return;
            }
            disposed = true;
            provider.RewardEarned -= OnReward;
            RewardEarned = null;
            try
            {
                lifetime.Cancel();
            }
            finally
            {
                if (!busy)
                {
                    Release();
                }
            }
        }

        // 只执行一次最终释放。借用的适配器由创建方负责销毁。
        private void Release()
        {
            if (released)
            {
                return;
            }
            released = true;
            lifetime.Dispose();
            if (ownsProvider)
            {
                provider.Dispose();
            }
        }
    }
}

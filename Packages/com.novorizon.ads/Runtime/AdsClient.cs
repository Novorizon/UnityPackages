using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Ads
{
    public enum AdFormat
    {
        Rewarded,
        Interstitial
    }

    public sealed class AdPlacement
    {
        public string Id { get; }
        public string UnitId { get; }
        public AdFormat Format { get; }

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

    public sealed class AdReward
    {
        public string Provider { get; }
        public string ShowId { get; }
        public string PlacementId { get; }
        public string Label { get; }
        public double Amount { get; }
        public string IdempotencyKey => Provider + ":" + ShowId;

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

    public interface IAdProvider : IDisposable
    {
        string Id { get; }
        event Action<AdReward> RewardEarned;
        Task InitializeAsync(CancellationToken cancellationToken);
        Task LoadAsync(AdPlacement placement, CancellationToken cancellationToken);
        bool IsReady(AdPlacement placement);
        /// <summary>Completes on native close/failure, not when a caller stops waiting. Close never grants a reward.</summary>
        Task ShowAsync(AdPlacement placement, CancellationToken cancellationToken);
    }

    /// <summary>Use one explicitly configured provider per client. Reward delivery is independent of closing.</summary>
    public class AdsClient : IDisposable
    {
        private readonly IAdProvider provider;
        private readonly bool ownsProvider;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly HashSet<string> rewarded = new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string> recent = new Queue<string>();
        private bool busy, initialized, disposed, released;
        public bool IsBusy => busy;
        public event Action<AdReward> RewardEarned;

        public AdsClient(IAdProvider provider, bool ownsProvider = false)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.ownsProvider = ownsProvider;
            provider.RewardEarned += OnReward;
        }

        public Task InitializeAsync(CancellationToken token = default)
        {
            return Run(async stop =>
            {
                await provider.InitializeAsync(stop);
                stop.ThrowIfCancellationRequested();
                initialized = true;
            }, token);
        }

        public Task LoadAsync(AdPlacement placement, CancellationToken token = default)
        {
            RequireInitialized(placement);
            return Run(stop => provider.LoadAsync(placement, stop), token);
        }

        public bool IsReady(AdPlacement placement)
        {
            RequireInitialized(placement);
            return provider.IsReady(placement);
        }

        public Task ShowAsync(AdPlacement placement, CancellationToken token = default)
        {
            RequireInitialized(placement);
            if (!provider.IsReady(placement))
            {
                throw new InvalidOperationException("Load the ad and await readiness before showing it.");
            }
            return Run(stop => provider.ShowAsync(placement, stop), token);
        }

        private void RequireInitialized(AdPlacement placement)
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AdsClient));
            }
            if (!initialized || placement == null)
            {
                throw new InvalidOperationException("Initialize ads and provide a placement first.");
            }
        }

        private Task Run(Func<CancellationToken, Task> action, CancellationToken caller)
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AdsClient));
            }
            if (busy)
            {
                throw new InvalidOperationException("An advertising operation is already active.");
            }
            caller.ThrowIfCancellationRequested();
            busy = true;
            return TaskUtil.WaitAsync(Execute(action, CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, caller)), caller);
        }

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

        private void OnReward(AdReward reward)
        {
            if (disposed || reward == null || reward.Provider != provider.Id || !rewarded.Add(reward.IdempotencyKey))
            {
                return;
            }
            recent.Enqueue(reward.IdempotencyKey);
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

        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use platform services on the initialized Unity main thread.");
            }
        }

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

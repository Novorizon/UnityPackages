using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Ads;
using Game.Framework.Auth;
using Game.Framework.Payments;

internal static class Program
{
    private static Pump pump;
    private static int passed;

    private static void Main()
    {
        Run("identity proof redacts and retires credentials", () =>
        {
            var proof = Proof();
            Check(!proof.ToString().Contains("secret"));
            proof.Dispose();
            Throws<ObjectDisposedException>(() => _ = proof.Token);
            string nonce = IdentityNonce.Create();
            Check(nonce != IdentityNonce.Create() && nonce.Length >= 32);
            Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", IdentityNonce.Sha256("abc"));
        });
        Run("link preserves the guest UID and disposes its proof", () =>
        {
            var backend = new Backend();
            var source = new Identity();
            using var client = new AuthManager(backend, new[] { source });
            Equal("guest", Done(client.LinkAsync(IdentityProvider.Google)).Id);
            Equal("guest", backend.LinkedUid);
            Throws<ObjectDisposedException>(() => _ = source.Last.Token);
        });
        Run("account change during identity popup cannot link the new account", () =>
        {
            var backend = new Backend();
            var source = new Identity { Gate = Signal<IdentityProof>() };
            using var client = new AuthManager(backend, new[] { source });
            var pending = client.LinkAsync(IdentityProvider.Google);
            backend.User = new AuthUser("other", false, new[] { "google.com" });
            var proof = Proof();
            source.Gate.SetResult(proof);
            Fails<InvalidOperationException>(pending);
            Equal(0, backend.Links);
            Throws<ObjectDisposedException>(() => _ = proof.Token);
        });
        Run("identity from the wrong provider is never sent to backend", () =>
        {
            var source = new Identity { ResultProvider = IdentityProvider.Apple };
            var backend = new Backend();
            using var client = new AuthManager(backend, new[] { source });
            Fails<InvalidOperationException>(client.SignInAsync(IdentityProvider.Google));
            Equal(0, backend.SignIns);
        });
        Run("last sign-in provider cannot be unlinked", () =>
        {
            var backend = new Backend { User = new AuthUser("account", false, new[] { "apple.com" }) };
            using var client = new AuthManager(backend, Array.Empty<IIdentityProvider>());
            Throws<InvalidOperationException>(() => client.UnlinkAsync("apple.com"));
            Equal(0, backend.Unlinks);
        });
        Run("cancelled sign-in waiter retains native popup ownership", () =>
        {
            var backend = new Backend();
            var source = new Identity { Gate = Signal<IdentityProof>() };
            using var client = new AuthManager(backend, new[] { source });
            using var stop = new CancellationTokenSource();
            var pending = client.SignInAsync(IdentityProvider.Google, stop.Token);
            stop.Cancel();
            Fails<OperationCanceledException>(pending);
            Check(client.IsBusy);
            Throws<InvalidOperationException>(() => client.SignInAsync(IdentityProvider.Google));
            source.Gate.SetResult(Proof());
            pump.Until(() => !client.IsBusy);
            Equal(0, backend.SignIns);
        });
        Run("auth disposal defers owned SDK cleanup until popup completion", () =>
        {
            var backend = new Backend();
            var source = new Identity { Gate = Signal<IdentityProof>() };
            var client = new AuthManager(backend, new[] { source }, true);
            var pending = client.SignInAsync(IdentityProvider.Google);
            client.Dispose();
            Equal(0, source.Disposals);
            source.Gate.SetResult(Proof());
            Fails<OperationCanceledException>(pending);
            Equal(1, source.Disposals);
            Equal(1, backend.Disposals);
            client.Dispose();
            Equal(1, backend.Disposals);
        });
        Run("pre-cancelled auth never opens a popup", () =>
        {
            var source = new Identity();
            using var client = new AuthManager(new Backend(), new[] { source });
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            Throws<OperationCanceledException>(() => client.SignInAsync(IdentityProvider.Google, stop.Token));
            Equal(0, source.Calls);
        });
        Run("backend credential collision propagates without merging accounts", () =>
        {
            var backend = new Backend { RejectLink = true };
            using var client = new AuthManager(backend, new[] { new Identity() });
            Fails<InvalidOperationException>(client.LinkAsync(IdentityProvider.Google));
            Equal("guest", client.CurrentUser.Id);
        });
        Run("auth rejects worker-thread operations before calling SDK", () =>
        {
            var backend = new Backend();
            using var client = new AuthManager(backend, new[] { new Identity() });
            Fails<InvalidOperationException>(Task.Run(() => client.SignOut()));
            Equal(0, backend.SignOuts);
        });

        Run("purchase is verified and granted before store confirmation", () =>
        {
            var trace = new List<string>();
            var store = new Store { Trace = trace };
            var fulfillment = new Fulfillment { Trace = trace, Gate = Signal<bool>() };
            using var client = Payment(store, fulfillment);
            var pending = client.PurchaseAsync("coins");
            Equal("verify", string.Join(",", trace));
            Equal(0, store.Confirms);
            fulfillment.Gate.SetResult(true);
            Check(Done(pending).Confirmed);
            Equal("verify,confirm", string.Join(",", trace));
        });
        Run("failed server verification leaves store transaction unconfirmed", () =>
        {
            var store = new Store();
            using var client = Payment(store, new Fulfillment { Fail = true });
            Fails<InvalidOperationException>(client.PurchaseAsync("coins"));
            Equal(0, store.Confirms);
        });
        Run("confirmation failure is observable after idempotent fulfillment", () =>
        {
            var store = new Store { FailConfirmation = true };
            var fulfillment = new Fulfillment();
            using var client = Payment(store, fulfillment);
            Fails<InvalidOperationException>(client.PurchaseAsync("coins"));
            Equal(1, fulfillment.Calls);
        });
        Run("restoring deduplicates a pending transaction within one batch", () =>
        {
            var store = new Store();
            var fulfillment = new Fulfillment();
            store.Receipts.Add(Receipt());
            store.Receipts.Add(Receipt());
            using var client = Payment(store, fulfillment);
            Equal(1, Done(client.RestoreAsync()).Count);
            Equal(1, store.Confirms);
            Equal(1, fulfillment.Calls);
        });
        Run("confirmed entitlements reconcile without acknowledging twice", () =>
        {
            var store = new Store();
            var fulfillment = new Fulfillment();
            store.Receipts.Add(new PurchaseReceipt("GooglePlay", "premium", "old", "signed", requiresConfirmation: false));
            using var client = Payment(store, fulfillment);
            Check(Done(client.RestoreAsync()).Single().Confirmed);
            Equal(1, fulfillment.Calls);
            Equal(0, store.Confirms);
        });
        Run("startup recovery fetches orders without invoking explicit restore", () =>
        {
            var store = new Store();
            using var client = Payment(store, new Fulfillment());
            Done(client.RecoverAsync());
            Equal(1, store.Fetches);
            Equal(0, store.Restores);
            Done(client.RestoreAsync());
            Equal(1, store.Restores);
        });
        Run("deferred and cancelled orders never grant or confirm", () =>
        {
            var store = new Store { PurchaseStatus = StorePurchaseStatus.Deferred };
            var fulfillment = new Fulfillment();
            using var client = Payment(store, fulfillment);
            Check(!Done(client.PurchaseAsync("coins")).Confirmed);
            store.PurchaseStatus = StorePurchaseStatus.Cancelled;
            Check(!Done(client.PurchaseAsync("coins")).Confirmed);
            Equal(0, fulfillment.Calls);
            Equal(0, store.Confirms);
        });
        Run("wrong product receipt is rejected before fulfillment", () =>
        {
            var store = new Store { ProductId = "other" };
            var fulfillment = new Fulfillment();
            using var client = Payment(store, fulfillment);
            Fails<InvalidOperationException>(client.PurchaseAsync("coins"));
            Equal(0, fulfillment.Calls);
        });
        Run("cancelled checkout retains ownership and does not confirm late result", () =>
        {
            var store = new Store { Gate = Signal<StorePurchase>() };
            using var client = Payment(store, new Fulfillment());
            using var stop = new CancellationTokenSource();
            var pending = client.PurchaseAsync("coins", stop.Token);
            stop.Cancel();
            Fails<OperationCanceledException>(pending);
            Check(client.IsBusy);
            Throws<InvalidOperationException>(() => client.PurchaseAsync("coins"));
            store.Gate.SetResult(new StorePurchase(StorePurchaseStatus.PendingVerification, Receipt()));
            pump.Until(() => !client.IsBusy);
            Equal(0, store.Confirms);
        });
        Run("cancellation after server grant leaves order for idempotent recovery", () =>
        {
            var store = new Store();
            var fulfillment = new Fulfillment { Gate = Signal<bool>() };
            using var client = Payment(store, fulfillment);
            using var stop = new CancellationTokenSource();
            var pending = client.PurchaseAsync("coins", stop.Token);
            stop.Cancel();
            Fails<OperationCanceledException>(pending);
            fulfillment.Gate.SetResult(true);
            pump.Until(() => !client.IsBusy);
            Equal(0, store.Confirms);
        });
        Run("payment disposal waits for native checkout before releasing owned store", () =>
        {
            var store = new Store { Gate = Signal<StorePurchase>() };
            var client = Payment(store, new Fulfillment(), true);
            var pending = client.PurchaseAsync("coins");
            client.Dispose();
            Equal(0, store.Disposals);
            store.Gate.SetResult(new StorePurchase(StorePurchaseStatus.Cancelled));
            Fails<OperationCanceledException>(pending);
            Equal(1, store.Disposals);
        });
        Run("receipts require verifiable payloads and redact logs", () =>
        {
            Throws<ArgumentException>(() => new PurchaseReceipt("GooglePlay", "coins", "1", ""));
            Check(!Receipt().ToString().Contains("signed"));
        });

        Run("ad close grants no reward", () =>
        {
            var provider = new Ads();
            using var client = Advertising(provider);
            int rewards = 0;
            client.RewardEarned += _ => rewards++;
            Done(client.ShowAsync(Placement()));
            Equal(0, rewards);
        });
        Run("duplicate reward callbacks grant once", () =>
        {
            var provider = new Ads();
            using var client = Advertising(provider);
            int rewards = 0;
            client.RewardEarned += _ => rewards++;
            provider.Reward(new AdReward(provider.Id, "show", "coins", "coin", 1));
            provider.Reward(new AdReward(provider.Id, "show", "coins", "coin", 1));
            Equal(1, rewards);
        });
        Run("late reward after close preserves its show identity", () =>
        {
            var provider = new Ads();
            using var client = Advertising(provider);
            string show = null;
            client.RewardEarned += value => show = value.ShowId;
            Done(client.ShowAsync(Placement()));
            provider.Reward(new AdReward(provider.Id, "old-show", "coins", "coin", 1));
            Equal("old-show", show);
        });
        Run("foreign provider reward is ignored", () =>
        {
            var provider = new Ads();
            using var client = Advertising(provider);
            int rewards = 0;
            client.RewardEarned += _ => rewards++;
            provider.Reward(new AdReward("other", "show", "coins", "coin", 1));
            Equal(0, rewards);
        });
        Run("cancelled ad waiter cannot start another fullscreen ad", () =>
        {
            var provider = new Ads { Gate = Signal<bool>() };
            using var client = Advertising(provider);
            using var stop = new CancellationTokenSource();
            var pending = client.ShowAsync(Placement(), stop.Token);
            stop.Cancel();
            Fails<OperationCanceledException>(pending);
            Check(client.IsBusy);
            Throws<InvalidOperationException>(() => client.ShowAsync(Placement()));
            provider.Gate.SetResult(true);
            pump.Until(() => !client.IsBusy);
        });
        Run("ad disposal suppresses rewards and defers native owner release", () =>
        {
            var provider = new Ads { Gate = Signal<bool>() };
            var client = Advertising(provider, true);
            int rewards = 0;
            client.RewardEarned += _ => rewards++;
            var pending = client.ShowAsync(Placement());
            client.Dispose();
            Equal(0, provider.Disposals);
            provider.Reward(new AdReward(provider.Id, "show", "coins", "coin", 1));
            Equal(0, rewards);
            provider.Gate.SetResult(true);
            Done(pending);
            Equal(1, provider.Disposals);
        });
        Run("invalid ad rewards cannot enter business callbacks", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new AdReward("sdk", "show", "place", "coin", double.NaN));
            Throws<ArgumentOutOfRangeException>(() => new AdReward("sdk", "show", "place", "coin", -1));
        });
        Console.WriteLine($"PASS platform services: {passed} behavioral cases (test providers; no live purchases, ads or login).");
    }

    private static IdentityProof Proof(IdentityProvider provider = IdentityProvider.Google) => new IdentityProof(provider, IdentityTokenKind.IdToken, "secret-token", "nonce");
    private static PurchaseReceipt Receipt() => new PurchaseReceipt("GooglePlay", "coins", "transaction", "signed-receipt");
    private static AdPlacement Placement() => new AdPlacement("coins", "unit", AdFormat.Rewarded);
    private static IapManager Payment(Store store, Fulfillment fulfillment, bool owns = false)
    {
        var client = new IapManager(store, fulfillment, owns);
        Done(client.InitializeAsync());
        return client;
    }
    private static AdsClient Advertising(Ads provider, bool owns = false)
    {
        var client = new AdManager(provider, owns);
        Done(client.InitializeAsync());
        return client;
    }
    private static TaskCompletionSource<T> Signal<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    private static void Run(string name, Action test)
    {
        UnityThread.Reset();
        pump = new Pump();
        SynchronizationContext.SetSynchronizationContext(pump);
        UnityThread.Initialize();
        try
        {
            test();
            passed++;
        }
        catch (Exception error)
        {
            throw new Exception(name, error);
        }
    }
    private static void Check(bool value)
    {
        if (!value)
        {
            throw new Exception("Assertion failed.");
        }
    }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual));
    private static void Done(Task task)
    {
        pump.Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }
    private static T Done<T>(Task<T> task)
    {
        Done((Task)task);
        return task.GetAwaiter().GetResult();
    }
    private static void Fails<T>(Task task) where T : Exception => Throws<T>(() => Done(task));
    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private sealed class Pump : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback, object)> queue = new ConcurrentQueue<(SendOrPostCallback, object)>();
        public override void Post(SendOrPostCallback callback, object state) => queue.Enqueue((callback, state));
        internal void Until(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!done())
            {
                if (queue.TryDequeue(out var work))
                {
                    work.Item1(work.Item2);
                }
                else
                {
                    Thread.Yield();
                }
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Test callback did not finish.");
                }
            }
        }
    }
    private sealed class Identity : IIdentityProvider
    {
        public IdentityProvider Provider => IdentityProvider.Google;
        internal IdentityProvider ResultProvider = IdentityProvider.Google;
        internal TaskCompletionSource<IdentityProof> Gate;
        internal IdentityProof Last;
        internal int Calls, Disposals;
        public Task<IdentityProof> AcquireAsync(CancellationToken token)
        {
            Calls++;
            return Gate?.Task ?? Task.FromResult(Last = Proof(ResultProvider));
        }
        public void Dispose() => Disposals++;
    }
    private sealed class Backend : IAuthBackend
    {
        internal AuthUser User = new AuthUser("guest", true, Array.Empty<string>());
        internal string LinkedUid;
        internal int Links, Unlinks, SignIns, SignOuts, Disposals;
        internal bool RejectLink;
        public AuthUser CurrentUser => User;
        public Task<AuthUser> SignInAnonymouslyAsync(CancellationToken token) => Task.FromResult(User);
        public Task<AuthUser> SignInAsync(IdentityProof proof, CancellationToken token)
        {
            SignIns++;
            return Task.FromResult(User);
        }
        public Task<AuthUser> LinkAsync(string uid, IdentityProof proof, CancellationToken token)
        {
            if (RejectLink)
            {
                throw new InvalidOperationException("credential-already-in-use");
            }
            Links++;
            LinkedUid = uid;
            User = new AuthUser(uid, false, new[] { "google.com" });
            return Task.FromResult(User);
        }
        public Task<AuthUser> UnlinkAsync(string uid, string providerId, CancellationToken token)
        {
            Unlinks++;
            return Task.FromResult(User);
        }
        public Task<string> GetSessionTokenAsync(bool refresh, CancellationToken token) => Task.FromResult("session");
        public void SignOut() => SignOuts++;
        public void Dispose() => Disposals++;
    }
    private sealed class Store : IPaymentStore
    {
        internal TaskCompletionSource<StorePurchase> Gate;
        internal List<string> Trace;
        internal List<PurchaseReceipt> Receipts = new List<PurchaseReceipt>();
        internal string ProductId = "coins";
        internal bool FailConfirmation;
        internal StorePurchaseStatus PurchaseStatus = StorePurchaseStatus.PendingVerification;
        internal int Confirms, Disposals, Fetches, Restores;
        public IReadOnlyList<StoreProduct> Products => Array.Empty<StoreProduct>();
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task<StorePurchase> PurchaseAsync(string productId, CancellationToken token) => Gate?.Task ?? Task.FromResult(new StorePurchase(PurchaseStatus, new PurchaseReceipt("GooglePlay", ProductId, "transaction", "signed")));
        public Task<IReadOnlyList<PurchaseReceipt>> FetchPurchasesAsync(CancellationToken token)
        {
            Fetches++;
            return Task.FromResult<IReadOnlyList<PurchaseReceipt>>(Receipts);
        }
        public Task<IReadOnlyList<PurchaseReceipt>> RestoreAsync(CancellationToken token)
        {
            Restores++;
            return Task.FromResult<IReadOnlyList<PurchaseReceipt>>(Receipts);
        }
        public Task ConfirmAsync(PurchaseReceipt receipt, CancellationToken token)
        {
            Trace?.Add("confirm");
            Confirms++;
            return FailConfirmation ? Task.FromException(new InvalidOperationException("confirm failed")) : Task.CompletedTask;
        }
        public void Dispose() => Disposals++;
    }
    private sealed class Fulfillment : IPurchaseFulfillment
    {
        internal TaskCompletionSource<bool> Gate;
        internal List<string> Trace;
        internal bool Fail;
        internal int Calls;
        public Task VerifyAndGrantAsync(PurchaseReceipt receipt, CancellationToken token)
        {
            Calls++;
            Trace?.Add("verify");
            return Fail ? Task.FromException(new InvalidOperationException("verification failed")) : Gate?.Task ?? Task.CompletedTask;
        }
    }
    private sealed class Ads : IAdProvider
    {
        internal TaskCompletionSource<bool> Gate;
        internal int Disposals;
        public string Id => "test-sdk";
        public event Action<AdReward> RewardEarned;
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task LoadAsync(AdPlacement placement, CancellationToken token) => Task.CompletedTask;
        public bool IsReady(AdPlacement placement) => true;
        public Task ShowAsync(AdPlacement placement, CancellationToken token) => Gate?.Task ?? Task.CompletedTask;
        internal void Reward(AdReward reward) => RewardEarned?.Invoke(reward);
        public void Dispose() => Disposals++;
    }
}

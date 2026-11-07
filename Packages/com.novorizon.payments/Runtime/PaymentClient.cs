using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Payments
{
    /// <summary>Explicit main-thread owner; cancellation never implies a financial transaction was rolled back.</summary>
    public class PaymentClient : IDisposable
    {
        private readonly IPaymentStore store;
        private readonly IPurchaseFulfillment fulfillment;
        private readonly bool ownsStore;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool busy, ready, disposed, released;
        public bool IsBusy => busy;
        public IReadOnlyList<StoreProduct> Products => store.Products;

        public PaymentClient(IPaymentStore store, IPurchaseFulfillment fulfillment, bool ownsStore = false)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.fulfillment = fulfillment ?? throw new ArgumentNullException(nameof(fulfillment));
            this.ownsStore = ownsStore;
        }

        public Task InitializeAsync(CancellationToken token = default)
        {
            return Run(async stop =>
            {
                await store.InitializeAsync(stop);
                stop.ThrowIfCancellationRequested();
                ready = true;
                return true;
            }, token);
        }

        public Task<PaymentResult> PurchaseAsync(string productId, CancellationToken token = default)
        {
            if (!ready)
            {
                throw new InvalidOperationException("Await payment initialization before purchasing.");
            }
            if (string.IsNullOrWhiteSpace(productId))
            {
                throw new ArgumentException("Product ID is required.", nameof(productId));
            }
            return Run(async stop =>
            {
                StorePurchase purchase = await store.PurchaseAsync(productId, stop);
                stop.ThrowIfCancellationRequested();
                if (purchase == null)
                {
                    throw new InvalidOperationException("The store returned no purchase result.");
                }
                if (purchase.Status != StorePurchaseStatus.PendingVerification)
                {
                    return new PaymentResult(purchase.Status, false, null);
                }
                if (purchase.Receipt.ProductId != productId)
                {
                    throw new InvalidOperationException("The store returned a different product.");
                }
                return await Fulfill(purchase.Receipt, stop);
            }, token);
        }

        /// <summary>Redelivered pending orders are verified again; the server must deduplicate by store/transaction.</summary>
        public Task<IReadOnlyList<PaymentResult>> RestoreAsync(CancellationToken token = default)
        {
            return Reconcile(true, token);
        }

        /// <summary>Startup recovery without invoking Apple's explicit RestoreTransactions flow.</summary>
        public Task<IReadOnlyList<PaymentResult>> RecoverAsync(CancellationToken token = default)
        {
            return Reconcile(false, token);
        }

        private Task<IReadOnlyList<PaymentResult>> Reconcile(bool restore, CancellationToken token)
        {
            if (!ready)
            {
                throw new InvalidOperationException("Await payment initialization before restoring.");
            }
            return Run<IReadOnlyList<PaymentResult>>(async stop =>
            {
                var receipts = await (restore ? store.RestoreAsync(stop) : store.FetchPurchasesAsync(stop));
                if (receipts == null)
                {
                    throw new InvalidOperationException("The store returned no purchase history.");
                }
                var results = new List<PaymentResult>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var receipt in receipts)
                {
                    stop.ThrowIfCancellationRequested();
                    if (receipt == null)
                    {
                        throw new InvalidOperationException("The store returned an empty receipt.");
                    }
                    if (seen.Add(receipt.IdempotencyKey))
                    {
                        results.Add(await Fulfill(receipt, stop));
                    }
                }
                return results.AsReadOnly();
            }, token);
        }

        private async Task<PaymentResult> Fulfill(PurchaseReceipt receipt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            await fulfillment.VerifyAndGrantAsync(receipt, token);
            token.ThrowIfCancellationRequested();
            // Failed/cancelled verification never consumes or acknowledges a store order.
            if (receipt.RequiresConfirmation)
            {
                await store.ConfirmAsync(receipt, token);
            }
            return new PaymentResult(StorePurchaseStatus.PendingVerification, true, receipt.TransactionId);
        }

        private Task<T> Run<T>(Func<CancellationToken, Task<T>> action, CancellationToken caller)
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(PaymentClient));
            }
            if (busy)
            {
                throw new InvalidOperationException("A payment operation is already active.");
            }
            caller.ThrowIfCancellationRequested();
            busy = true;
            var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, caller);
            return TaskUtil.WaitAsync(Execute(action, stop), caller);
        }

        private async Task<T> Execute<T>(Func<CancellationToken, Task<T>> action, CancellationTokenSource stop)
        {
            try
            {
                return await action(stop.Token);
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
            if (ownsStore)
            {
                store.Dispose();
            }
        }
    }
}

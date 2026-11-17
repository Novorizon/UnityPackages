using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Payments
{
    /// <summary>Explicit main-thread owner; cancellation never implies a financial transaction was rolled back.</summary>
    /// <remarks>串行处理平台内购、验单发货和订单确认，由商店适配器执行原生操作。</remarks>
    public sealed class IapManager : IDisposable
    {
        private readonly IPaymentStore store;
        private readonly IPurchaseFulfillment fulfillment;
        private readonly bool ownsStore;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool busy, ready, disposed, released;
        public bool IsBusy => busy;
        public IReadOnlyList<StoreProduct> Products => store.Products;

        /// <summary>注入商店接口和验单发货接口。ownsStore 为 true 时由管理器释放商店。</summary>
        public IapManager(IPaymentStore store, IPurchaseFulfillment fulfillment, bool ownsStore = false)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.fulfillment = fulfillment ?? throw new ArgumentNullException(nameof(fulfillment));
            this.ownsStore = ownsStore;
        }

        /// <summary>初始化商店连接和商品信息，完成后才接受购买及恢复请求。</summary>
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

        /// <summary>购买指定商品。商店返回待验证订单后，才会验单、发货并按需确认。</summary>
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
        /// <remarks>向商店发起显式恢复，返回各笔可处理订单的验单结果。</remarks>
        public Task<IReadOnlyList<PaymentResult>> RestoreAsync(CancellationToken token = default)
        {
            return Reconcile(true, token);
        }

        /// <summary>Startup recovery without invoking Apple's explicit RestoreTransactions flow.</summary>
        /// <remarks>主动拉取未处理购买，供启动恢复使用。</remarks>
        public Task<IReadOnlyList<PaymentResult>> RecoverAsync(CancellationToken token = default)
        {
            return Reconcile(false, token);
        }

        // 拉取订单并在本批内去重，按顺序验单和发货。
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

        // 完成发货后才确认商店订单。发货或等待取消时保留待处理订单。
        private async Task<PaymentResult> Fulfill(PurchaseReceipt receipt, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // 先由发货端完成验单和发货，再确认商店订单；失败时订单可留待后续恢复。
            await fulfillment.VerifyAndGrantAsync(receipt, token);
            token.ThrowIfCancellationRequested();
            // Failed/cancelled verification never consumes or acknowledges a store order.
            if (receipt.RequiresConfirmation)
            {
                await store.ConfirmAsync(receipt, token);
            }
            return new PaymentResult(StorePurchaseStatus.PendingVerification, true, receipt.TransactionId);
        }

        // 只允许同时执行一个商店操作，调用方等待可以取消。
        private Task<T> Run<T>(Func<CancellationToken, Task<T>> action, CancellationToken caller)
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(IapManager));
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

        // 商店操作实际结束后清掉忙碌状态。
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

        // 校验原生商店操作在 Unity 主线程调用。
        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use platform services on the initialized Unity main thread.");
            }
        }

        /// <summary>停止接受新操作并取消已有操作。进行中的操作退出后再释放自有商店。</summary>
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

        // 释放管理器拥有的取消资源和商店接口。
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

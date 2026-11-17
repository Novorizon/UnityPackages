using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;

namespace Game.Framework.Payments.UnityIap
{
    /// <summary>定义原生商店可选的枚举值。</summary>
    public enum NativeStore
    {
        Apple,
        GooglePlay
    }

    /// <summary>Unity IAP 5.5 native stores. One owner of the shared default controller at a time.</summary>
    /// <remarks>提供Unity 内购商店的实现。</remarks>
    public sealed class UnityIapStore : IPaymentStore
    {
        private static UnityIapStore owner;
        private readonly NativeStore nativeStore;
        private readonly StoreProduct[] catalog;
        private readonly Dictionary<string, PendingOrder> pending = new Dictionary<string, PendingOrder>(StringComparer.Ordinal);
        private readonly Dictionary<string, TaskCompletionSource<bool>> confirming = new Dictionary<string, TaskCompletionSource<bool>>(StringComparer.Ordinal);
        private StoreController controller;
        private TaskCompletionSource<bool> fetchingProducts;
        private TaskCompletionSource<bool> restoring;
        private TaskCompletionSource<IReadOnlyList<PurchaseReceipt>> fetchingPurchases;
        private TaskCompletionSource<StorePurchase> buying;
        private string buyingProduct;
        private IReadOnlyList<StoreProduct> products = Array.Empty<StoreProduct>();
        private bool initialized, disposed, connecting;
        public IReadOnlyList<StoreProduct> Products => products;

        /// <summary>使用调用方提供的store、catalog创建 UnityIapStore。</summary>
        public UnityIapStore(NativeStore store, IEnumerable<StoreProduct> catalog)
        {
            if (!Enum.IsDefined(typeof(NativeStore), store))
            {
                throw new ArgumentOutOfRangeException(nameof(store));
            }
            nativeStore = store;
            this.catalog = (catalog ?? throw new ArgumentNullException(nameof(catalog))).ToArray();
            if (this.catalog.Length == 0 || this.catalog.Any(p => p == null) ||
                this.catalog.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != this.catalog.Length ||
                this.catalog.Select(p => p.StoreId).Distinct(StringComparer.Ordinal).Count() != this.catalog.Length)
            {
                throw new ArgumentException("Provide unique logical and store IDs.", nameof(catalog));
            }
        }

        /// <summary>异步初始化 Unity 内购商店。</summary>
        public async Task InitializeAsync(CancellationToken token)
        {
            MainThread();
            ThrowIfDisposed();
            token.ThrowIfCancellationRequested();
            if (initialized)
            {
                return;
            }
            if (owner != null && !ReferenceEquals(owner, this))
            {
                throw new InvalidOperationException("Another adapter owns the shared Unity IAP controller.");
            }
            if (!Application.isEditor &&
                ((nativeStore == NativeStore.Apple && Application.platform != RuntimePlatform.IPhonePlayer) ||
                 (nativeStore == NativeStore.GooglePlay && Application.platform != RuntimePlatform.Android)))
            {
                throw new PlatformNotSupportedException("The configured store does not match the player platform.");
            }
            if (controller != null)
            {
                throw new InvalidOperationException("Initialize once, or dispose this failed adapter before retrying.");
            }
            owner = this;
            controller = UnityIAPServices.StoreController(Application.isEditor ? null : StoreName);
            controller.OnProductsFetched += ProductsFetched;
            controller.OnProductsFetchFailed += ProductsFailed;
            controller.OnPurchasePending += PurchasePending;
            controller.OnPurchaseFailed += PurchaseFailed;
            controller.OnPurchaseDeferred += PurchaseDeferred;
            controller.OnPurchaseConfirmed += PurchaseConfirmed;
            controller.OnPurchasesFetched += PurchasesFetched;
            controller.OnPurchasesFetchFailed += PurchasesFailed;
            controller.OnStoreDisconnected += Disconnected;
            controller.OnAuthAccountChanged += AccountChanged;
            connecting = true;
            try
            {
                await controller.Connect();
                ThrowIfDisposed();
                token.ThrowIfCancellationRequested();
                fetchingProducts = Signal<bool>();
                controller.FetchProductsWithNoRetries(catalog.Select(p => new ProductDefinition(p.Id, p.StoreId, (ProductType)p.Kind)).ToList());
                await fetchingProducts.Task;
                ThrowIfDisposed();
                token.ThrowIfCancellationRequested();
                initialized = true;
            }
            finally
            {
                fetchingProducts = null;
                connecting = false;
            }
        }

        /// <summary>异步发起购买 Unity 内购商店。</summary>
        public async Task<StorePurchase> PurchaseAsync(string productId, CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (buying != null || restoring != null || fetchingPurchases != null || confirming.Count != 0)
            {
                throw new InvalidOperationException("A store purchase is still active.");
            }
            Product product = controller.GetProductById(productId);
            if (product == null || !product.availableToPurchase)
            {
                throw new InvalidOperationException("The product was not returned as purchasable by the store.");
            }
            buying = Signal<StorePurchase>();
            buyingProduct = productId;
            try
            {
                controller.PurchaseProduct(product);
                // IAP does not offer financial rollback on token cancellation. Keep ownership until its event.
                return await buying.Task;
            }
            finally
            {
                buying = null;
                buyingProduct = null;
            }
        }

        /// <summary>异步恢复 Unity 内购商店。</summary>
        public Task<IReadOnlyList<PurchaseReceipt>> RestoreAsync(CancellationToken token) => FetchHistory(true, token);

        /// <summary>异步拉取购买记录。</summary>
        public Task<IReadOnlyList<PurchaseReceipt>> FetchPurchasesAsync(CancellationToken token) => FetchHistory(false, token);

        /// <summary>拉取 History。</summary>
        private async Task<IReadOnlyList<PurchaseReceipt>> FetchHistory(bool restore, CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (fetchingPurchases != null || restoring != null || buying != null || confirming.Count != 0)
            {
                throw new InvalidOperationException("Purchase fetching is already active.");
            }
            if (restore && nativeStore == NativeStore.Apple)
            {
                restoring = Signal<bool>();
                var restored = restoring;
                try
                {
                    controller.RestoreTransactions((success, _) =>
                    {
                        if (success)
                        {
                            restored.TrySetResult(true);
                        }
                        else
                        {
                            restored.TrySetException(new InvalidOperationException("Apple restore failed."));
                        }
                    });
                    await restoring.Task;
                    ThrowIfDisposed();
                    token.ThrowIfCancellationRequested();
                }
                finally
                {
                    restoring = null;
                }
            }
            fetchingPurchases = Signal<IReadOnlyList<PurchaseReceipt>>();
            try
            {
                controller.FetchPurchases();
                var result = await fetchingPurchases.Task;
                token.ThrowIfCancellationRequested();
                return result;
            }
            finally
            {
                fetchingPurchases = null;
            }
        }

        /// <summary>异步确认 Unity 内购商店。</summary>
        public async Task ConfirmAsync(PurchaseReceipt receipt, CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (receipt == null || receipt.Store != StoreName || !pending.TryGetValue(receipt.TransactionId, out var order))
            {
                throw new InvalidOperationException("Only an owned, currently pending transaction can be confirmed.");
            }
            if (confirming.ContainsKey(receipt.TransactionId))
            {
                throw new InvalidOperationException("This transaction is already being confirmed.");
            }
            var actual = ToReceipt(order);
            if (receipt.ProductId != actual.ProductId || receipt.Receipt != actual.Receipt || receipt.AppleJws != actual.AppleJws)
            {
                throw new InvalidOperationException("The receipt does not match the pending store transaction.");
            }
            var signal = Signal<bool>();
            confirming.Add(receipt.TransactionId, signal);
            try
            {
                controller.ConfirmPurchase(order);
                await signal.Task;
            }
            finally
            {
                confirming.Remove(receipt.TransactionId);
            }
        }

        private string StoreName => nativeStore == NativeStore.Apple ? "AppleAppStore" : "GooglePlay";

        /// <summary>调用 values.FirstOrDefault 完成商品列表已获取。</summary>
        private void ProductsFetched(List<Product> values)
        {
            if (disposed)
            {
                return;
            }
            var available = new List<StoreProduct>();
            foreach (var definition in catalog)
            {
                Product value = values.FirstOrDefault(p => p.definition.id == definition.Id);
                if (value != null)
                {
                    available.Add(new StoreProduct(definition.Id, definition.StoreId, definition.Kind, value.metadata.localizedPriceString, value.metadata.isoCurrencyCode));
                }
            }
            products = available.AsReadOnly();
            fetchingProducts?.TrySetResult(true);
        }

        /// <summary>调用 .TrySetException 完成商品列表失败。</summary>
        private void ProductsFailed(ProductFetchFailed _) => fetchingProducts?.TrySetException(new InvalidOperationException("Store product fetching failed."));

        /// <summary>将当前值转换为收据。</summary>
        private PurchaseReceipt ToReceipt(Order order, bool requiresConfirmation = true)
        {
            if (!Application.isEditor && order.StoreName != StoreName)
            {
                throw new InvalidOperationException("The transaction belongs to a different store.");
            }
            var items = order.CartOrdered.Items().ToArray();
            if (items.Length != 1 || items[0].Quantity != 1)
            {
                throw new NotSupportedException("This adapter supports single-product, single-quantity orders.");
            }
            return new PurchaseReceipt(StoreName, items[0].Product.definition.id, order.Info.TransactionID, order.Info.Receipt, order.Info.Apple?.jwsRepresentation, requiresConfirmation);
        }

        /// <summary>发起购买待处理。</summary>
        private void PurchasePending(PendingOrder order)
        {
            if (disposed)
            {
                return;
            }
            try
            {
                var receipt = ToReceipt(order);
                pending[receipt.TransactionId] = order;
                if (receipt.ProductId == buyingProduct)
                {
                    buying?.TrySetResult(new StorePurchase(StorePurchaseStatus.PendingVerification, receipt));
                }
            }
            catch (Exception error)
            {
                buying?.TrySetException(error);
                fetchingPurchases?.TrySetException(error);
            }
        }

        /// <summary>发起购买失败。</summary>
        private void PurchaseFailed(FailedOrder order)
        {
            if (!MatchesCurrentPurchase(order))
            {
                return;
            }
            if (order.FailureReason == PurchaseFailureReason.UserCancelled)
            {
                buying?.TrySetResult(new StorePurchase(StorePurchaseStatus.Cancelled));
            }
            else
            {
                buying?.TrySetException(new InvalidOperationException("Store purchase failed: " + order.FailureReason));
            }
        }

        /// <summary>发起购买 Deferred。</summary>
        private void PurchaseDeferred(DeferredOrder order)
        {
            if (MatchesCurrentPurchase(order))
            {
                buying?.TrySetResult(new StorePurchase(StorePurchaseStatus.Deferred));
            }
        }

        /// <summary>判断是否匹配当前购买。</summary>
        private bool MatchesCurrentPurchase(Order order)
        {
            return !disposed && buyingProduct != null && order.CartOrdered.Items().Any(item => item.Product.definition.id == buyingProduct);
        }

        /// <summary>发起购买 Confirmed。</summary>
        private void PurchaseConfirmed(Order order)
        {
            if (confirming.TryGetValue(order.Info.TransactionID, out var signal))
            {
                if (order is FailedOrder)
                {
                    signal.TrySetException(new InvalidOperationException("Store confirmation failed; the transaction remains pending."));
                }
                else
                {
                    pending.Remove(order.Info.TransactionID);
                    signal.TrySetResult(true);
                }
            }
        }

        /// <summary>调用 ToReceipt 完成购买记录已获取。</summary>
        private void PurchasesFetched(Orders orders)
        {
            try
            {
                var receipts = new List<PurchaseReceipt>();
                foreach (var order in orders.PendingOrders)
                {
                    var receipt = ToReceipt(order);
                    pending[receipt.TransactionId] = order;
                    receipts.Add(receipt);
                }
                foreach (var order in orders.ConfirmedOrders)
                {
                    // Completed consumables are not inventory and must never be restored as new grants.
                    if (order.CartOrdered.Items().Any(item => item.Product.definition.type != ProductType.Consumable))
                    {
                        receipts.Add(ToReceipt(order, false));
                    }
                }
                fetchingPurchases?.TrySetResult(receipts.AsReadOnly());
            }
            catch (Exception error)
            {
                fetchingPurchases?.TrySetException(error);
            }
        }

        /// <summary>调用 .TrySetException 完成购买记录失败。</summary>
        private void PurchasesFailed(PurchasesFetchFailureDescription _) => fetchingPurchases?.TrySetException(new InvalidOperationException("Store purchase history fetching failed."));
        /// <summary>调用 .TrySetException 完成Disconnected。</summary>
        private void Disconnected(StoreConnectionFailureDescription _)
        {
            initialized = false;
            var error = new InvalidOperationException("The store disconnected. Recreate after active native transactions finish.");
            fetchingProducts?.TrySetException(error);
            fetchingPurchases?.TrySetException(error);
            restoring?.TrySetException(error);
            // A connection error cannot prove a native checkout/confirmation has stopped.
        }

        /// <summary>调用 Array.Empty&lt;StoreProduct&gt; 完成账号变化。</summary>
        private void AccountChanged()
        {
            initialized = false;
            products = Array.Empty<StoreProduct>();
            pending.Clear();
            FailWaiting(new InvalidOperationException("The store account changed. Dispose and reinitialize this adapter."));
        }

        /// <summary>标记失败 Waiting。</summary>
        private void FailWaiting(Exception error)
        {
            fetchingProducts?.TrySetException(error);
            restoring?.TrySetException(error);
            fetchingPurchases?.TrySetException(error);
            buying?.TrySetException(error);
            foreach (var signal in confirming.Values)
            {
                signal.TrySetException(error);
            }
        }

        /// <summary>调用 MainThread 完成就绪。</summary>
        private void Ready()
        {
            MainThread();
            ThrowIfDisposed();
            if (!initialized)
            {
                throw new InvalidOperationException("Await store initialization first.");
            }
        }

        /// <summary>对象已结束使用时抛出异常。</summary>
        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(UnityIapStore));
            }
        }

        /// <summary>校验当前调用位于 Unity 主线程。</summary>
        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use Unity IAP on the initialized Unity main thread.");
            }
        }

        /// <summary>取得信号。</summary>
        private static TaskCompletionSource<T> Signal<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>结束Unity 内购商店对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            MainThread();
            if (disposed)
            {
                return;
            }
            if (connecting || buying != null || restoring != null || fetchingPurchases != null || confirming.Count != 0)
            {
                throw new InvalidOperationException("Await native IAP operations before disposing their adapter.");
            }
            disposed = true;
            FailWaiting(new ObjectDisposedException(nameof(UnityIapStore)));
            if (controller != null)
            {
                controller.OnProductsFetched -= ProductsFetched;
                controller.OnProductsFetchFailed -= ProductsFailed;
                controller.OnPurchasePending -= PurchasePending;
                controller.OnPurchaseFailed -= PurchaseFailed;
                controller.OnPurchaseDeferred -= PurchaseDeferred;
                controller.OnPurchaseConfirmed -= PurchaseConfirmed;
                controller.OnPurchasesFetched -= PurchasesFetched;
                controller.OnPurchasesFetchFailed -= PurchasesFailed;
                controller.OnStoreDisconnected -= Disconnected;
                controller.OnAuthAccountChanged -= AccountChanged;
            }
            if (ReferenceEquals(owner, this))
            {
                owner = null;
            }
            pending.Clear();
        }
    }
}

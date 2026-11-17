using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Payments
{
    /// <summary>定义商店商品种类可选的枚举值。</summary>
    public enum StoreProductKind
    {
        Consumable,
        NonConsumable,
        Subscription
    }

    /// <summary>定义商店购买状态可选的枚举值。</summary>
    public enum StorePurchaseStatus
    {
        PendingVerification,
        Deferred,
        Cancelled
    }

    /// <summary>提供商店商品的实现。</summary>
    public sealed class StoreProduct
    {
        public string Id { get; }
        public string StoreId { get; }
        public StoreProductKind Kind { get; }
        public string LocalizedPrice { get; }
        public string Currency { get; }

        /// <summary>使用调用方提供的id、storeId、kind创建 StoreProduct。</summary>
        public StoreProduct(string id, string storeId, StoreProductKind kind, string localizedPrice = null, string currency = null)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(storeId) || !Enum.IsDefined(typeof(StoreProductKind), kind))
            {
                throw new ArgumentException("Valid logical/store product IDs and kind are required.");
            }
            Id = id;
            StoreId = storeId;
            Kind = kind;
            LocalizedPrice = localizedPrice;
            Currency = currency;
        }
    }

    /// <summary>提供购买收据的实现。</summary>
    public sealed class PurchaseReceipt
    {
        public string Store { get; }
        public string ProductId { get; }
        public string TransactionId { get; }
        public string Receipt { get; }
        public string AppleJws { get; }
        public bool RequiresConfirmation { get; }
        public string IdempotencyKey => Store + ":" + TransactionId;

        /// <summary>使用调用方提供的store、productId、transactionId创建 PurchaseReceipt。</summary>
        public PurchaseReceipt(string store, string productId, string transactionId, string receipt, string appleJws = null, bool requiresConfirmation = true)
        {
            if (string.IsNullOrWhiteSpace(store) || string.IsNullOrWhiteSpace(productId) || string.IsNullOrWhiteSpace(transactionId))
            {
                throw new ArgumentException("Store, product and transaction IDs are required.");
            }
            if (string.IsNullOrWhiteSpace(receipt) && string.IsNullOrWhiteSpace(appleJws))
            {
                throw new ArgumentException("A signed receipt or Apple JWS is required.");
            }
            Store = store;
            ProductId = productId;
            TransactionId = transactionId;
            Receipt = receipt;
            AppleJws = appleJws;
            RequiresConfirmation = requiresConfirmation;
        }

        /// <summary>格式化当前值，供显示或诊断使用。</summary>
        public override string ToString() => Store + " purchase (receipt redacted)";
    }

    /// <summary>提供商店购买的实现。</summary>
    public sealed class StorePurchase
    {
        public StorePurchaseStatus Status { get; }
        public PurchaseReceipt Receipt { get; }

        /// <summary>使用调用方提供的status、receipt创建 StorePurchase。</summary>
        public StorePurchase(StorePurchaseStatus status, PurchaseReceipt receipt = null)
        {
            if (!Enum.IsDefined(typeof(StorePurchaseStatus), status) ||
                (status == StorePurchaseStatus.PendingVerification && receipt == null))
            {
                throw new ArgumentException("A pending purchase needs its receipt.");
            }
            Status = status;
            Receipt = receipt;
        }
    }

    /// <summary>定义支付商店的调用契约。</summary>
    public interface IPaymentStore : IDisposable
    {
        IReadOnlyList<StoreProduct> Products { get; }
        /// <summary>异步初始化支付商店。</summary>
        Task InitializeAsync(CancellationToken cancellationToken);
        /// <summary>异步发起购买支付商店。</summary>
        Task<StorePurchase> PurchaseAsync(string productId, CancellationToken cancellationToken);
        /// <summary>Fetch known pending orders/entitlements without explicitly invoking Apple's restore UI.</summary>
        /// <remarks>异步拉取购买记录。</remarks>
        Task<IReadOnlyList<PurchaseReceipt>> FetchPurchasesAsync(CancellationToken cancellationToken);
        /// <summary>异步恢复支付商店。</summary>
        Task<IReadOnlyList<PurchaseReceipt>> RestoreAsync(CancellationToken cancellationToken);
        /// <summary>异步确认支付商店。</summary>
        Task ConfirmAsync(PurchaseReceipt receipt, CancellationToken cancellationToken);
    }

    /// <summary>Call a trusted backend. Success means verification AND durable idempotent fulfilment completed.</summary>
    /// <remarks>定义购买 Fulfillment的调用契约。</remarks>
    public interface IPurchaseFulfillment
    {
        /// <summary>异步核验 AndGrant。</summary>
        Task VerifyAndGrantAsync(PurchaseReceipt receipt, CancellationToken cancellationToken);
    }

    /// <summary>支付对应的结果。携带操作执行结果和相关数据。</summary>
    public sealed class PaymentResult
    {
        public StorePurchaseStatus StoreStatus { get; }
        public bool Confirmed { get; }
        public string TransactionId { get; }
        /// <summary>使用调用方提供的status、confirmed、transactionId创建 PaymentResult。</summary>
        internal PaymentResult(StorePurchaseStatus status, bool confirmed, string transactionId)
        {
            StoreStatus = status;
            Confirmed = confirmed;
            TransactionId = transactionId;
        }
    }
}

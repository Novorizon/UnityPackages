using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Payments
{
    public enum StoreProductKind
    {
        Consumable,
        NonConsumable,
        Subscription
    }

    public enum StorePurchaseStatus
    {
        PendingVerification,
        Deferred,
        Cancelled
    }

    public sealed class StoreProduct
    {
        public string Id { get; }
        public string StoreId { get; }
        public StoreProductKind Kind { get; }
        public string LocalizedPrice { get; }
        public string Currency { get; }

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

    public sealed class PurchaseReceipt
    {
        public string Store { get; }
        public string ProductId { get; }
        public string TransactionId { get; }
        public string Receipt { get; }
        public string AppleJws { get; }
        public bool RequiresConfirmation { get; }
        public string IdempotencyKey => Store + ":" + TransactionId;

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

        public override string ToString() => Store + " purchase (receipt redacted)";
    }

    public sealed class StorePurchase
    {
        public StorePurchaseStatus Status { get; }
        public PurchaseReceipt Receipt { get; }

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

    public interface IPaymentStore : IDisposable
    {
        IReadOnlyList<StoreProduct> Products { get; }
        Task InitializeAsync(CancellationToken cancellationToken);
        Task<StorePurchase> PurchaseAsync(string productId, CancellationToken cancellationToken);
        /// <summary>Fetch known pending orders/entitlements without explicitly invoking Apple's restore UI.</summary>
        Task<IReadOnlyList<PurchaseReceipt>> FetchPurchasesAsync(CancellationToken cancellationToken);
        Task<IReadOnlyList<PurchaseReceipt>> RestoreAsync(CancellationToken cancellationToken);
        Task ConfirmAsync(PurchaseReceipt receipt, CancellationToken cancellationToken);
    }

    /// <summary>Call a trusted backend. Success means verification AND durable idempotent fulfilment completed.</summary>
    public interface IPurchaseFulfillment
    {
        Task VerifyAndGrantAsync(PurchaseReceipt receipt, CancellationToken cancellationToken);
    }

    public sealed class PaymentResult
    {
        public StorePurchaseStatus StoreStatus { get; }
        public bool Confirmed { get; }
        public string TransactionId { get; }
        internal PaymentResult(StorePurchaseStatus status, bool confirmed, string transactionId)
        {
            StoreStatus = status;
            Confirmed = confirmed;
            TransactionId = transactionId;
        }
    }
}

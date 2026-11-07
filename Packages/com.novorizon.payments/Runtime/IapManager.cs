namespace Game.Framework.Payments
{
    /// <summary>Business-facing entry point; configure vendor adapters at application startup.</summary>
    public sealed class IapManager : PaymentClient
    {
        public IapManager(IPaymentStore store, IPurchaseFulfillment fulfillment, bool ownsStore = false) : base(store, fulfillment, ownsStore)
        {
        }
    }
}

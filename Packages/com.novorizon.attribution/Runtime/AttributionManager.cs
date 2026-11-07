namespace Game.Framework.Attribution
{
    /// <summary>Business-facing entry point; configure vendor adapters at application startup.</summary>
    public sealed class AttributionManager : AttributionClient
    {
        public AttributionManager(IAttributionBackend backend, bool ownsBackend = false) : base(backend, ownsBackend)
        {
        }
    }
}

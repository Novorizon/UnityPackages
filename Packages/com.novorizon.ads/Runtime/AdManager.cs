namespace Game.Framework.Ads
{
    /// <summary>Business-facing entry point; configure vendor adapters at application startup.</summary>
    public sealed class AdManager : AdsClient
    {
        public AdManager(IAdProvider provider, bool ownsProvider = false) : base(provider, ownsProvider)
        {
        }
    }
}

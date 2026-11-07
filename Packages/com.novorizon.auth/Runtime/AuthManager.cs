using System.Collections.Generic;

namespace Game.Framework.Auth
{
    /// <summary>Business-facing entry point; configure vendor adapters at application startup.</summary>
    public sealed class AuthManager : AuthClient
    {
        public AuthManager(
            IAuthBackend backend,
            IEnumerable<IIdentityProvider> providers,
            bool ownsDependencies = false) : base(backend, providers, ownsDependencies)
        {
        }
    }
}

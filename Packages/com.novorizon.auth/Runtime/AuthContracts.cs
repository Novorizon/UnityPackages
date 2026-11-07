using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Auth
{
    public enum IdentityProvider
    {
        Apple,
        Google,
        Facebook
    }

    public enum IdentityTokenKind
    {
        IdToken,
        AccessToken
    }

    /// <summary>Short-lived provider proof. Never log or persist its secret fields.</summary>
    public sealed class IdentityProof : IDisposable
    {
        private string token;
        private string rawNonce;
        private string accessToken;
        public IdentityProvider Provider { get; }
        public IdentityTokenKind Kind { get; }
        public string Token => token ?? throw new ObjectDisposedException(nameof(IdentityProof));
        public string RawNonce => token != null ? rawNonce : throw new ObjectDisposedException(nameof(IdentityProof));
        public string AccessToken => token != null ? accessToken : throw new ObjectDisposedException(nameof(IdentityProof));

        public IdentityProof(
            IdentityProvider provider,
            IdentityTokenKind kind,
            string token,
            string rawNonce = null,
            string accessToken = null)
        {
            if (!Enum.IsDefined(typeof(IdentityProvider), provider) || !Enum.IsDefined(typeof(IdentityTokenKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(provider));
            }
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new ArgumentException("A provider token is required.", nameof(token));
            }
            Provider = provider;
            Kind = kind;
            this.token = token;
            this.rawNonce = rawNonce;
            this.accessToken = accessToken;
        }

        public void Dispose()
        {
            token = rawNonce = accessToken = null;
        }

        public override string ToString() => Provider + " proof (redacted)";
    }

    public sealed class AuthUser
    {
        public string Id { get; }
        public bool IsAnonymous { get; }
        public IReadOnlyList<string> LinkedProviders { get; }

        public AuthUser(string id, bool anonymous, IEnumerable<string> linkedProviders)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("The verified backend user ID is required.", nameof(id));
            }
            Id = id;
            IsAnonymous = anonymous;
            LinkedProviders = new List<string>(linkedProviders ?? Array.Empty<string>()).AsReadOnly();
        }
    }

    public interface IIdentityProvider : IDisposable
    {
        IdentityProvider Provider { get; }
        Task<IdentityProof> AcquireAsync(CancellationToken cancellationToken);
    }

    /// <summary>The backend verifies proofs and owns identity conflicts. It must not merge by email.</summary>
    public interface IAuthBackend : IDisposable
    {
        AuthUser CurrentUser { get; }
        Task<AuthUser> SignInAnonymouslyAsync(CancellationToken cancellationToken);
        Task<AuthUser> SignInAsync(IdentityProof proof, CancellationToken cancellationToken);
        Task<AuthUser> LinkAsync(string expectedUserId, IdentityProof proof, CancellationToken cancellationToken);
        Task<AuthUser> UnlinkAsync(string expectedUserId, string providerId, CancellationToken cancellationToken);
        Task<string> GetSessionTokenAsync(bool forceRefresh, CancellationToken cancellationToken);
        void SignOut();
    }
}

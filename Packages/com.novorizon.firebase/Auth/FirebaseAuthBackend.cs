using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Auth;
using AuthSdk = global::Firebase.Auth.FirebaseAuth;
using Credential = global::Firebase.Auth.Credential;

namespace Game.Framework.Firebase.Unity
{
    /// <summary>Uses the project's Firebase Auth instance; disposal never signs out another owner.</summary>
    public sealed class FirebaseAuthBackend : IAuthBackend
    {
        private static FirebaseAuthBackend owner;
        private readonly AuthSdk sdk;
        private bool disposed, busy;

        public FirebaseAuthBackend(FirebaseRuntime runtime)
        {
            FirebaseRuntime.MainThread();
            if (owner != null)
            {
                throw new InvalidOperationException("Another backend owns the project's default Firebase Auth instance.");
            }
            sdk = AuthSdk.GetAuth((runtime ?? throw new ArgumentNullException(nameof(runtime))).App);
            owner = this;
        }

        public AuthUser CurrentUser
        {
            get
            {
                Ready();
                var user = sdk.CurrentUser;
                return user == null ? null : new AuthUser(user.UserId, user.IsAnonymous, user.ProviderData.Select(p => p.ProviderId));
            }
        }

        public async Task<AuthUser> SignInAnonymouslyAsync(CancellationToken token)
        {
            using var operation = Begin(token);
            if (sdk.CurrentUser != null && !sdk.CurrentUser.IsAnonymous)
            {
                throw new InvalidOperationException("Sign out explicitly before creating a guest over a signed-in account.");
            }
            await sdk.SignInAnonymouslyAsync();
            return CurrentUser;
        }

        public async Task<AuthUser> SignInAsync(IdentityProof proof, CancellationToken token)
        {
            using var operation = Begin(token);
            using (var credential = BuildCredential(proof))
            {
                await sdk.SignInWithCredentialAsync(credential);
            }
            // Native Firebase auth cannot be rolled back by cancelling a waiter. Publish its actual state.
            return CurrentUser;
        }

        public async Task<AuthUser> LinkAsync(string expectedUserId, IdentityProof proof, CancellationToken token)
        {
            using var operation = Begin(token);
            var user = ExpectedUser(expectedUserId);
            token.ThrowIfCancellationRequested();
            using (var credential = BuildCredential(proof))
            {
                await user.LinkWithCredentialAsync(credential);
            }
            if (sdk.CurrentUser?.UserId != expectedUserId)
            {
                throw new InvalidOperationException("The Firebase account changed while linking.");
            }
            return CurrentUser;
        }

        public async Task<AuthUser> UnlinkAsync(string expectedUserId, string providerId, CancellationToken token)
        {
            using var operation = Begin(token);
            var user = ExpectedUser(expectedUserId);
            token.ThrowIfCancellationRequested();
            await user.UnlinkAsync(providerId);
            ExpectedUser(expectedUserId);
            return CurrentUser;
        }

        public async Task<string> GetSessionTokenAsync(bool forceRefresh, CancellationToken token)
        {
            using var operation = Begin(token);
            var user = sdk.CurrentUser ?? throw new InvalidOperationException("Sign in before requesting a session token.");
            string result = await user.TokenAsync(forceRefresh);
            token.ThrowIfCancellationRequested();
            if (sdk.CurrentUser?.UserId != user.UserId)
            {
                throw new InvalidOperationException("The Firebase account changed while refreshing the token.");
            }
            return result;
        }

        private global::Firebase.Auth.FirebaseUser ExpectedUser(string id)
        {
            Ready();
            var user = sdk.CurrentUser;
            if (user == null || user.UserId != id)
            {
                throw new InvalidOperationException("The Firebase account does not match the operation owner.");
            }
            return user;
        }

        private static Credential BuildCredential(IdentityProof proof)
        {
            if (proof == null)
            {
                throw new ArgumentNullException(nameof(proof));
            }
            switch (proof.Provider)
            {
                case IdentityProvider.Apple when proof.Kind == IdentityTokenKind.IdToken && !string.IsNullOrEmpty(proof.RawNonce):
                    return global::Firebase.Auth.OAuthProvider.GetCredential("apple.com", proof.Token, proof.RawNonce, proof.AccessToken);
                case IdentityProvider.Google when proof.Kind == IdentityTokenKind.IdToken:
                    return global::Firebase.Auth.GoogleAuthProvider.GetCredential(proof.Token, proof.AccessToken);
                case IdentityProvider.Facebook when proof.Kind == IdentityTokenKind.AccessToken:
                    return global::Firebase.Auth.FacebookAuthProvider.GetCredential(proof.Token);
                case IdentityProvider.Facebook when proof.Kind == IdentityTokenKind.IdToken && !string.IsNullOrEmpty(proof.RawNonce):
                    return global::Firebase.Auth.OAuthProvider.GetCredential("facebook.com", proof.Token, proof.RawNonce, null);
                default:
                    throw new ArgumentException("The provider proof kind or nonce is invalid.", nameof(proof));
            }
        }

        public void SignOut()
        {
            Ready();
            if (busy)
            {
                throw new InvalidOperationException("Await the active Firebase Auth operation before signing out.");
            }
            sdk.SignOut();
        }

        private Operation Begin(CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (busy)
            {
                throw new InvalidOperationException("A Firebase Auth operation is already active.");
            }
            busy = true;
            return new Operation(this);
        }

        private sealed class Operation : IDisposable
        {
            private FirebaseAuthBackend backend;

            internal Operation(FirebaseAuthBackend backend)
            {
                this.backend = backend;
            }

            public void Dispose()
            {
                if (backend != null)
                {
                    backend.busy = false;
                    if (backend.disposed && ReferenceEquals(owner, backend))
                    {
                        owner = null;
                    }
                    backend = null;
                }
            }
        }

        private void Ready()
        {
            FirebaseRuntime.MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(FirebaseAuthBackend));
            }
        }

        public void Dispose()
        {
            FirebaseRuntime.MainThread();
            disposed = true;
            if (!busy && ReferenceEquals(owner, this))
            {
                owner = null;
            }
        }
    }
}

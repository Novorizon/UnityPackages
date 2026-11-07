using System;
using System.Threading;
using System.Threading.Tasks;
using Facebook.Unity;
using UnityEngine;

namespace Game.Framework.Auth.Facebook
{
    /// <summary>Facebook SDK 19. iOS uses Limited Login with a fresh nonce; Android uses an access token.</summary>
    public sealed class FacebookIdentityProvider : IIdentityProvider
    {
        private static FacebookIdentityProvider owner;
        private bool busy, disposed;
        public IdentityProvider Provider => IdentityProvider.Facebook;

        public async Task<IdentityProof> AcquireAsync(CancellationToken token)
        {
            MainThread();
            if (disposed || busy || (owner != null && owner != this))
            {
                throw new InvalidOperationException("Facebook sign-in already has an owner, is busy or is disposed.");
            }
            token.ThrowIfCancellationRequested();
            if (Application.isEditor || (Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.IPhonePlayer))
            {
                throw new PlatformNotSupportedException("Use Facebook identity in an Android or iOS player.");
            }
            owner = this;
            busy = true;
            try
            {
                if (!FB.IsInitialized)
                {
                    var initialized = new TaskCompletionSource<bool>();
                    FB.Init(() => initialized.TrySetResult(FB.IsInitialized));
                    if (!await initialized.Task)
                    {
                        throw new InvalidOperationException("Facebook SDK initialization failed. Configure its App ID first.");
                    }
                }
                token.ThrowIfCancellationRequested();
                string nonce = IdentityNonce.Create();
                string challenge = IdentityNonce.Sha256(nonce);
                var completion = new TaskCompletionSource<ILoginResult>();
                if (Application.platform == RuntimePlatform.IPhonePlayer)
                {
                    FB.Mobile.LoginWithTrackingPreference(LoginTracking.LIMITED, new[] { "public_profile" }, challenge, value => completion.TrySetResult(value));
                }
                else
                {
                    FB.LogInWithReadPermissions(new[] { "public_profile" }, value => completion.TrySetResult(value));
                }
                // Retain the SDK owner even if a caller stops waiting for its native popup.
                var result = await completion.Task;
                token.ThrowIfCancellationRequested();
                if (result == null || result.Cancelled)
                {
                    throw new OperationCanceledException("The player cancelled Facebook sign-in.");
                }
                if (!string.IsNullOrEmpty(result.Error))
                {
                    // Raw SDK error payloads can contain credentials; do not propagate them into logs.
                    throw new InvalidOperationException("Facebook sign-in failed.");
                }
                if (Application.platform == RuntimePlatform.IPhonePlayer)
                {
                    return LimitedProof(nonce, FB.Mobile.CurrentAuthenticationToken());
                }
                var access = AccessToken.CurrentAccessToken;
                if (access == null)
                {
                    throw new InvalidOperationException("Facebook returned no access token.");
                }
                return new IdentityProof(Provider, IdentityTokenKind.AccessToken, access.TokenString);
            }
            finally
            {
                busy = false;
                owner = null;
            }
        }

        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use Facebook identity on the initialized Unity main thread.");
            }
        }

        internal static IdentityProof LimitedProof(string rawNonce, AuthenticationToken authentication)
        {
            // Firebase expects the raw nonce and validates SHA-256(rawNonce) against the ID token.
            if (authentication == null || authentication.Nonce != IdentityNonce.Sha256(rawNonce))
            {
                throw new InvalidOperationException("Facebook returned a missing or mismatched nonce.");
            }
            return new IdentityProof(IdentityProvider.Facebook, IdentityTokenKind.IdToken, authentication.TokenString, rawNonce);
        }

        public void Dispose()
        {
            MainThread();
            if (busy)
            {
                throw new InvalidOperationException("Await the Facebook callback before disposing its provider.");
            }
            disposed = true;
        }
    }
}

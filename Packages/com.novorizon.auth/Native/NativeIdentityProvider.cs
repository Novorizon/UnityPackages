using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Framework.Auth.Native
{
    /// <summary>Apple AuthenticationServices on iOS; Google native SDK on iOS and Credential Manager on Android.</summary>
    public sealed class NativeIdentityProvider : IIdentityProvider
    {
        private readonly string googleWebClientId;
        private readonly string googleIosClientId;
        private IdentityCallbackHost host;
        private bool listeningForUrls;
        private bool busy, disposed;
        public IdentityProvider Provider { get; }

        public NativeIdentityProvider(IdentityProvider provider, string googleWebClientId = null, string googleIosClientId = null)
        {
            if (provider != IdentityProvider.Apple && provider != IdentityProvider.Google)
            {
                throw new ArgumentException("Use the Facebook adapter for Facebook.", nameof(provider));
            }
            Provider = provider;
            this.googleWebClientId = googleWebClientId;
            this.googleIosClientId = googleIosClientId;
        }

        public async Task<IdentityProof> AcquireAsync(CancellationToken token)
        {
            MainThread();
            if (disposed || busy)
            {
                throw new InvalidOperationException("The native identity provider is disposed or busy.");
            }
            token.ThrowIfCancellationRequested();
            bool ios = Application.platform == RuntimePlatform.IPhonePlayer;
            bool android = Application.platform == RuntimePlatform.Android;
            if (Application.isEditor || (!ios && !android) || (android && Provider == IdentityProvider.Apple))
            {
                throw new PlatformNotSupportedException("Apple uses the iOS player; Google uses the iOS or Android player.");
            }
            if (Provider == IdentityProvider.Google && (string.IsNullOrWhiteSpace(googleWebClientId) || (ios && string.IsNullOrWhiteSpace(googleIosClientId))))
            {
                throw new InvalidOperationException("Configure the Google web client ID and, on iOS, the iOS client ID.");
            }
            busy = true;
            string nonce = IdentityNonce.Create();
            string request = Guid.NewGuid().ToString("N");
            try
            {
                if (host == null)
                {
                    var root = new GameObject("NovorizonIdentity_" + Guid.NewGuid().ToString("N"));
                    Object.DontDestroyOnLoad(root);
                    host = root.AddComponent<IdentityCallbackHost>();
                }
                Task<IdentityCallbackHost.Response> completion = host.Begin(request);
                try
                {
                    StartNative(host.gameObject.name, request, nonce);
                }
                catch
                {
                    host.FailStart();
                    await completion;
                    throw;
                }
                // Caller cancellation cannot close every platform popup. Retain Busy until its callback retires.
                var response = await completion;
                token.ThrowIfCancellationRequested();
                if (response.cancelled)
                {
                    throw new OperationCanceledException("The player cancelled native sign-in.");
                }
                if (!string.IsNullOrEmpty(response.error) || string.IsNullOrEmpty(response.token))
                {
                    throw new InvalidOperationException("Native sign-in failed: " + (response.error ?? "no_token"));
                }
                return new IdentityProof(Provider, IdentityTokenKind.IdToken, response.token, nonce, response.accessToken);
            }
            finally
            {
                busy = false;
            }
        }

        private void StartNative(string receiver, string request, string nonce)
        {
#if UNITY_IOS && !UNITY_EDITOR && NOVORIZON_NATIVE_AUTH
            if (Provider == IdentityProvider.Apple)
            {
                NovorizonAppleSignIn(receiver, request, IdentityNonce.Sha256(nonce));
            }
            else
            {
                if (!listeningForUrls)
                {
                    Application.deepLinkActivated += HandleUrl;
                    listeningForUrls = true;
                }
                NovorizonGoogleSignIn(receiver, request, googleIosClientId, googleWebClientId, nonce);
            }
#elif UNITY_ANDROID && !UNITY_EDITOR && NOVORIZON_NATIVE_AUTH
            using (var bridge = new AndroidJavaClass("com.novorizon.auth.GoogleIdentityBridge"))
            {
                bridge.CallStatic("signIn", receiver, request, googleWebClientId, nonce);
            }
#else
            throw new PlatformNotSupportedException("Install native bridges using Novorizon/Platform SDKs/Install native identity, then build an iOS/Android player.");
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR && NOVORIZON_NATIVE_AUTH
        [DllImport("__Internal")]
        private static extern void NovorizonAppleSignIn(string receiver, string request, string hashedNonce);
        [DllImport("__Internal")]
        private static extern void NovorizonGoogleSignIn(string receiver, string request, string iosClientId, string webClientId, string nonce);
        [DllImport("__Internal")]
        private static extern void NovorizonGoogleHandleUrl(string url);
#endif

        private void HandleUrl(string url)
        {
#if UNITY_IOS && !UNITY_EDITOR && NOVORIZON_NATIVE_AUTH
            if (!disposed && !string.IsNullOrEmpty(url))
            {
                NovorizonGoogleHandleUrl(url);
            }
#endif
        }

        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use native identity on the initialized Unity main thread.");
            }
        }

        public void Dispose()
        {
            MainThread();
            if (busy)
            {
                throw new InvalidOperationException("Wait for the native sign-in callback before disposing its provider.");
            }
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (listeningForUrls)
            {
                Application.deepLinkActivated -= HandleUrl;
                listeningForUrls = false;
            }
            if (host != null)
            {
                Object.Destroy(host.gameObject);
                host = null;
            }
        }
    }
}

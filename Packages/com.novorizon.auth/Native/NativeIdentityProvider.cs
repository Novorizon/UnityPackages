using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Game.Framework.Auth.Native
{
    /// <summary>Apple AuthenticationServices on iOS; Google native SDK on iOS and Credential Manager on Android.</summary>
    /// <remarks>原生身份对应的厂商适配器。提供此模块需要的能力或数据。</remarks>
    public sealed class NativeIdentityProvider : IIdentityProvider
    {
        private readonly string googleWebClientId;
        private readonly string googleIosClientId;
        private IdentityCallbackHost host;
        private bool listeningForUrls;
        private bool busy, disposed;
        public IdentityProvider Provider { get; }

        /// <summary>使用调用方提供的provider、googleWebClientId、googleIosClientId创建 NativeIdentityProvider。</summary>
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

        /// <summary>异步取得原生身份厂商适配器。</summary>
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

        /// <summary>启动原生。</summary>
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
        /// <summary>处理NovorizonAppleSignIn。</summary>
        [DllImport("__Internal")]
        private static extern void NovorizonAppleSignIn(string receiver, string request, string hashedNonce);
        /// <summary>处理NovorizonGoogleSignIn。</summary>
        [DllImport("__Internal")]
        private static extern void NovorizonGoogleSignIn(string receiver, string request, string iosClientId, string webClientId, string nonce);
        /// <summary>处理NovorizonGoogle 句柄 URL。</summary>
        [DllImport("__Internal")]
        private static extern void NovorizonGoogleHandleUrl(string url);
#endif

        /// <summary>处理 URL。</summary>
        private void HandleUrl(string url)
        {
#if UNITY_IOS && !UNITY_EDITOR && NOVORIZON_NATIVE_AUTH
            if (!disposed && !string.IsNullOrEmpty(url))
            {
                NovorizonGoogleHandleUrl(url);
            }
#endif
        }

        /// <summary>校验当前调用位于 Unity 主线程。</summary>
        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use native identity on the initialized Unity main thread.");
            }
        }

        /// <summary>结束原生身份厂商适配器对相关资源的持有，并执行该类型的清理流程。</summary>
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

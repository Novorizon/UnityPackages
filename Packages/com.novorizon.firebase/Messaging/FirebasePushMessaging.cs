using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MessagingSdk = global::Firebase.Messaging.FirebaseMessaging;

namespace Game.Framework.Firebase.Unity
{
    /// <summary>提供FirebasePush 推送的实现。</summary>
    public sealed class FirebasePushMessaging : IPushMessaging
    {
        private bool disposed, permissionActive;
        public event Action<string> InstallationRegistered;
        public event Action<PushMessage> MessageReceived;

        /// <summary>使用调用方提供的runtime创建 FirebasePushMessaging。</summary>
        public FirebasePushMessaging(FirebaseManager runtime)
        {
            FirebaseManager.MainThread();
            if (runtime == null || !runtime.IsInitialized)
            {
                throw new InvalidOperationException("Await Firebase initialization first.");
            }
            MessagingSdk.RegistrationReceived += OnRegistration;
            MessagingSdk.MessageReceived += OnMessage;
        }

        /// <summary>根据传入参数取得请求权限 Async。</summary>
        public async Task RequestPermissionAsync(CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            if (permissionActive)
            {
                throw new InvalidOperationException("A notification permission request is already active.");
            }
            permissionActive = true;
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (var version = new UnityEngine.AndroidJavaClass("android.os.Build$VERSION"))
                {
                    const string permission = "android.permission.POST_NOTIFICATIONS";
                    if (version.GetStatic<int>("SDK_INT") >= 33 && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission))
                    {
                        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var callbacks = new UnityEngine.Android.PermissionCallbacks();
                        callbacks.PermissionGranted += _ => completion.TrySetResult(true);
                        callbacks.PermissionDenied += _ => completion.TrySetResult(false);
                        callbacks.PermissionDeniedAndDontAskAgain += _ => completion.TrySetResult(false);
                        UnityEngine.Android.Permission.RequestUserPermission(permission, callbacks);
                        if (!await completion.Task)
                        {
                            throw new UnauthorizedAccessException("Notification permission was denied.");
                        }
                    }
                }
#endif
                await MessagingSdk.RequestPermissionAsync();
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                permissionActive = false;
            }
        }

        /// <summary>异步订阅 FirebasePush 推送。</summary>
        public async Task SubscribeAsync(string topic, CancellationToken token)
        {
            Ready();
            ValidateTopic(topic);
            token.ThrowIfCancellationRequested();
            await MessagingSdk.SubscribeAsync(topic);
            token.ThrowIfCancellationRequested();
        }

        /// <summary>异步登记 FirebasePush 推送。</summary>
        public async Task RegisterAsync(CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            await MessagingSdk.RegisterAsync();
            token.ThrowIfCancellationRequested();
        }

        /// <summary>异步撤销登记 FirebasePush 推送。</summary>
        public async Task UnregisterAsync(CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            await MessagingSdk.UnregisterAsync();
            token.ThrowIfCancellationRequested();
        }

        /// <summary>异步取消订阅 FirebasePush 推送。</summary>
        public async Task UnsubscribeAsync(string topic, CancellationToken token)
        {
            Ready();
            ValidateTopic(topic);
            token.ThrowIfCancellationRequested();
            await MessagingSdk.UnsubscribeAsync(topic);
            token.ThrowIfCancellationRequested();
        }

        /// <summary>验证主题。</summary>
        private static void ValidateTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new ArgumentException("Messaging topic is required.", nameof(topic));
            }
        }

        /// <summary>响应登记。</summary>
        private void OnRegistration(object sender, global::Firebase.Messaging.RegistrationReceivedEventArgs args)
        {
            string installation = args.InstallationId;
            Dispatch(() => Publish(InstallationRegistered, installation));
        }

        /// <summary>响应消息。</summary>
        private void OnMessage(object sender, global::Firebase.Messaging.MessageReceivedEventArgs args)
        {
            var message = args.Message;
            var copy = new PushMessage(
                message.MessageId,
                message.Notification?.Title,
                message.Notification?.Body,
                message.Data == null ? null : new Dictionary<string, string>(message.Data));
            Dispatch(() => Publish(MessageReceived, copy));
        }

        /// <summary>调度 FirebasePush 推送。</summary>
        private void Dispatch(Action action)
        {
            if (disposed)
            {
                return;
            }
            if (UnityThread.IsMainThread)
            {
                action();
            }
            else
            {
                UnityThread.TryPost(() =>
                {
                    if (!disposed)
                    {
                        action();
                    }
                });
            }
        }

        /// <summary>发布 FirebasePush 推送。</summary>
        private static void Publish<T>(Action<T> handlers, T value)
        {
            if (handlers == null)
            {
                return;
            }
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(value);
                }
                catch (Exception error)
                {
                    Logger.Exception(error);
                }
            }
        }

        /// <summary>调用 FirebaseManager.MainThread 完成就绪。</summary>
        private void Ready()
        {
            FirebaseManager.MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(FirebasePushMessaging));
            }
        }

        /// <summary>结束FirebasePush 推送对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            FirebaseManager.MainThread();
            if (disposed)
            {
                return;
            }
            disposed = true;
            MessagingSdk.RegistrationReceived -= OnRegistration;
            MessagingSdk.MessageReceived -= OnMessage;
            InstallationRegistered = null;
            MessageReceived = null;
        }
    }
}

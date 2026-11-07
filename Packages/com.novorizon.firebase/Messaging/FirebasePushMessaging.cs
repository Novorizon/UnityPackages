using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MessagingSdk = global::Firebase.Messaging.FirebaseMessaging;

namespace Game.Framework.Firebase.Unity
{
    public sealed class FirebasePushMessaging : IPushMessaging
    {
        private bool disposed, permissionActive;
        public event Action<string> InstallationRegistered;
        public event Action<PushMessage> MessageReceived;

        public FirebasePushMessaging(FirebaseRuntime runtime)
        {
            FirebaseRuntime.MainThread();
            if (runtime == null || !runtime.IsInitialized)
            {
                throw new InvalidOperationException("Await Firebase initialization first.");
            }
            MessagingSdk.RegistrationReceived += OnRegistration;
            MessagingSdk.MessageReceived += OnMessage;
        }

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

        public async Task SubscribeAsync(string topic, CancellationToken token)
        {
            Ready();
            ValidateTopic(topic);
            token.ThrowIfCancellationRequested();
            await MessagingSdk.SubscribeAsync(topic);
            token.ThrowIfCancellationRequested();
        }

        public async Task RegisterAsync(CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            await MessagingSdk.RegisterAsync();
            token.ThrowIfCancellationRequested();
        }

        public async Task UnregisterAsync(CancellationToken token)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            await MessagingSdk.UnregisterAsync();
            token.ThrowIfCancellationRequested();
        }

        public async Task UnsubscribeAsync(string topic, CancellationToken token)
        {
            Ready();
            ValidateTopic(topic);
            token.ThrowIfCancellationRequested();
            await MessagingSdk.UnsubscribeAsync(topic);
            token.ThrowIfCancellationRequested();
        }

        private static void ValidateTopic(string topic)
        {
            if (string.IsNullOrWhiteSpace(topic))
            {
                throw new ArgumentException("Messaging topic is required.", nameof(topic));
            }
        }

        private void OnRegistration(object sender, global::Firebase.Messaging.RegistrationReceivedEventArgs args)
        {
            string installation = args.InstallationId;
            Dispatch(() => Publish(InstallationRegistered, installation));
        }

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

        private void Ready()
        {
            FirebaseRuntime.MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(FirebasePushMessaging));
            }
        }

        public void Dispose()
        {
            FirebaseRuntime.MainThread();
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

using System;
using CrashSdk = global::Firebase.Crashlytics.Crashlytics;

namespace Game.Framework.Firebase.Unity
{
    public sealed class FirebaseCrashReporter : ICrashReporter
    {
        public FirebaseCrashReporter(FirebaseManager runtime)
        {
            FirebaseManager.MainThread();
            if (runtime == null || !runtime.IsInitialized)
            {
                throw new InvalidOperationException("Await Firebase initialization first.");
            }
        }

        public void SetCollectionEnabled(bool enabled)
        {
            FirebaseManager.MainThread();
            CrashSdk.IsCrashlyticsCollectionEnabled = enabled;
        }

        public void SetUserId(string userId)
        {
            FirebaseManager.MainThread();
            CrashSdk.SetUserId(userId ?? "");
        }

        public void SetKey(string key, string value)
        {
            FirebaseManager.MainThread();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Crash key is required.", nameof(key));
            }
            CrashSdk.SetCustomKey(key, value ?? "");
        }

        public void Log(string message)
        {
            FirebaseManager.MainThread();
            CrashSdk.Log(message ?? "");
        }

        public void Report(Exception exception)
        {
            FirebaseManager.MainThread();
            CrashSdk.LogException(exception ?? throw new ArgumentNullException(nameof(exception)));
        }
    }
}

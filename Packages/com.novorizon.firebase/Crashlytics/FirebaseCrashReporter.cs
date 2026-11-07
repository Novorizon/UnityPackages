using System;
using CrashSdk = global::Firebase.Crashlytics.Crashlytics;

namespace Game.Framework.Firebase.Unity
{
    public sealed class FirebaseCrashReporter : ICrashReporter
    {
        public FirebaseCrashReporter(FirebaseRuntime runtime)
        {
            FirebaseRuntime.MainThread();
            if (runtime == null || !runtime.IsInitialized)
            {
                throw new InvalidOperationException("Await Firebase initialization first.");
            }
        }

        public void SetCollectionEnabled(bool enabled)
        {
            FirebaseRuntime.MainThread();
            CrashSdk.IsCrashlyticsCollectionEnabled = enabled;
        }

        public void SetUserId(string userId)
        {
            FirebaseRuntime.MainThread();
            CrashSdk.SetUserId(userId ?? "");
        }

        public void SetKey(string key, string value)
        {
            FirebaseRuntime.MainThread();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Crash key is required.", nameof(key));
            }
            CrashSdk.SetCustomKey(key, value ?? "");
        }

        public void Log(string message)
        {
            FirebaseRuntime.MainThread();
            CrashSdk.Log(message ?? "");
        }

        public void Report(Exception exception)
        {
            FirebaseRuntime.MainThread();
            CrashSdk.LogException(exception ?? throw new ArgumentNullException(nameof(exception)));
        }
    }
}

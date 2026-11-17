using System;
using CrashSdk = global::Firebase.Crashlytics.Crashlytics;

namespace Game.Framework.Firebase.Unity
{
    /// <summary>提供Firebase 崩溃上报器的实现。</summary>
    public sealed class FirebaseCrashReporter : ICrashReporter
    {
        /// <summary>使用调用方提供的runtime创建 FirebaseCrashReporter。</summary>
        public FirebaseCrashReporter(FirebaseManager runtime)
        {
            FirebaseManager.MainThread();
            if (runtime == null || !runtime.IsInitialized)
            {
                throw new InvalidOperationException("Await Firebase initialization first.");
            }
        }

        /// <summary>设置采集开启。</summary>
        public void SetCollectionEnabled(bool enabled)
        {
            FirebaseManager.MainThread();
            CrashSdk.IsCrashlyticsCollectionEnabled = enabled;
        }

        /// <summary>设置用户标识。</summary>
        public void SetUserId(string userId)
        {
            FirebaseManager.MainThread();
            CrashSdk.SetUserId(userId ?? "");
        }

        /// <summary>设置键。</summary>
        public void SetKey(string key, string value)
        {
            FirebaseManager.MainThread();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Crash key is required.", nameof(key));
            }
            CrashSdk.SetCustomKey(key, value ?? "");
        }

        /// <summary>记录 Firebase 崩溃上报器。</summary>
        public void Log(string message)
        {
            FirebaseManager.MainThread();
            CrashSdk.Log(message ?? "");
        }

        /// <summary>上报 Firebase 崩溃上报器。</summary>
        public void Report(Exception exception)
        {
            FirebaseManager.MainThread();
            CrashSdk.LogException(exception ?? throw new ArgumentNullException(nameof(exception)));
        }
    }
}

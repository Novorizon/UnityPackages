using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RemoteSdk = global::Firebase.RemoteConfig.FirebaseRemoteConfig;

namespace Game.Framework.Firebase.Unity
{
    /// <summary>提供Firebase 远程配置的实现。</summary>
    public sealed class FirebaseRemoteConfiguration : IRemoteConfiguration
    {
        private readonly RemoteSdk sdk;
        private bool busy;

        /// <summary>使用调用方提供的runtime创建 FirebaseRemoteConfiguration。</summary>
        public FirebaseRemoteConfiguration(FirebaseManager runtime)
        {
            FirebaseManager.MainThread();
            sdk = RemoteSdk.GetInstance((runtime ?? throw new ArgumentNullException(nameof(runtime))).App);
        }

        /// <summary>异步设置 Defaults。</summary>
        public async Task SetDefaultsAsync(IReadOnlyDictionary<string, object> defaults)
        {
            FirebaseManager.MainThread();
            if (busy)
            {
                throw new InvalidOperationException("A Remote Config operation is already active.");
            }
            var copy = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in defaults ?? throw new ArgumentNullException(nameof(defaults)))
            {
                copy.Add(pair.Key, pair.Value);
            }
            busy = true;
            try
            {
                await sdk.SetDefaultsAsync(copy);
            }
            finally
            {
                busy = false;
            }
        }

        /// <summary>异步拉取 AndActivate。</summary>
        public async Task<bool> FetchAndActivateAsync(TimeSpan minimumCacheAge, CancellationToken token)
        {
            FirebaseManager.MainThread();
            token.ThrowIfCancellationRequested();
            if (minimumCacheAge < TimeSpan.Zero || busy)
            {
                throw new InvalidOperationException("Remote Config cache age must be nonnegative and no operation may overlap.");
            }
            busy = true;
            try
            {
                await sdk.FetchAsync(minimumCacheAge);
                token.ThrowIfCancellationRequested();
                // A cancelled fetch may populate SDK cache, but must not activate it as visible configuration.
                return await sdk.ActivateAsync();
            }
            finally
            {
                busy = false;
            }
        }

        /// <summary>获取字符串。</summary>
        public string GetString(string key) => Value(key).StringValue;
        /// <summary>获取整数64。</summary>
        public long GetInt64(string key) => Value(key).LongValue;
        /// <summary>获取双精度浮点数。</summary>
        public double GetDouble(string key) => Value(key).DoubleValue;
        /// <summary>获取布尔值。</summary>
        public bool GetBoolean(string key) => Value(key).BooleanValue;

        /// <summary>根据传入参数取得值。</summary>
        private global::Firebase.RemoteConfig.ConfigValue Value(string key)
        {
            FirebaseManager.MainThread();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Configuration key is required.", nameof(key));
            }
            return sdk.GetValue(key);
        }
    }
}

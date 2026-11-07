using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RemoteSdk = global::Firebase.RemoteConfig.FirebaseRemoteConfig;

namespace Game.Framework.Firebase.Unity
{
    public sealed class FirebaseRemoteConfiguration : IRemoteConfiguration
    {
        private readonly RemoteSdk sdk;
        private bool busy;

        public FirebaseRemoteConfiguration(FirebaseRuntime runtime)
        {
            FirebaseRuntime.MainThread();
            sdk = RemoteSdk.GetInstance((runtime ?? throw new ArgumentNullException(nameof(runtime))).App);
        }

        public async Task SetDefaultsAsync(IReadOnlyDictionary<string, object> defaults)
        {
            FirebaseRuntime.MainThread();
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

        public async Task<bool> FetchAndActivateAsync(TimeSpan minimumCacheAge, CancellationToken token)
        {
            FirebaseRuntime.MainThread();
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

        public string GetString(string key) => Value(key).StringValue;
        public long GetInt64(string key) => Value(key).LongValue;
        public double GetDouble(string key) => Value(key).DoubleValue;
        public bool GetBoolean(string key) => Value(key).BooleanValue;

        private global::Firebase.RemoteConfig.ConfigValue Value(string key)
        {
            FirebaseRuntime.MainThread();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Configuration key is required.", nameof(key));
            }
            return sdk.GetValue(key);
        }
    }
}

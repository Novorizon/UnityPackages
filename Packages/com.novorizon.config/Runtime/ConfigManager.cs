using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Framework.Config
{
    /// <summary>按记录类型与键类型管理表。整表校验成功后才替换旧表；应在游戏主线程使用。</summary>
    public sealed partial class ConfigManager : ConfigurableSingleton<ConfigManager>, IDisposable
    {
        [Serializable]
        private sealed class JsonTable<T>
        {
            public T[] items = null;
        }

        private readonly Dictionary<Type, object> tables = new Dictionary<Type, object>();
        private readonly Dictionary<Type, long> requests = new Dictionary<Type, long>();
        private Func<string, CancellationToken, Task<string>> loadText;
        private Func<string, CancellationToken, Task<byte[]>> loadBytes;
        private long revision;
        private bool disposed;
        public static ConfigManager Instance => GetOrCreateInstance(() => new ConfigManager());
        public int Count => tables.Count + generated.Count;
        public bool Initialized => loadText != null && !disposed;

        public static void Reset(bool stopping = false)
        {
            ResetInstance(value => value.Dispose(), stopping);
        }

        /// <summary>默认使用 Resources 文本；可传入资源系统的文本加载函数，不绑定任何配置生成工具。</summary>
        public void Initialize(Func<string, CancellationToken, Task<string>> textLoader = null, Func<string, CancellationToken, Task<byte[]>> bytesLoader = null)
        {
            EnsureAvailable();
            if (loadText != null)
            {
                if ((textLoader != null && loadText != textLoader) || (bytesLoader != null && loadBytes != bytesLoader))
                {
                    throw new InvalidOperationException("Configuration text loader is already initialized.");
                }

                return;
            }

            loadText = textLoader ?? LoadResourcesTextAsync;
            loadBytes = bytesLoader ?? LoadResourcesBytesAsync;
        }

        public void SetTable<TKey, T>(IEnumerable<T> items, Func<T, TKey> keySelector)
            where T : class
        {
            EnsureAvailable();
            var table = new ConfigTable<TKey, T>(items, keySelector);
            var type = typeof(ConfigTable<TKey, T>);
            requests[type] = ++revision;
            tables[type] = table;
        }

        public ConfigTable<TKey, T> GetTable<TKey, T>()
            where T : class
        {
            EnsureAvailable();
            return tables.TryGetValue(typeof(ConfigTable<TKey, T>), out var table) ? (ConfigTable<TKey, T>)table : throw new KeyNotFoundException("Configuration table is not loaded: " + typeof(T).FullName);
        }

        public T Get<TKey, T>(TKey key)
            where T : class => GetTable<TKey, T>()[key];
        public bool TryGet<TKey, T>(TKey key, out T value)
            where T : class
        {
            EnsureAvailable();
            value = null;
            return tables.TryGetValue(typeof(ConfigTable<TKey, T>), out var table) && ((ConfigTable<TKey, T>)table).TryGet(key, out value);
        }

        /// <summary>JSON 格式为 {"items":[...]}；重复键和空记录会拒绝整次加载。</summary>
        public void LoadJson<TKey, T>(string json, Func<T, TKey> keySelector)
            where T : class
        {
            SetTable(Parse<T>(json), keySelector);
        }

        public async Task LoadJsonAsync<TKey, T>(string location, Func<T, TKey> keySelector, CancellationToken token = default)
            where T : class
        {
            EnsureAvailable();
            if (loadText == null)
            {
                throw new InvalidOperationException("Initialize ConfigManager first.");
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Configuration location is required.", nameof(location));
            }

            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }

            token.ThrowIfCancellationRequested();
            var type = typeof(ConfigTable<TKey, T>);
            long request = ++revision;
            requests[type] = request;
            string json = await TaskUtil.WaitAsync(loadText(location, token), token);
            token.ThrowIfCancellationRequested();
            EnsureAvailable();
            var table = new ConfigTable<TKey, T>(Parse<T>(json), keySelector);
            if (!requests.TryGetValue(type, out var current) || current != request)
            {
                throw new OperationCanceledException("Configuration request was superseded or cleared.");
            }

            tables[type] = table;
        }

        private static T[] Parse<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Configuration JSON is empty.", nameof(json));
            }

            var table = JsonUtility.FromJson<JsonTable<T>>(json);
            return table?.items ?? throw new ArgumentException("Configuration JSON requires an items array.", nameof(json));
        }

        private static async Task<string> LoadResourcesTextAsync(string location, CancellationToken token)
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Resources configuration must load on Unity's main thread.");
            }

            var request = Resources.LoadAsync<TextAsset>(location);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!request.isDone)
            {
                request.completed += _ => completion.TrySetResult(true);
            }
            else
            {
                completion.TrySetResult(true);
            }

            await TaskUtil.WaitAsync(completion.Task, token);
            var asset = request.asset as TextAsset;
            if (asset == null)
            {
                throw new InvalidOperationException("Configuration text was not found: " + location);
            }

            return asset.text;
        }

        public void Clear()
        {
            EnsureAvailable();
            tables.Clear();
            requests.Clear();
            generated.Clear();
        }

        private void EnsureAvailable()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ConfigManager));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Clear();
            disposed = true;
            loadText = null;
            loadBytes = null;
            ReleaseInstance(this);
        }
    }
}

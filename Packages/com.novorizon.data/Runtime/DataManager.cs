using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Luban;
using UnityEngine;

namespace Game.Framework.Data
{
    [Serializable]
    public sealed class LubanManifest
    {
        public int version;
        public string format;
        public LubanFile[] files;
    }

    [Serializable]
    public sealed class LubanFile
    {
        public string name;
        public string location;
    }

    /// <summary>按 Luban Tables 构造函数加载完整快照；不需要预注册，不持有玩家动态数据。</summary>
    public sealed class DataManager : Singleton<DataManager>, IDisposable
    {
        private readonly Dictionary<Type, object> tables = new Dictionary<Type, object>();
        private readonly Dictionary<Type, long> requests = new Dictionary<Type, long>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly CancellationToken lifetimeToken;
        private Func<string, CancellationToken, Task<byte[]>> loadBytes;
        private long revision;
        private bool disposed;
        public int Count => tables.Count;
        public bool Initialized => loadBytes != null && !disposed;

        public DataManager()
        {
            lifetimeToken = lifetime.Token;
        }

        public static void Reset(bool stopping = false)
        {
            ResetInstance(value => value.Dispose(), stopping);
        }

        public override void Initialize()
        {
            Initialize(null);
        }

        /// <summary>清单和表文件共用一个字节加载器；默认从 Resources 读取 TextAsset。</summary>
        public void Initialize(Func<string, CancellationToken, Task<byte[]>> loader)
        {
            EnsureAvailable();
            if (loadBytes != null)
            {
                if (loader != null && loadBytes != loader)
                {
                    throw new InvalidOperationException("DataManager is already initialized with another loader.");
                }

                return;
            }

            loadBytes = loader ?? LoadResourcesBytesAsync;
        }

        /// <summary>直接传入生成的 Tables 构造函数；完整读取并校验成功后才替换同类型旧快照。</summary>
        public async Task<TTables> LoadAsync<TTables>(Func<Func<string, ByteBuf>, TTables> createTables, string manifestLocation = "Config/Luban/manifest", CancellationToken token = default)
            where TTables : class
        {
            EnsureAvailable();
            if (!Initialized)
            {
                throw new InvalidOperationException("Initialize DataManager first.");
            }

            if (createTables == null)
            {
                throw new ArgumentNullException(nameof(createTables));
            }

            if (string.IsNullOrWhiteSpace(manifestLocation))
            {
                throw new ArgumentException("A Luban manifest location is required.", nameof(manifestLocation));
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetimeToken);
            var active = linked.Token;
            active.ThrowIfCancellationRequested();
            long request = ++revision;
            requests[typeof(TTables)] = request;
            byte[] manifestBytes = await TaskUtil.WaitAsync(loadBytes(manifestLocation, active), active);
            CheckRequest(typeof(TTables), request, active);
            if (manifestBytes == null)
            {
                throw new InvalidOperationException("Luban manifest loader returned null: " + manifestLocation);
            }

            string text = Encoding.UTF8.GetString(manifestBytes).TrimStart('\uFEFF');
            var manifest = JsonUtility.FromJson<LubanManifest>(text);
            if (manifest == null || manifest.version != 1 || manifest.format != "bin" || manifest.files == null)
            {
                throw new ArgumentException("Unsupported Luban manifest: " + manifestLocation);
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in manifest.files)
            {
                if (file == null || string.IsNullOrWhiteSpace(file.name) || string.IsNullOrWhiteSpace(file.location) || !names.Add(file.name))
                {
                    throw new ArgumentException("Luban manifest contains an invalid or duplicate table.");
                }
            }

            var buffers = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var file in manifest.files)
            {
                CheckRequest(typeof(TTables), request, active);
                byte[] bytes = await TaskUtil.WaitAsync(loadBytes(file.location, active), active);
                if (bytes == null)
                {
                    throw new InvalidOperationException("Luban data loader returned null: " + file.location);
                }

                buffers.Add(file.name, bytes);
            }

            CheckRequest(typeof(TTables), request, active);
            var snapshot = createTables(name => buffers.TryGetValue(name, out var bytes)
                ? new ByteBuf(bytes)
                : throw new KeyNotFoundException("Luban table is missing from the manifest: " + name));
            CheckRequest(typeof(TTables), request, active);
            if (snapshot == null)
            {
                throw new InvalidOperationException("Luban tables factory returned null.");
            }

            tables[typeof(TTables)] = snapshot;
            return snapshot;
        }

        public TTables Get<TTables>()
            where TTables : class
        {
            if (!TryGet<TTables>(out var value))
            {
                throw new KeyNotFoundException("Luban tables have not been loaded: " + typeof(TTables).FullName);
            }

            return value;
        }

        public bool TryGet<TTables>(out TTables value)
            where TTables : class
        {
            EnsureAvailable();
            value = tables.TryGetValue(typeof(TTables), out var snapshot) ? (TTables)snapshot : null;
            return value != null;
        }

        /// <summary>使未完成的加载失效并清空已加载快照；再次加载时直接传入构造函数。</summary>
        public void Clear()
        {
            EnsureAvailable();
            ++revision;
            requests.Clear();
            tables.Clear();
        }

        private void CheckRequest(Type type, long request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            EnsureAvailable();
            if (!requests.TryGetValue(type, out var current) || current != request)
            {
                throw new OperationCanceledException("Luban load was superseded or cleared.");
            }
        }

        private void EnsureAvailable()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(DataManager));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            requests.Clear();
            tables.Clear();
            loadBytes = null;
            try
            {
                lifetime.Cancel();
            }
            finally
            {
                lifetime.Dispose();
                ReleaseInstance(this);
            }
        }

        private static async Task<byte[]> LoadResourcesBytesAsync(string location, CancellationToken token)
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Resources data must load on Unity's main thread.");
            }

            var request = Resources.LoadAsync<TextAsset>(location);
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (request.isDone)
            {
                ready.TrySetResult(true);
            }
            else
            {
                request.completed += _ => ready.TrySetResult(true);
            }

            await TaskUtil.WaitAsync(ready.Task, token);
            if (!(request.asset is TextAsset text))
            {
                throw new InvalidOperationException("Luban asset was not found: " + location);
            }

            return text.bytes;
        }
    }
}

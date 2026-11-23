using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Luban;
using UnityEngine;

namespace Game.Framework.Data
{
    /// <summary>提供Luban 清单的实现。</summary>
    [Serializable]
    public sealed class LubanManifest
    {
        public int version;
        public string format;
        public LubanFile[] files;
    }

    /// <summary>提供Luban 文件的实现。</summary>
    [Serializable]
    public sealed class LubanFile
    {
        public string name;
        public string location;
    }

    /// <summary>按 Luban Tables 构造函数加载完整快照；不需要预注册，不持有玩家动态数据。</summary>
    public static class DataManager
    {
#if UNITY_EDITOR
        static DataManager()
        {
            EditorStaticReset.Register(Reset);
        }
#endif

        private static readonly Dictionary<Type, object> tables = new Dictionary<Type, object>();
        private static readonly Dictionary<Type, long> requests = new Dictionary<Type, long>();
        private static CancellationTokenSource lifetime;
        private static Func<string, CancellationToken, Task<byte[]>> loadBytes;
        private static long revision;

        public static int Count => tables.Count;
        public static bool Initialized => loadBytes != null;

        /// <summary>清单和表文件共用一个字节加载器；默认从 Resources 读取 TextAsset。</summary>
        public static void Initialize(Func<string, CancellationToken, Task<byte[]>> loader = null)
        {
            if (loadBytes != null)
            {
                if (loader != null && loadBytes != loader)
                {
                    throw new InvalidOperationException("DataManager is already initialized with another loader.");
                }

                return;
            }

            lifetime = new CancellationTokenSource();
            loadBytes = loader ?? LoadResourcesBytesAsync;
        }

        /// <summary>直接传入生成的 Tables 构造函数；完整读取并校验成功后才替换同类型旧快照。</summary>
        /// <remarks>加载清单及 Luban 表数据，全部成功后才发布新的表快照。</remarks>
        public static async Task<TTables> LoadAsync<TTables>(Func<Func<string, ByteBuf>, TTables> createTables, string manifestLocation = "Config/Luban/manifest", CancellationToken token = default)
            where TTables : class
        {
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

            // 保存本轮加载器和令牌，Reset 之后旧请求不能借用新一轮的配置。
            var loader = loadBytes;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var active = linked.Token;
            active.ThrowIfCancellationRequested();
            long request = ++revision;
            requests[typeof(TTables)] = request;
            byte[] manifestBytes = await TaskUtil.WaitAsync(loader(manifestLocation, active), active);
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
                byte[] bytes = await TaskUtil.WaitAsync(loader(file.location, active), active);
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

        /// <summary>读取该类型最新发布的配置表，未加载时抛出异常。</summary>
        public static TTables Get<TTables>()
            where TTables : class
        {
            if (!TryGet<TTables>(out var value))
            {
                throw new KeyNotFoundException("Luban tables have not been loaded: " + typeof(TTables).FullName);
            }

            return value;
        }

        /// <summary>读取该表类型的当前快照，尚未加载时返回 false。</summary>
        public static bool TryGet<TTables>(out TTables value)
            where TTables : class
        {
            value = tables.TryGetValue(typeof(TTables), out var snapshot) ? (TTables)snapshot : null;
            return value != null;
        }

        /// <summary>使未完成的加载失效并清空已加载快照；再次加载时直接传入构造函数。</summary>
        public static void Clear()
        {
            ++revision;
            requests.Clear();
            tables.Clear();
        }

        // 用请求序号阻止过期的异步加载覆盖后发请求的结果。
        private static void CheckRequest(Type type, long request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!requests.TryGetValue(type, out var current) || current != request)
            {
                throw new OperationCanceledException("Luban load was superseded or cleared.");
            }
        }

        /// <summary>取消旧请求并清空配置表。下一次 Initialize 会创建新的生命周期令牌。</summary>
        public static void Reset()
        {
            var previous = lifetime;
            lifetime = null;
            loadBytes = null;
            Clear();
            if (previous == null)
            {
                return;
            }

            try
            {
                previous.Cancel();
            }
            finally
            {
                previous.Dispose();
            }
        }

        /// <summary>异步加载资源字节数据。</summary>
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

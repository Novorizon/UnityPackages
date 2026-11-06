using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Luban;
using UnityEngine;

namespace Game.Framework.Config
{
    [Serializable]
    public sealed class LubanConfigManifest
    {
        public int version;
        public string format;
        public LubanConfigFile[] files;
    }

    [Serializable]
    public sealed class LubanConfigFile
    {
        public string name;
        public string location;
    }

    public sealed partial class ConfigManager
    {
        private sealed class GeneratedSlot<T>
        {
        }

        private readonly Dictionary<Type, object> generated = new Dictionary<Type, object>();
        /// <summary>预取导出清单中的数据，再用 Luban 生成的 Tables 构造函数建立完整快照。</summary>
        public async Task<TTables> LoadLubanAsync<TTables>(Func<Func<string, ByteBuf>, TTables> createTables, string manifestLocation = "Config/Luban/manifest", CancellationToken token = default)
            where TTables : class
        {
            EnsureAvailable();
            if (!Initialized)
            {
                throw new InvalidOperationException("Initialize ConfigManager first.");
            }

            if (createTables == null)
            {
                throw new ArgumentNullException(nameof(createTables));
            }

            if (string.IsNullOrWhiteSpace(manifestLocation))
            {
                throw new ArgumentException("A Luban manifest location is required.", nameof(manifestLocation));
            }

            token.ThrowIfCancellationRequested();
            var slot = typeof(GeneratedSlot<TTables>);
            long request = ++revision;
            requests[slot] = request;
            string text = await TaskUtil.WaitAsync(loadText(manifestLocation, token), token);
            token.ThrowIfCancellationRequested();
            var manifest = JsonUtility.FromJson<LubanConfigManifest>(text);
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
                CheckGeneratedRequest(slot, request, token);
                byte[] bytes = await TaskUtil.WaitAsync(loadBytes(file.location, token), token);
                if (bytes == null)
                {
                    throw new InvalidOperationException("Luban data loader returned null: " + file.location);
                }

                buffers.Add(file.name, bytes);
            }

            CheckGeneratedRequest(slot, request, token);
            var tables = createTables(name => buffers.TryGetValue(name, out var bytes) ? new ByteBuf(bytes) : throw new KeyNotFoundException("Luban table is missing from the export manifest: " + name));
            CheckGeneratedRequest(slot, request, token);
            if (tables == null)
            {
                throw new InvalidOperationException("Luban tables factory returned null.");
            }

            generated[slot] = tables;
            return tables;
        }

        public TTables GetLuban<TTables>()
            where TTables : class
        {
            EnsureAvailable();
            return generated.TryGetValue(typeof(GeneratedSlot<TTables>), out var tables) ? (TTables)tables : throw new KeyNotFoundException("Luban tables have not been loaded: " + typeof(TTables).FullName);
        }

        private void CheckGeneratedRequest(Type slot, long request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            EnsureAvailable();
            if (!requests.TryGetValue(slot, out var current) || current != request)
            {
                throw new OperationCanceledException("Luban load was superseded or cleared.");
            }
        }

        private static async Task<byte[]> LoadResourcesBytesAsync(string location, CancellationToken token)
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Resources configuration must load on Unity's main thread.");
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
                throw new InvalidOperationException("Luban binary asset was not found: " + location);
            }

            return text.bytes;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using YooAsset;
using Logger = Game.Framework.Logger;
using Object = UnityEngine.Object;

namespace Game.Resource
{
    public partial class ResourceManager
    {
        private enum AssetLifetime
        {
            Asset,
            SpriteClone,
            PrefabInstance,
            AtlasBinding
        }

        private sealed class AssetReferenceComparer : IEqualityComparer<Object>
        {
            internal static readonly AssetReferenceComparer Instance = new AssetReferenceComparer();
            public bool Equals(Object left, Object right) => ReferenceEquals(left, right);
            public int GetHashCode(Object asset) => RuntimeHelpers.GetHashCode(asset);
        }

        private sealed class AssetReferences
        {
            internal readonly Stack<AssetHandle> Handles = new Stack<AssetHandle>();
            internal bool DestroyOnRelease;
        }

        private sealed class PendingAssetLoad
        {
            internal AssetHandle Handle;
            internal readonly TaskCompletionSource<bool> Cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal void Release()
            {
                var handle = Handle;
                Handle = null;
                ReleaseAssetHandle(handle);
            }
        }

        private readonly Dictionary<Object, AssetReferences> loadedAssets = new Dictionary<Object, AssetReferences>(AssetReferenceComparer.Instance);
        private readonly HashSet<PendingAssetLoad> pendingAssets = new HashSet<PendingAssetLoad>();
        private readonly List<AssetHandle> atlasBindings = new List<AssetHandle>();
        private int assetGeneration;

        /// <summary>每次成功调用持有一个独立引用。使用结束后调用 Release，即使两次取得的是同一个对象。</summary>
        public Task<T> LoadAssetAsync<T>(string location) where T : Object =>
            LoadAssetAsync<T>(location, CancellationToken.None);

        /// <summary>异步取得一个独立引用。加载失败或取消返回 null。</summary>
        public Task<T> LoadAssetAsync<T>(string location, CancellationToken token) where T : Object =>
            LoadAssetAsync<T, T>(location, asset => asset, AssetLifetime.Asset, token);

        /// <summary>非空回调接收一次加载结果。成功时由回调的调用方归还该引用。</summary>
        public Task LoadAssetAsync<T>(string location, Action<T> callback) where T : Object =>
            LoadAssetAsync(location, callback, CancellationToken.None);

        /// <summary>非空回调接收一次加载结果，取消或失败时结果为 null。</summary>
        public async Task LoadAssetAsync<T>(string location, Action<T> callback, CancellationToken token) where T : Object
        {
            if (callback != null)
            {
                callback(await LoadAssetAsync<T>(location, token));
            }
        }

        /// <summary>同步取得一个独立引用。每次成功加载都要配对 Release。</summary>
        public T LoadAsset<T>(string location) where T : Object =>
            LoadAsset<T, T>(location, asset => asset, AssetLifetime.Asset);

        /// <summary>非空回调接收同步加载结果，并接管成功的资源引用。</summary>
        public void LoadAsset<T>(string location, Action<T> callback) where T : Object
        {
            if (callback != null)
            {
                callback(LoadAsset<T>(location));
            }
        }

        /// <summary>归还此对象的一次加载引用。没有剩余记录时返回 false。</summary>
        public bool Release(Object asset)
        {
            MainThread();
            // 实例可能已被 Destroy。用托管对象引用匹配最初返回的实例。
            if (ReferenceEquals(asset, null) || !loadedAssets.TryGetValue(asset, out var references))
            {
                return false;
            }

            var handle = references.Handles.Pop();
            if (references.Handles.Count == 0)
            {
                loadedAssets.Remove(asset);
                if (references.DestroyOnRelease && asset != null)
                {
                    Object.Destroy(asset);
                }
            }
            ReleaseAssetHandle(handle);
            return true;
        }

        /// <summary>应用级操作，撤销未完成的普通加载并清理所有普通资源和自动绑定的图集。</summary>
        public void ReleaseAllAssets()
        {
            MainThread();
            ReleaseAssetsCore();
        }

        private async Task<TResult> LoadAssetAsync<TSource, TResult>(
            string location,
            Func<TSource, TResult> select,
            AssetLifetime lifetime,
            CancellationToken token)
            where TSource : Object
            where TResult : Object
        {
            if (token.IsCancellationRequested || !CanLoadAsset(location))
            {
                return null;
            }

            var pending = new PendingAssetLoad();
            pendingAssets.Add(pending);
            try
            {
                // YooAsset 每次生成一个 Handle，内部自行共享 Provider 和 AssetBundle。
                var handle = package.LoadAssetAsync<TSource>(location);
                pending.Handle = handle;
                Task loading = handle?.Task;
                if (loading == null)
                {
                    LogAssetFailure(location, handle);
                    return null;
                }

                if (!loading.IsCompleted)
                {
                    using (token.Register(() => pending.Cancelled.TrySetResult(true)))
                    {
                        if (await Task.WhenAny(loading, pending.Cancelled.Task) != loading)
                        {
                            loading.Forget();
                            return null;
                        }
                    }
                }
                await loading;

                if (pending.Cancelled.Task.IsCompleted || token.IsCancellationRequested || shuttingDown)
                {
                    return null;
                }

                var result = ReadAsset(handle, location, select);
                if (result == null)
                {
                    return null;
                }
                if (pending.Cancelled.Task.IsCompleted || token.IsCancellationRequested || shuttingDown)
                {
                    DestroyUnpublishedAsset(result, lifetime);
                    return null;
                }

                TrackAsset(result, handle, lifetime);
                pending.Handle = null;
                return result;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception error) when (IsResourceIoFailure(error))
            {
                Logger.Warn("Could not load resource " + location + ": " + error.Message);
                return null;
            }
            finally
            {
                pendingAssets.Remove(pending);
                pending.Release();
            }
        }

        private TResult LoadAsset<TSource, TResult>(string location, Func<TSource, TResult> select, AssetLifetime lifetime)
            where TSource : Object
            where TResult : Object
        {
            if (!CanLoadAsset(location))
            {
                return null;
            }

            int startedGeneration = assetGeneration;
            AssetHandle handle = null;
            try
            {
                handle = package.LoadAssetSync<TSource>(location);
                if (startedGeneration != assetGeneration || shuttingDown)
                {
                    return null;
                }
                var result = ReadAsset(handle, location, select);
                if (result == null)
                {
                    return null;
                }
                if (startedGeneration != assetGeneration || shuttingDown)
                {
                    DestroyUnpublishedAsset(result, lifetime);
                    return null;
                }
                TrackAsset(result, handle, lifetime);
                handle = null;
                return result;
            }
            catch (Exception error) when (IsResourceIoFailure(error))
            {
                Logger.Warn("Could not load resource " + location + ": " + error.Message);
                return null;
            }
            finally
            {
                ReleaseAssetHandle(handle);
            }
        }

        private bool CanLoadAsset(string location)
        {
            MainThread();
            if (!IsReady || package == null || string.IsNullOrWhiteSpace(location))
            {
                Logger.Warn("Resource package is unavailable or the location is empty: " + location);
                return false;
            }
            return true;
        }

        private static TResult ReadAsset<TSource, TResult>(AssetHandle handle, string location, Func<TSource, TResult> select)
            where TSource : Object
            where TResult : Object
        {
            if (handle == null || !handle.IsValid || handle.Status != EOperationStatus.Succeed)
            {
                LogAssetFailure(location, handle);
                return null;
            }
            var source = handle.AssetObject as TSource;
            var result = source == null ? null : select(source);
            if (result == null)
            {
                Logger.Warn("Resource " + location + " did not provide " + typeof(TResult).Name + ".");
            }
            return result;
        }

        private static void LogAssetFailure(string location, AssetHandle handle)
        {
            string detail = handle != null && handle.IsValid ? handle.LastError : "Invalid YooAsset handle.";
            Logger.Warn("Could not load resource " + location + ": " + detail);
        }

        private void TrackAsset(Object asset, AssetHandle handle, AssetLifetime lifetime)
        {
            if (lifetime == AssetLifetime.AtlasBinding)
            {
                // Unity 的自动绑定回调没有归还通知，图集随资源管理器统一回收。
                atlasBindings.Add(handle);
                return;
            }
            if (!loadedAssets.TryGetValue(asset, out var references))
            {
                references = new AssetReferences();
                loadedAssets.Add(asset, references);
            }
            references.DestroyOnRelease |= lifetime == AssetLifetime.SpriteClone;
            references.Handles.Push(handle);
        }

        private void ReleaseAssetsCore()
        {
            assetGeneration++;
            var pending = new List<PendingAssetLoad>(pendingAssets);
            var loaded = new List<KeyValuePair<Object, AssetReferences>>(loadedAssets);
            var bindings = new List<AssetHandle>(atlasBindings);
            pendingAssets.Clear();
            loadedAssets.Clear();
            atlasBindings.Clear();

            foreach (var load in pending)
            {
                load.Cancelled.TrySetResult(true);
                load.Release();
            }
            foreach (var pair in loaded)
            {
                if (pair.Value.DestroyOnRelease && pair.Key != null)
                {
                    Object.Destroy(pair.Key);
                }
                foreach (var handle in pair.Value.Handles)
                {
                    ReleaseAssetHandle(handle);
                }
            }
            foreach (var handle in bindings)
            {
                ReleaseAssetHandle(handle);
            }
        }

        private static void ReleaseAssetHandle(AssetHandle handle)
        {
            if (handle != null && handle.IsValid)
            {
                handle.Release();
            }
        }

        private static void DestroyUnpublishedAsset(Object asset, AssetLifetime lifetime)
        {
            if (lifetime == AssetLifetime.SpriteClone || lifetime == AssetLifetime.PrefabInstance)
            {
                Object.Destroy(asset);
            }
        }
    }
}

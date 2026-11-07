using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using Logger = Game.Framework.Logger;

namespace Game.Resource
{
    public partial class ResourceManager
    {
        private sealed class OwnedAsset
        {
            internal long DiagnosticId;
            internal string Location;
            internal string AssetType;
            internal string Owner;
            internal long ScopeId;
            internal int SceneHandle;
            internal string SceneName;
            internal string CreationStack;
            internal AssetHandle Handle;
            internal Task Loading;
            internal readonly CancellationTokenSource Lifetime = new CancellationTokenSource();
            internal Action InvalidateLease;
            internal Task ReleaseTask;
            internal bool Invalidated;
            internal bool KeepAliveUntilDisposed;
            internal readonly TaskCompletionSource<bool> OwnerReleased = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly HashSet<OwnedAsset> ownedAssets = new HashSet<OwnedAsset>();
        private readonly string diagnosticSessionId = Guid.NewGuid().ToString("N");
        private long nextLeaseId, nextScopeId;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool captureLeaseCreationStacks;
#endif

        /// <summary>Opt-in for future acquisitions only. Shipping non-development players never capture stacks.</summary>
        public bool CaptureLeaseCreationStacks
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return captureLeaseCreationStacks;
#else
                return false;
#endif
            }
            set
            {
                MainThread();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                captureLeaseCreationStacks = value;
#endif
            }
        }

        /// <summary>Allocates a read-only scalar snapshot only on request. Includes cancelled I/O awaiting native release.</summary>
        public ResourceLeaseDiagnostic[] CaptureLeaseDiagnostics()
        {
            MainThread();
            var snapshots = new ResourceLeaseDiagnostic[ownedAssets.Count];
            int index = 0;
            foreach (var owned in ownedAssets)
            {
                string state = owned.Invalidated ? "Releasing" : owned.Loading.IsCompleted &&
                    owned.Handle.Status == EOperationStatus.Succeed ? "Active" : "Loading";
                snapshots[index++] = new ResourceLeaseDiagnostic(
                    diagnosticSessionId,
                    owned.DiagnosticId,
                    owned.Owner,
                    owned.ScopeId,
                    owned.SceneHandle,
                    owned.SceneName,
                    owned.Location,
                    owned.AssetType,
                    state,
                    owned.CreationStack);
            }
            Array.Sort(snapshots, (left, right) => left.LeaseId.CompareTo(right.LeaseId));
            return snapshots;
        }

        internal long NextResourceScopeId()
        {
            MainThread();
            return ++nextScopeId;
        }

        private string CaptureCreationStack()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (captureLeaseCreationStacks)
            {
                try
                {
                    string stack = new System.Diagnostics.StackTrace(2, true).ToString();
                    return stack.Length <= 8192 ? stack : stack.Substring(0, 8192);
                }
                catch (Exception)
                {
                    return "Creation stack unavailable";
                }
            }
#endif
            return null;
        }
        /// <summary>包含尚未结束的原生加载和等待释放的句柄。</summary>
        public int OwnedAssetCount => ownedAssets.Count;

        public Task<AssetLease<T>> AcquireAsync<T>(string location, CancellationToken token = default, bool keepAliveUntilDisposed = false)
            where T : UnityEngine.Object
        {
            return AcquireOwnedAsync<T>(location, token, keepAliveUntilDisposed, null, 0, 0, null);
        }

        public Task<AssetLease<T>> AcquireOwnedAsync<T>(
            string location,
            string owner,
            CancellationToken token = default,
            bool keepAliveUntilDisposed = false)
            where T : UnityEngine.Object
        {
            return AcquireOwnedAsync<T>(location, token, keepAliveUntilDisposed, owner, 0, 0, null);
        }

        internal async Task<AssetLease<T>> AcquireOwnedAsync<T>(
            string location,
            CancellationToken token,
            bool keepAliveUntilDisposed,
            string owner,
            long scopeId,
            int sceneHandle,
            string sceneName)
            where T : UnityEngine.Object
        {
            Ready();
            Validate(location);
            token.ThrowIfCancellationRequested();
            var handle = package.LoadAssetAsync<T>(location);
            var owned = new OwnedAsset
            {
                DiagnosticId = ++nextLeaseId,
                Location = location,
                AssetType = typeof(T).FullName,
                Owner = string.IsNullOrEmpty(owner) ? "Unassigned" : owner,
                ScopeId = scopeId,
                SceneHandle = sceneHandle,
                SceneName = sceneName,
                CreationStack = CaptureCreationStack(),
                Handle = handle,
                Loading = handle.Task,
                KeepAliveUntilDisposed = keepAliveUntilDisposed
            };
            ownedAssets.Add(owned);
            try
            {
                using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, owned.Lifetime.Token))
                {
                    await TaskUtil.WaitAsync(owned.Loading, cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (owned.Invalidated)
                    {
                        throw new OperationCanceledException("Resource owner stopped.");
                    }

                    if (handle.Status != EOperationStatus.Succeed || handle.AssetObject == null)
                    {
                        throw new InvalidOperationException(handle.LastError);
                    }

                    var lease = new AssetLease<T>(Cast<T>(handle.AssetObject, location), () => ReleaseOwned(owned));
                    owned.InvalidateLease = lease.Invalidate;
                    return lease;
                }
            }
            catch
            {
                ReleaseOwned(owned);
                throw;
            }
        }

        private void ReleaseOwned(OwnedAsset owned)
        {
            MainThread();
            if (owned.ReleaseTask != null)
            {
                return;
            }

            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            owned.ReleaseTask = completion.Task;
            owned.OwnerReleased.TrySetResult(true);
            _ = owned.ReleaseTask.ContinueWith(task =>
            {
                _ = task.Exception;
            }, TaskContinuationOptions.OnlyOnFaulted);
            owned.Invalidated = true;
            owned.InvalidateLease?.Invoke();
            owned.InvalidateLease = null;
            owned.Lifetime.Cancel();
            CompleteOwnedReleaseAsync(owned, completion).Forget();
        }

        private async Task CompleteOwnedReleaseAsync(OwnedAsset owned, TaskCompletionSource<bool> completion)
        {
            try
            {
                // 调用方取消等待后，原生 I/O 仍可能进行；不能提前让其句柄失效。
                try
                {
                    await owned.Loading;
                }
                catch
                {
                // 原加载异常由调用方获得，此处仍然释放句柄。
                }

                if (owned.Handle.IsValid)
                {
                    owned.Handle.Release();
                }

                completion.TrySetResult(true);
            }
            catch (Exception error)
            {
                completion.TrySetException(error);
                Logger.Exception(error);
            }
            finally
            {
                ownedAssets.Remove(owned);
                owned.Lifetime.Dispose();
            }
        }

        private async Task WaitForOwnedReleaseAsync(OwnedAsset owned, bool respectOwner)
        {
            // 原生实例仍在使用资源时，由持有者在实例销毁后归还租约。
            if (respectOwner && owned.KeepAliveUntilDisposed && owned.InvalidateLease != null && owned.ReleaseTask == null)
            {
                await owned.OwnerReleased.Task;
            }
            else
            {
                ReleaseOwned(owned);
            }

            await owned.ReleaseTask;
        }

        private Task ReleaseOwnedAssetsAsync(bool respectOwner = false)
        {
            var releases = new List<Task>();
            foreach (var owned in new List<OwnedAsset>(ownedAssets))
            {
                releases.Add(WaitForOwnedReleaseAsync(owned, respectOwner));
            }

            return Task.WhenAll(releases);
        }

        public ResourceScope CreateScope()
        {
            return CreateScope(null);
        }

        public ResourceScope CreateScope(string owner)
        {
            MainThread();
            return new ResourceScope(this, null, owner);
        }

        /// <summary>场景卸载时取消未完成获取并释放此作用域的租约，不影响其他场景的租约。</summary>
        public ResourceScope CreateSceneScope(Scene scene)
        {
            return CreateSceneScope(scene, null);
        }

        public ResourceScope CreateSceneScope(Scene scene, string owner)
        {
            MainThread();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException("A loaded scene is required.", nameof(scene));
            }

            return new ResourceScope(this, scene, owner);
        }
    }
}

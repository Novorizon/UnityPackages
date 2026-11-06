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
        /// <summary>包含尚未结束的原生加载和等待释放的句柄。</summary>
        public int OwnedAssetCount => ownedAssets.Count;

        public async Task<AssetLease<T>> AcquireAsync<T>(string location, CancellationToken token = default, bool keepAliveUntilDisposed = false)
            where T : UnityEngine.Object
        {
            Ready();
            Validate(location);
            token.ThrowIfCancellationRequested();
            var handle = package.LoadAssetAsync<T>(location);
            var owned = new OwnedAsset
            {
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
            MainThread();
            return new ResourceScope(this);
        }

        /// <summary>场景卸载时取消未完成获取并释放此作用域的租约，不影响其他场景的租约。</summary>
        public ResourceScope CreateSceneScope(Scene scene)
        {
            MainThread();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException("A loaded scene is required.", nameof(scene));
            }

            return new ResourceScope(this, scene);
        }
    }
}

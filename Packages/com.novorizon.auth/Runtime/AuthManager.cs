using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Auth
{
    /// <summary>Explicitly owned main-thread facade. Busy persists until the underlying SDK task ends.</summary>
    /// <remarks>把第三方身份凭据交给认证后端，统一处理登录、绑定和会话令牌。</remarks>
    public sealed class AuthManager : IDisposable
    {
        private readonly IAuthBackend backend;
        private readonly Dictionary<IdentityProvider, IIdentityProvider> providers = new Dictionary<IdentityProvider, IIdentityProvider>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly bool ownsDependencies;
        private bool busy, disposed;
        public bool IsBusy => busy;
        public AuthUser CurrentUser
        {
            get
            {
                MainThread();
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(AuthManager));
                }
                return backend.CurrentUser;
            }
        }

        /// <summary>注入会话后端和各厂商身份适配器。ownsDependencies 为 true 时由管理器释放这些依赖。</summary>
        public AuthManager(IAuthBackend backend, IEnumerable<IIdentityProvider> providers, bool ownsDependencies = false)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
            this.ownsDependencies = ownsDependencies;
            foreach (var provider in providers ?? throw new ArgumentNullException(nameof(providers)))
            {
                if (provider == null || this.providers.ContainsKey(provider.Provider))
                {
                    throw new ArgumentException("Provider instances must be non-null and unique.", nameof(providers));
                }
                this.providers.Add(provider.Provider, provider);
            }
        }

        /// <summary>使用后端的匿名登录能力创建或进入匿名会话。</summary>
        public Task<AuthUser> SignInAnonymouslyAsync(CancellationToken cancellationToken = default)
        {
            return Run(backend.SignInAnonymouslyAsync, cancellationToken);
        }

        /// <summary>向指定第三方获取凭据，再交给认证后端建立会话。</summary>
        public Task<AuthUser> SignInAsync(IdentityProvider provider, CancellationToken cancellationToken = default)
        {
            return Run(token => Authenticate(provider, null, token), cancellationToken);
        }

        /// <summary>将第三方账号绑定到当前用户。等待授权期间若发生切号，绑定会失败。</summary>
        public Task<AuthUser> LinkAsync(IdentityProvider provider, CancellationToken cancellationToken = default)
        {
            Ready();
            string user = backend.CurrentUser?.Id ?? throw new InvalidOperationException("Sign in before linking an identity.");
            return Run(token => Authenticate(provider, user, token), cancellationToken);
        }

        /// <summary>解除当前用户与指定厂商的绑定，普通账号必须保留至少一种登录方式。</summary>
        public Task<AuthUser> UnlinkAsync(string providerId, CancellationToken cancellationToken = default)
        {
            Ready();
            if (string.IsNullOrWhiteSpace(providerId))
            {
                throw new ArgumentException("Provider ID is required.", nameof(providerId));
            }
            var user = backend.CurrentUser ?? throw new InvalidOperationException("Sign in before unlinking.");
            if (!user.IsAnonymous && user.LinkedProviders.Count <= 1)
            {
                throw new InvalidOperationException("Link another sign-in method before removing the last one.");
            }
            return Run(token => backend.UnlinkAsync(user.Id, providerId, token), cancellationToken);
        }

        /// <summary>获取当前后端会话令牌；forceRefresh 为 true 时要求后端刷新。</summary>
        public Task<string> GetSessionTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            return Run(token => backend.GetSessionTokenAsync(forceRefresh, token), cancellationToken);
        }

        /// <summary>退出当前后端会话。调用前必须处于空闲且未销毁状态。</summary>
        public void SignOut()
        {
            Ready();
            backend.SignOut();
        }

        // 获取并校验一次第三方凭据，再按有无目标用户选择绑定或登录。
        private async Task<AuthUser> Authenticate(IdentityProvider provider, string expectedUser, CancellationToken token)
        {
            if (!providers.TryGetValue(provider, out var source))
            {
                throw new NotSupportedException("No identity adapter was configured for " + provider + ".");
            }
            using (var proof = await source.AcquireAsync(token))
            {
                token.ThrowIfCancellationRequested();
                if (proof == null || proof.Provider != provider)
                {
                    throw new InvalidOperationException("The identity adapter returned the wrong proof.");
                }
                if (expectedUser != null)
                {
                    // 第三方授权可能弹出原生界面，回来后要再次确认仍是发起绑定时的账号。
                    if (backend.CurrentUser?.Id != expectedUser)
                    {
                        throw new InvalidOperationException("The current account changed before linking.");
                    }
                    return await backend.LinkAsync(expectedUser, proof, token);
                }
                return await backend.SignInAsync(proof, token);
            }
        }

        // 串行启动认证操作，并允许调用方取消等待。
        private Task<T> Run<T>(Func<CancellationToken, Task<T>> action, CancellationToken caller)
        {
            Ready();
            caller.ThrowIfCancellationRequested();
            busy = true;
            var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, caller);
            Task<T> operation = Execute(action, stop);
            // Cancelling a waiter never starts a second operation over an unfinished native popup.
            // 调用方取消等待后，忙碌状态仍由底层操作负责清除，原生授权窗口不会重叠。
            return TaskUtil.WaitAsync(operation, caller);
        }

        // 认证操作实际结束时清掉忙碌状态，按需释放依赖。
        private async Task<T> Execute<T>(Func<CancellationToken, Task<T>> action, CancellationTokenSource stop)
        {
            try
            {
                return await action(stop.Token);
            }
            finally
            {
                stop.Dispose();
                busy = false;
                if (disposed)
                {
                    ReleaseDependencies();
                }
            }
        }

        // 检查主线程、生命周期与忙碌状态，防止原生授权窗口重叠。
        private void Ready()
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AuthManager));
            }
            if (busy)
            {
                throw new InvalidOperationException("An authentication operation is already active.");
            }
        }

        // 登录和绑定入口必须在已初始化的 Unity 主线程使用。
        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use platform services on the initialized Unity main thread.");
            }
        }

        /// <summary>取消当前认证并结束管理器生命周期，等待底层退出后再释放自有依赖。</summary>
        public void Dispose()
        {
            MainThread();
            if (disposed)
            {
                return;
            }
            disposed = true;
            try
            {
                lifetime.Cancel();
            }
            finally
            {
                if (!busy)
                {
                    ReleaseDependencies();
                }
            }
        }

        private bool released;
        // 释放管理器持有的取消令牌，并按所有权释放身份适配器和认证后端。
        private void ReleaseDependencies()
        {
            if (released)
            {
                return;
            }
            released = true;
            lifetime.Dispose();
            if (!ownsDependencies)
            {
                return;
            }
            var errors = new List<Exception>();
            foreach (var provider in providers.Values)
            {
                try
                {
                    provider.Dispose();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
            try
            {
                backend.Dispose();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            if (errors.Count != 0)
            {
                throw new AggregateException("Authentication dependency cleanup failed.", errors);
            }
        }
    }
}

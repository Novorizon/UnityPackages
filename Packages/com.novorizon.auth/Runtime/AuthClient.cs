using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Auth
{
    /// <summary>Explicitly owned main-thread facade. Busy persists until the underlying SDK task ends.</summary>
    public class AuthClient : IDisposable
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
                    throw new ObjectDisposedException(nameof(AuthClient));
                }
                return backend.CurrentUser;
            }
        }

        public AuthClient(IAuthBackend backend, IEnumerable<IIdentityProvider> providers, bool ownsDependencies = false)
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

        public Task<AuthUser> SignInAnonymouslyAsync(CancellationToken cancellationToken = default)
        {
            return Run(backend.SignInAnonymouslyAsync, cancellationToken);
        }

        public Task<AuthUser> SignInAsync(IdentityProvider provider, CancellationToken cancellationToken = default)
        {
            return Run(token => Authenticate(provider, null, token), cancellationToken);
        }

        public Task<AuthUser> LinkAsync(IdentityProvider provider, CancellationToken cancellationToken = default)
        {
            Ready();
            string user = backend.CurrentUser?.Id ?? throw new InvalidOperationException("Sign in before linking an identity.");
            return Run(token => Authenticate(provider, user, token), cancellationToken);
        }

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

        public Task<string> GetSessionTokenAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            return Run(token => backend.GetSessionTokenAsync(forceRefresh, token), cancellationToken);
        }

        public void SignOut()
        {
            Ready();
            backend.SignOut();
        }

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
                    if (backend.CurrentUser?.Id != expectedUser)
                    {
                        throw new InvalidOperationException("The current account changed before linking.");
                    }
                    return await backend.LinkAsync(expectedUser, proof, token);
                }
                return await backend.SignInAsync(proof, token);
            }
        }

        private Task<T> Run<T>(Func<CancellationToken, Task<T>> action, CancellationToken caller)
        {
            Ready();
            caller.ThrowIfCancellationRequested();
            busy = true;
            var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, caller);
            Task<T> operation = Execute(action, stop);
            // Cancelling a waiter never starts a second operation over an unfinished native popup.
            return TaskUtil.WaitAsync(operation, caller);
        }

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

        private void Ready()
        {
            MainThread();
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AuthClient));
            }
            if (busy)
            {
                throw new InvalidOperationException("An authentication operation is already active.");
            }
        }

        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use platform services on the initialized Unity main thread.");
            }
        }

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

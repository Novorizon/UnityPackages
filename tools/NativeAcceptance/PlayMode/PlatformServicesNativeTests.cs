using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Auth;
using Game.Framework.Auth.Native;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class PlatformServicesNativeTests
{
    [UnityTest]
    public IEnumerator NativeIdentityJsonRejectsStaleAndDuplicateCallbacks()
    {
        var root = new GameObject("Identity callback acceptance");
        var host = root.AddComponent<IdentityCallbackHost>();
        var first = host.Begin("first");
        root.SendMessage("OnNativeIdentityResult", "{\"requestId\":\"stale\",\"token\":\"old\"}");
        Assert.IsFalse(first.IsCompleted);
        root.SendMessage("OnNativeIdentityResult", "{\"requestId\":\"first\",\"token\":\"accepted\",\"cancelled\":false}");
        yield return new WaitUntil(() => first.IsCompleted);
        Assert.AreEqual("accepted", first.Result.token);
        var second = host.Begin("second");
        root.SendMessage("OnNativeIdentityResult", "{\"requestId\":\"first\",\"token\":\"duplicate\"}");
        Assert.IsFalse(second.IsCompleted);
        root.SendMessage("OnNativeIdentityResult", "{\"requestId\":\"second\",\"cancelled\":true}");
        yield return new WaitUntil(() => second.IsCompleted);
        Assert.IsTrue(second.Result.cancelled);
        Object.Destroy(root);
        yield return null;
    }

    [UnityTest]
    public IEnumerator DestroyedIdentityReceiverRetiresItsPendingOperation()
    {
        var root = new GameObject("Identity destruction acceptance");
        var host = root.AddComponent<IdentityCallbackHost>();
        var pending = host.Begin("request");
        Object.Destroy(root);
        yield return null;
        Assert.IsTrue(pending.IsFaulted);
        Assert.IsInstanceOf<ObjectDisposedException>(pending.Exception.InnerException);
    }

    [UnityTest]
    public IEnumerator CancellationKeepsNativeOwnershipUntilTheUnityCallback()
    {
        UnityThread.Initialize();
        var source = new PendingIdentity();
        var backend = new NoBackend();
        using (var client = new AuthManager(backend, new[] { source }, true))
        using (var stop = new CancellationTokenSource())
        {
            var pending = client.SignInAsync(IdentityProvider.Google, stop.Token);
            stop.Cancel();
            yield return new WaitUntil(() => pending.IsCompleted);
            Assert.IsTrue(pending.IsCanceled);
            Assert.IsTrue(client.IsBusy);
            client.Dispose();
            Assert.IsFalse(source.Disposed);
            var proof = new IdentityProof(IdentityProvider.Google, IdentityTokenKind.IdToken, "acceptance-token");
            source.Completion.SetResult(proof);
            yield return new WaitUntil(() => !client.IsBusy);
            Assert.IsTrue(source.Disposed);
            Assert.IsFalse(backend.Called);
            Assert.Throws<ObjectDisposedException>(() => _ = proof.Token);
        }
    }

    private sealed class PendingIdentity : IIdentityProvider
    {
        internal readonly TaskCompletionSource<IdentityProof> Completion = new TaskCompletionSource<IdentityProof>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed;
        public IdentityProvider Provider => IdentityProvider.Google;
        public Task<IdentityProof> AcquireAsync(CancellationToken token) => Completion.Task;
        public void Dispose() => Disposed = true;
    }

    private sealed class NoBackend : IAuthBackend
    {
        internal bool Called;
        public AuthUser CurrentUser => null;
        public Task<AuthUser> SignInAsync(IdentityProof proof, CancellationToken token)
        {
            Called = true;
            throw new InvalidOperationException("A cancelled native identity must not reach the backend.");
        }
        public Task<AuthUser> SignInAnonymouslyAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<AuthUser> LinkAsync(string uid, IdentityProof proof, CancellationToken token) => throw new NotSupportedException();
        public Task<AuthUser> UnlinkAsync(string uid, string provider, CancellationToken token) => throw new NotSupportedException();
        public Task<string> GetSessionTokenAsync(bool refresh, CancellationToken token) => throw new NotSupportedException();
        public void SignOut()
        {
        }

        public void Dispose()
        {
        }
    }
}

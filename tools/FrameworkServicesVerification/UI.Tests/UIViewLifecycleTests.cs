using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
public sealed class UIViewLifecycleTests : IDisposable
{
    public UIViewLifecycleTests() => Object.ResetTestState();
    public void Dispose() => Object.ResetTestState();
    static Probe Create()
    {
        var view = new GameObject("UIView lifecycle probe").AddComponent<Probe>();
        view.InstanceId = 1;
        view.InternalOnCreate();
        return view;
    }

    static void Open(Probe view, int id = 1)
    {
        view.InstanceId = id;
        view.InternalSetVisible(true);
        view.InternalOnOpen(null);
    }

    [Fact]
    public void OpenAndInstanceScopesHaveSeparateCancellationAndSubscriptionLifetimes()
    {
        var view = Create();
        int openDisposed = 0, instanceDisposed = 0;
        Assert.True(view.OpenToken.IsCancellationRequested);
        var destroyToken = view.DestroyToken;
        view.AddUntilDestroyed(new Release(() => instanceDisposed++));
        Open(view);
        var first = view.OpenToken;
        view.Add(new Release(() => openDisposed++));
        view.InternalOnClose(UICloseReason.Back);
        Assert.True(first.IsCancellationRequested);
        Assert.True(view.OpenToken.IsCancellationRequested);
        Assert.False(destroyToken.IsCancellationRequested);
        Assert.Equal(1, openDisposed);
        Assert.Equal(0, instanceDisposed);
        Assert.Equal(UICloseReason.Back, view.LastCloseReason);
        Open(view, 2);
        Assert.False(view.OpenToken.IsCancellationRequested);
        Object.Destroy(view.gameObject);
        Assert.True(destroyToken.IsCancellationRequested);
        Assert.Equal(1, instanceDisposed);
        Assert.Equal(UICloseReason.Destroyed, view.LastCloseReason);
    }

    [Fact]
    public void CachedCloseReasonSurvivesLaterNativeDestruction()
    {
        var view = Create();
        Open(view);
        view.InternalOnClose(UICloseReason.Back);
        Object.Destroy(view.gameObject);
        Assert.Equal(UICloseReason.Back, view.LastCloseReason);
        var neverOpened = Create();
        neverOpened.InternalOnClose(UICloseReason.Disposed);
        Object.Destroy(neverOpened.gameObject);
        Assert.Equal(UICloseReason.Disposed, neverOpened.LastCloseReason);
    }

    [Fact]
    public void SameLeaseRefreshReleasesOldWorkWithoutDuplicatingVisibilityCallbacks()
    {
        var view = Create();
        int disposed = 0;
        Open(view);
        var old = view.OpenToken;
        view.Add(new Release(() => disposed++));
        view.InternalOnOpen(null);
        Assert.True(old.IsCancellationRequested);
        Assert.False(view.OpenToken.IsCancellationRequested);
        Assert.Equal(1, disposed);
        Assert.Equal(2, view.Opens);
        Assert.Equal(1, view.Shows);
        Assert.Equal(0, view.Hides);
    }

    [Fact]
    public void HidingPreservesOpenWorkAndRepeatedShowHideIsIdempotent()
    {
        var view = Create();
        Open(view);
        var token = view.OpenToken;
        view.InternalSetVisible(false);
        view.InternalSetVisible(false);
        Assert.True(view.IsOpen);
        Assert.False(view.IsVisible);
        Assert.False(token.IsCancellationRequested);
        Assert.Equal(1, view.Hides);
        Assert.Equal(0, view.Closes);
        view.InternalSetVisible(true);
        view.InternalSetVisible(true);
        Assert.True(view.IsVisible);
        Assert.Equal(2, view.Shows);
        view.InternalOnClose();
        Assert.Equal(2, view.Hides);
        Assert.Equal(1, view.Closes);
    }

    [Fact]
    public void CancelCallbacksCannotAttachToOrNormallyCloseAReentrantNewScope()
    {
        var view = Create();
        Open(view);
        int discarded = 0, freshDisposed = 0;
        view.OpenAction = () => view.Add(new Release(() => freshDisposed++));
        var original = view.OpenToken;
        using var registration = original.Register(() =>
        {
            view.InternalOnOpen(null); // Nested refresh wins this same lease.
            Assert.True(view.OpenToken.IsCancellationRequested); // The old cancellation callback sees its old scope.
            view.Add(new Release(() => discarded++));
            Assert.False(view.InternalCanClose(UICloseReason.Programmatic));
        });
        view.InternalOnOpen(null);
        Assert.True(view.IsOpen);
        Assert.False(view.OpenToken.IsCancellationRequested);
        Assert.Equal(1, discarded);
        Assert.Equal(0, freshDisposed);
        view.InternalOnClose();
        Assert.Equal(1, freshDisposed);
    }

    [Fact]
    public void CancellationAndDisposeExceptionsStillReleaseEverythingAndRunAllCloseHooks()
    {
        var view = Create();
        Open(view);
        int disposed = 0, closed = 0;
        using var registration = view.OpenToken.Register(() => throw new InvalidOperationException("cancel failure"));
        view.Add(new Release(() => disposed++));
        view.Add(new Release(() => throw new InvalidOperationException("dispose failure")));
        view.Add(new Release(() => disposed++));
        view.HideAction = () => throw new InvalidOperationException("hide failure");
        view.CloseAction = () => throw new InvalidOperationException("close failure");
        view.Closed += _ =>
        {
            closed++;
            throw new InvalidOperationException("event failure");
        };
        view.Closed += _ => closed++;
        Assert.Throws<InvalidOperationException>(() => view.InternalOnClose());
        Assert.Equal(2, disposed);
        Assert.Equal(2, closed);
        Assert.Equal(1, view.Closes);
        Assert.False(view.IsOpen);
        Assert.True(view.OpenToken.IsCancellationRequested);
        Assert.True(Debug.Exceptions.Count >= 3);
    }

    [Fact]
    public void OnDestroyedExceptionsStillReleaseLifetimeAndNotifyAllOwners()
    {
        var view = Create();
        Open(view);
        int disposed = 0, notified = 0;
        var token = view.DestroyToken;
        view.AddUntilDestroyed(new Release(() => disposed++));
        view.DestroyAction = () => throw new InvalidOperationException("destroy hook failure");
        view.Destroyed += _ =>
        {
            notified++;
            throw new InvalidOperationException("destroy event failure");
        };
        view.Destroyed += _ => notified++;
        Object.Destroy(view.gameObject);
        Assert.Equal(1, disposed);
        Assert.Equal(2, notified);
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(0, view.InstanceId);
    }

    [Fact]
    public async Task AsyncCloseGuardIsDeduplicatedAndSynchronousCloseCannotBypassIt()
    {
        var view = Create();
        Open(view);
        int prompts = 0;
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CloseGuard = (_, _) =>
        {
            prompts++;
            return approval.Task;
        };
        Assert.False(view.InternalCanClose(UICloseReason.Back));
        var first = view.InternalCanCloseAsync(UICloseReason.Back);
        var second = view.InternalCanCloseAsync(UICloseReason.Back);
        Assert.Equal(1, prompts);
        approval.SetResult(true);
        Assert.True(await first);
        Assert.True(await second);
        Assert.True(view.IsOpen);
    }

    [Fact]
    public async Task DifferentCloseReasonsCannotReuseTheSameGuardsApproval()
    {
        var view = Create();
        Open(view);
        int prompts = 0;
        var backApproval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var programmaticApproval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CloseGuard = (reason, _) =>
        {
            prompts++;
            return reason == UICloseReason.Back ? backApproval.Task : programmaticApproval.Task;
        };
        var backWait = view.InternalCanCloseAsync(UICloseReason.Back);
        var programmaticWait = view.InternalCanCloseAsync(UICloseReason.Programmatic);
        Assert.Equal(2, prompts);
        backApproval.SetResult(true);
        Assert.True(await backWait.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(programmaticWait.IsCompleted);
        programmaticApproval.SetResult(false);
        Assert.False(await programmaticWait.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(view.IsOpen);
    }

    [Fact]
    public async Task InterleavedCloseReasonsStillDeduplicateEachPendingGuardPair()
    {
        var view = Create();
        Open(view);
        int prompts = 0;
        var backApproval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var programmaticApproval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CloseGuard = (reason, _) =>
        {
            prompts++;
            return reason == UICloseReason.Back ? backApproval.Task : programmaticApproval.Task;
        };
        var firstBack = view.InternalCanCloseAsync(UICloseReason.Back);
        var programmatic = view.InternalCanCloseAsync(UICloseReason.Programmatic);
        var secondBack = view.InternalCanCloseAsync(UICloseReason.Back);
        Assert.Equal(2, prompts);
        backApproval.SetResult(true);
        Assert.True(await firstBack.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(await secondBack.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(programmatic.IsCompleted);
        programmaticApproval.SetResult(false);
        Assert.False(await programmatic.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(view.IsOpen);
    }

    [Fact]
    public async Task ReplacingPendingGuardCannotReuseThePreviousGuardsApproval()
    {
        var view = Create();
        Open(view);
        int prompts = 0;
        var previous = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var replacement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CloseGuard = (_, _) =>
        {
            prompts++;
            return previous.Task;
        };
        var oldWait = view.InternalCanCloseAsync(UICloseReason.Back);
        view.CloseGuard = (_, _) =>
        {
            prompts++;
            return replacement.Task;
        };
        var newWait = view.InternalCanCloseAsync(UICloseReason.Back);
        Assert.Equal(2, prompts);
        previous.SetResult(true);
        Assert.False(await oldWait.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(newWait.IsCompleted);
        replacement.SetResult(false);
        Assert.False(await newWait.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(view.IsOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingOrRefreshingStopsWaitingEvenWhenGuardIgnoresCancellation(bool refresh)
    {
        var view = Create();
        Open(view);
        var ignored = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CloseGuard = (_, _) => ignored.Task;
        var waiting = view.InternalCanCloseAsync(UICloseReason.Back);
        if (refresh)
        {
            view.InternalOnOpen(null);
        }
        else
        {
            view.InternalOnClose();
        }

        Assert.False(await waiting.WaitAsync(TimeSpan.FromSeconds(2)));
        ignored.SetException(new InvalidOperationException("late ignored guard failure"));
        Assert.Equal(refresh, view.IsOpen);
    }

    [Fact]
    public async Task CallerCancellationStopsAnIgnoringGuardAndKeepsCurrentViewOpen()
    {
        var view = Create();
        Open(view);
        using var cancel = new CancellationTokenSource();
        var ignored = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CloseGuard = (_, _) => ignored.Task;
        var waiting = view.InternalCanCloseAsync(UICloseReason.Back, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(view.IsOpen);
        ignored.SetException(new InvalidOperationException("late fault after caller cancelled"));
    }

    [Fact]
    public async Task ASecondCallerCanCancelOnlyItsOwnWaitWithoutDuplicatingTheGuard()
    {
        var view = Create();
        Open(view);
        int prompts = 0;
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CloseGuard = (_, _) =>
        {
            prompts++;
            return approval.Task;
        };
        var owner = view.InternalCanCloseAsync(UICloseReason.Back);
        using var cancel = new CancellationTokenSource();
        var follower = view.InternalCanCloseAsync(UICloseReason.Back, cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => follower.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, prompts);
        approval.SetResult(true);
        Assert.True(await owner);
    }

    [Fact]
    public async Task FirstCallerCancellationDoesNotCancelAnotherCallersSharedApproval()
    {
        var view = Create();
        Open(view);
        int prompts = 0;
        using var firstCancellation = new CancellationTokenSource();
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken guardToken = default;
        view.CloseGuard = (_, token) =>
        {
            prompts++;
            guardToken = token;
            return approval.Task;
        };
        var first = view.InternalCanCloseAsync(UICloseReason.Back, firstCancellation.Token);
        var second = view.InternalCanCloseAsync(UICloseReason.Back);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, prompts);
        Assert.False(guardToken.IsCancellationRequested);
        Assert.False(second.IsCompleted);
        approval.SetResult(true);
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(view.IsOpen);
    }

    [Fact]
    public async Task CancellingEveryCallerCancelsTheGuardAndLeavesTheViewOpen()
    {
        var view = Create();
        Open(view);
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var ignored = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken guardToken = default;
        view.CloseGuard = (_, token) =>
        {
            guardToken = token;
            return ignored.Task;
        };
        var first = view.InternalCanCloseAsync(UICloseReason.Back, firstCancellation.Token);
        var second = view.InternalCanCloseAsync(UICloseReason.Back, secondCancellation.Token);
        firstCancellation.Cancel();
        Assert.False(guardToken.IsCancellationRequested);
        secondCancellation.Cancel();
        Assert.True(guardToken.IsCancellationRequested);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(view.IsOpen);
        Assert.False(view.OpenToken.IsCancellationRequested);
        ignored.SetException(new InvalidOperationException("late guard fault after all callers cancelled"));
    }

    [Fact]
    public async Task CallerIsRegisteredBeforeGuardSynchronouslyReentersAndCancelsIt()
    {
        var view = Create();
        Open(view);
        int prompts = 0;
        using var firstCancellation = new CancellationTokenSource();
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? second = null;
        CancellationToken guardToken = default;
        view.CloseGuard = (_, token) =>
        {
            prompts++;
            guardToken = token;
            second = view.InternalCanCloseAsync(UICloseReason.Back);
            firstCancellation.Cancel();
            return approval.Task;
        };
        var first = view.InternalCanCloseAsync(UICloseReason.Back, firstCancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, prompts);
        Assert.NotNull(second);
        Assert.False(guardToken.IsCancellationRequested);
        approval.SetResult(true);
        Assert.True(await second!.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(view.IsOpen);
    }

    [Fact]
    public async Task StaleGuardApprovalCannotCloseANewLeaseOrRefresh()
    {
        var view = Create();
        Open(view);
        var approval = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        view.CloseGuard = (_, _) => approval.Task;
        var old = view.InternalCanCloseAsync(UICloseReason.Back);
        view.InternalOnClose();
        Open(view, 2);
        approval.SetResult(true);
        Assert.False(await old.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.True(view.IsOpen);
        Assert.Equal(2, view.InstanceId);
    }

    [Fact]
    public async Task SynchronousCanCloseReentryIsCheckedBeforeInvokingAnyGuard()
    {
        var view = Create();
        Open(view);
        int guards = 0;
        view.CanCloseAction = () =>
        {
            view.InternalOnClose();
            Open(view, 2);
            return true;
        };
        view.CloseGuard = (_, _) =>
        {
            guards++;
            return Task.FromResult(true);
        };
        Assert.False(await view.InternalCanCloseAsync(UICloseReason.Back));
        Assert.Equal(0, guards);
        Assert.Equal(2, view.InstanceId);
        Assert.True(view.IsOpen);
    }

    [Fact]
    public void ShowCallbackClosingTheViewCannotLeaveVisibilityOrSubscriptionsAlive()
    {
        var view = Create();
        int disposed = 0;
        view.OpenAction = () => view.Add(new Release(() => disposed++));
        view.ShowAction = () => view.InternalOnClose(UICloseReason.Cancelled);
        Open(view);
        Assert.False(view.IsOpen);
        Assert.False(view.IsVisible);
        Assert.Equal(1, disposed);
        Assert.Equal(UICloseReason.Cancelled, view.LastCloseReason);
    }

    sealed class Release : IDisposable
    {
        Action? callback;
        internal Release(Action callback)
        {
            this.callback = callback;
        }

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref callback, null);
            action?.Invoke();
        }
    }

    sealed class Probe : UIView
    {
        public int Opens, Shows, Hides, Closes;
        public Action? OpenAction, ShowAction, HideAction, CloseAction, DestroyAction;
        public Func<bool>? CanCloseAction;
        public T Add<T>(T item)
            where T : IDisposable => Track(item);
        public T AddUntilDestroyed<T>(T item)
            where T : IDisposable => TrackUntilDestroyed(item);
        protected override void OnOpen(object? args)
        {
            Opens++;
            OpenAction?.Invoke();
        }

        protected override void OnShow()
        {
            Shows++;
            ShowAction?.Invoke();
        }

        protected override void OnHide()
        {
            Hides++;
            HideAction?.Invoke();
        }

        protected override void OnClose()
        {
            Closes++;
            CloseAction?.Invoke();
        }

        protected override void OnDestroyed() => DestroyAction?.Invoke();
        protected override bool CanClose(UICloseReason reason) => CanCloseAction?.Invoke() ?? true;
    }
}
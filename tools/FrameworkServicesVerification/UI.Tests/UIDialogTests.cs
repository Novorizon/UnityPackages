using UI;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace Game.Framework.UI.Tests;
public sealed class UIDialogTests : IDisposable
{
    readonly GameObject layer;
    readonly Loader loader = new();
    readonly UIInstanceFactory factory;
    readonly PopupManager popups;
    readonly DialogManager dialogs;
    public UIDialogTests()
    {
        Object.ResetTestState();
        layer = new GameObject("Dialog Layer");
        factory = new UIInstanceFactory(loader, Enum.GetValues<UILayer>().ToDictionary(value => value, _ => layer.transform), 1);
        popups = new PopupManager(factory);
        dialogs = new DialogManager(popups);
        Register<ChoicePopup>("choice");
        Register<ConfirmPopup>("confirm");
        Register<PlainPopup>("plain");
    }

    public void Dispose()
    {
        factory.Dispose();
        Object.FlushDestroyQueueForTest();
        Object.AdvanceFrameForTest();
        Object.ResetTestState();
    }

    void Register<T>(string path)
        where T : UIView
    {
        var prefab = new GameObject(path);
        prefab.SetActive(false);
        prefab.AddComponent<T>();
        loader.Prefabs.Add(path, prefab);
    }

    [Fact]
    public async Task FalseIsACompletedValueAndSynchronousOnOpenCompletionIsNotLost()
    {
        var result = await dialogs.ShowAsync<bool>("confirm", (Action<ConfirmPopup>)(popup => popup.Select(false)));
        Assert.True(result.HasValue);
        Assert.False(result.Value);
        Assert.True(result.TryGetValue(out bool value));
        Assert.False(value);
        Assert.Equal(UICloseReason.Completed, result.CloseReason);
        Assert.Equal(0, popups.Count);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(1, factory.CachedCount);
    }

    [Fact]
    public async Task SelectionWaitsForThePopupAndClosesItBeforeReturningTheValue()
    {
        ChoicePopup? view = null;
        var showing = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(popup => view = popup));
        Assert.False(showing.IsCompleted);
        Assert.NotNull(view);
        Assert.True(view.Select("sword"));
        Assert.False(view.Select("duplicate"));
        var result = await showing;
        Assert.Equal("sword", result.Value);
        Assert.Equal(UICloseReason.Completed, result.CloseReason);
        Assert.False(view.IsOpen);
        Assert.Equal(0, popups.Count);
        Assert.Equal(0, factory.ActiveCount);
    }

    [Theory]
    [InlineData(UICloseReason.Back)]
    [InlineData(UICloseReason.Blocker)]
    [InlineData(UICloseReason.Programmatic)]
    public async Task DismissalHasNoValueAndPreservesTheCloseReason(UICloseReason reason)
    {
        var showing = dialogs.ShowAsync<string>("choice");
        Assert.True(popups.TryClose(popups.Peek()!.Value, reason));
        var result = await showing;
        Assert.False(result.HasValue);
        Assert.False(result.TryGetValue(out _));
        Assert.Equal(reason, result.CloseReason);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task ExternalNativeDestructionCompletesTheAwaiterWithDestroyedReason()
    {
        var showing = dialogs.ShowAsync<string>("choice");
        var handle = popups.Peek()!.Value;
        Object.DeferGameObjectDestructionForTest = true;
        Object.Destroy(handle.View!.gameObject);
        Assert.False(showing.IsCompleted);
        Assert.Equal(0, loader.Releases);
        Object.FlushDestroyQueueForTest();
        var result = await showing;
        Assert.False(result.HasValue);
        Assert.Equal(UICloseReason.Destroyed, result.CloseReason);
        Assert.Equal(0, popups.Count);
        Assert.Equal(1, loader.Releases);
    }

    [Fact]
    public async Task CancellingOneDialogCannotCloseAnotherDialogWithTheSamePrefab()
    {
        ChoicePopup? first = null;
        using var cancellation = new CancellationTokenSource();
        var firstTask = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => first = view));
        var secondTask = dialogs.ShowAsync<string>("choice", cancellationToken: cancellation.Token);
        Assert.Equal(2, popups.Count);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondTask);
        Assert.Equal(1, popups.Count);
        Assert.NotNull(first);
        Assert.True(first.IsOpen);
        first.Select("survives");
        Assert.Equal("survives", (await firstTask).Value);
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task CancellationDuringLoadReturnsBeforeTheLoaderAndReleasesTheLateLease()
    {
        loader.Delay = new TaskCompletionSource<GameObject?>();
        using var cancellation = new CancellationTokenSource();
        var showing = dialogs.ShowAsync<string>("choice", cancellationToken: cancellation.Token);
        Assert.False(showing.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => showing);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, loader.Releases);
        loader.Delay.SetResult(loader.Prefabs["choice"]);
        for (int i = 0; i < 8 && loader.Releases == 0; i++)
        {
            await Task.Yield();
        }

        Assert.Equal(1, loader.Releases);
        Assert.Equal(0, popups.Count);
        Assert.Equal(0, layer.transform.childCount);
    }

    [Fact]
    public async Task CachedReuseHasANewSessionAndOldCapturedCallbacksCannotCompleteIt()
    {
        ChoicePopup? first = null;
        var firstTask = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => first = view));
        Assert.NotNull(first);
        Action<string> staleCompletion = first.Capture();
        first.Select("first");
        Assert.Equal("first", (await firstTask).Value);
        ChoicePopup? second = null;
        var secondTask = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => second = view));
        Assert.Same(first, second);
        staleCompletion("stale");
        Assert.False(secondTask.IsCompleted);
        second!.Select("fresh");
        Assert.Equal("fresh", (await secondTask).Value);
        Assert.Equal(1, loader.Loads);
        Assert.Equal(0, factory.ActiveCount);
    }

    [Fact]
    public async Task ConcurrentShowsOfTheSamePrefabOwnIndependentInstancesAndResults()
    {
        ChoicePopup? first = null, second = null;
        var firstTask = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => first = view));
        var secondTask = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => second = view));
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
        second.Select("second");
        Assert.Equal("second", (await secondTask).Value);
        Assert.False(firstTask.IsCompleted);
        Assert.True(first.IsOpen);
        first.Select("first");
        Assert.Equal("first", (await firstTask).Value);
    }

    [Fact]
    public async Task CompletedSelectionBypassesACloseGuardThatWouldLeaveAnUnownedPopup()
    {
        ChoicePopup? popup = null;
        var showing = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => popup = view));
        Assert.NotNull(popup);
        int guardCalls = 0;
        popup.CloseGuard = (_, _) =>
        {
            guardCalls++;
            return Task.FromResult(false);
        };
        popup.Select("selected");
        Assert.Equal("selected", (await showing).Value);
        Assert.Equal(0, guardCalls);
        Assert.False(popup.IsOpen);
        Assert.Equal(0, popups.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongResultTypeDestroysTheMisconfiguredInstanceAndReleasesItsLease(bool deferred)
    {
        Object.DeferGameObjectDestructionForTest = deferred;
        await Assert.ThrowsAsync<InvalidOperationException>(() => dialogs.ShowAsync<int>("choice"));
        Assert.Equal(0, popups.Count);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, factory.CachedCount);
        Assert.Equal(deferred ? 0 : 1, loader.Releases);
        Object.FlushDestroyQueueForTest();
        Object.AdvanceFrameForTest();
        Assert.Equal(1, loader.Loads);
        Assert.Equal(1, loader.Releases);
        Assert.Equal(0, layer.transform.childCount);
    }

    [Fact]
    public async Task PlainPopupAndSingletonOptionsAreRejectedWithoutLeavingAnActiveView()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => dialogs.ShowAsync<string>("choice", options: new PopupOptions { SingletonByPath = true }));
        Assert.Equal(0, loader.Loads);
        await Assert.ThrowsAsync<InvalidOperationException>(() => dialogs.ShowAsync<string>("plain"));
        Assert.Equal(0, popups.Count);
        Assert.Equal(1, loader.Releases);
    }

    [Fact]
    public async Task AlreadyCancelledShowDoesNotAcquireAnyResource()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dialogs.ShowAsync<string>("choice", cancellationToken: cancellation.Token));
        Assert.Equal(0, loader.Loads);
        Assert.Equal(0, popups.Count);
    }

    [Fact]
    public async Task OwnerDisposalCompletesPendingDialogWithoutASelection()
    {
        var showing = dialogs.ShowAsync<string>("choice");
        factory.Dispose();
        var result = await showing;
        Assert.False(result.HasValue);
        Assert.Equal(UICloseReason.Disposed, result.CloseReason);
        Assert.Equal(0, popups.Count);
        Assert.Equal(loader.Loads, loader.Releases);
    }

    [Fact]
    public async Task CompletionCleanupFailureFaultsTheOperationWithoutKeepingThePopupActive()
    {
        ChoicePopup? popup = null;
        var showing = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => popup = view));
        Assert.NotNull(popup);
        popup.FailClose = true;
        popup.Select("selected");
        await Assert.ThrowsAsync<InvalidOperationException>(() => showing);
        Assert.Equal(0, popups.Count);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(1, loader.Releases);
    }

    [Fact]
    public async Task CancellationCleanupFailureCannotReplaceTheCallersCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        ChoicePopup? popup = null;
        var showing = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => popup = view), cancellationToken: cancellation.Token);
        Assert.NotNull(popup);
        popup.FailClose = true;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => showing);
        Assert.Equal(0, popups.Count);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(1, loader.Releases);
        Assert.Single(Debug.Exceptions);
    }

    [Fact]
    public async Task ClosingBeforePopupRegistrationStillReturnsTheCapturedDismissal()
    {
        loader.Delay = new TaskCompletionSource<GameObject?>();
        var queue = new QueuedContext();
        var originalContext = SynchronizationContext.Current;
        Task<UIDialogResult<string>> showing;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queue);
            showing = dialogs.ShowAsync<string>("choice");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        loader.Delay.SetResult(loader.Prefabs["choice"]);
        Assert.True(queue.RunOne()); // Factory finished opening; PopupManager has not registered it yet.
        Assert.Equal(1, factory.ActiveCount);
        Assert.Equal(0, popups.Count);
        factory.Dispose();
        queue.RunAll();
        Assert.True(showing.IsCompleted);
        var result = await showing;
        Assert.False(result.HasValue);
        Assert.Equal(UICloseReason.Disposed, result.CloseReason);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(loader.Loads, loader.Releases);
    }

    [Fact]
    public async Task ClosingAndReusingBetweenPopupAndDialogContinuationsCannotLoseTheOldTask()
    {
        loader.Delay = new TaskCompletionSource<GameObject?>();
        ChoicePopup? first = null;
        var queue = new QueuedContext();
        var originalContext = SynchronizationContext.Current;
        Task<UIDialogResult<string>> showing;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queue);
            showing = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => first = view));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalContext);
        }

        loader.Delay.SetResult(loader.Prefabs["choice"]);
        Assert.True(queue.RunOne()); // Factory completion.
        Assert.True(queue.RunOne()); // Popup registration, but the DialogManager continuation is still queued.
        Assert.Equal(1, popups.Count);
        Assert.False(showing.IsCompleted);
        Assert.NotNull(first);
        UIHandle oldHandle = popups.Peek()!.Value;
        Assert.True(popups.TryClose(oldHandle, UICloseReason.Back));
        UIHandle replacement = await popups.OpenAsync("choice");
        Assert.Same(first, replacement.View);
        Assert.NotEqual(oldHandle.Id, replacement.Id);
        queue.RunAll();
        Assert.True(showing.IsCompleted);
        var result = await showing;
        Assert.False(result.HasValue);
        Assert.Equal(UICloseReason.Back, result.CloseReason);
        Assert.True(replacement.IsValid);
        Assert.Equal(1, popups.Count);
        popups.ForceClose(replacement, UICloseReason.Programmatic);
    }

    [Fact]
    public async Task DrainWaitsForCancelledUiCleanupButDoesNotWaitForAnUncancelledDialog()
    {
        using var cancellation = new CancellationTokenSource();
        var queue = new QueuedContext();
        var cancelledShow = StartQueuedDialog(queue, cancellation.Token);
        ChoicePopup? remaining = null;
        var remainingShow = dialogs.ShowAsync<string>("choice", (Action<ChoicePopup>)(view => remaining = view));
        cancellation.Cancel();
        Task draining = dialogs.DrainCancelledAsync();
        Assert.False(draining.IsCompleted);
        Assert.Equal(2, popups.Count);
        await queue.WaitForWorkAsync().WaitAsync(TimeSpan.FromSeconds(5));
        queue.RunAll();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledShow);
        await draining;
        Assert.False(remainingShow.IsCompleted);
        Assert.Equal(1, popups.Count);
        Assert.NotNull(remaining);
        Assert.True(remaining.IsOpen);
        Assert.True(dialogs.DrainCancelledAsync().IsCompleted);
        remaining.Select("still open");
        Assert.Equal("still open", (await remainingShow).Value);
    }

    [Fact]
    public async Task OwnerCancellationCanInterruptTheDrainWithoutCancellingOtherWork()
    {
        using var cancellation = new CancellationTokenSource();
        using var owner = new CancellationTokenSource();
        var queue = new QueuedContext();
        var showing = StartQueuedDialog(queue, cancellation.Token);
        cancellation.Cancel();
        Task draining = dialogs.DrainCancelledAsync(owner.Token);
        owner.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => draining);
        Assert.False(showing.IsCompleted);
        Assert.Equal(1, popups.Count);
        await queue.WaitForWorkAsync().WaitAsync(TimeSpan.FromSeconds(5));
        queue.RunAll();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => showing);
        await dialogs.DrainCancelledAsync();
        Assert.Equal(0, popups.Count);
    }

    [Fact]
    public async Task DrainingACancelledOpenDoesNotWaitForAnUnabortableResourceLoad()
    {
        loader.Delay = new TaskCompletionSource<GameObject?>();
        using var cancellation = new CancellationTokenSource();
        var queue = new QueuedContext();
        var showing = StartQueuedDialog(queue, cancellation.Token);
        cancellation.Cancel();
        Task draining = dialogs.DrainCancelledAsync();
        Assert.False(draining.IsCompleted);
        await queue.WaitForWorkAsync().WaitAsync(TimeSpan.FromSeconds(5));
        queue.RunAll();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => showing);
        await draining;
        Assert.False(loader.Delay.Task.IsCompleted);
        Assert.Equal(0, factory.ActiveCount);
        Assert.Equal(0, loader.Releases);
        loader.Delay.SetResult(loader.Prefabs["choice"]);
        await queue.WaitForWorkAsync().WaitAsync(TimeSpan.FromSeconds(5));
        queue.RunAll();
        Assert.Equal(1, loader.Releases);
        Assert.Equal(0, layer.transform.childCount);
    }

    Task<UIDialogResult<string>> StartQueuedDialog(QueuedContext queue, CancellationToken cancellationToken)
    {
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(queue);
            return dialogs.ShowAsync<string>("choice", cancellationToken: cancellationToken);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    sealed class QueuedContext : SynchronizationContext
    {
        sealed class QueueState
        {
            internal readonly Queue<(SendOrPostCallback Callback, object? State)> Queue = new();
            internal TaskCompletionSource<bool> Available = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        readonly QueueState state;
        public QueuedContext() : this(new QueueState())
        {
        }

        QueuedContext(QueueState state)
        {
            this.state = state;
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            TaskCompletionSource<bool> available;
            lock (this.state.Queue)
            {
                this.state.Queue.Enqueue((callback, state));
                available = this.state.Available;
            }

            available.TrySetResult(true);
        }

        public Task WaitForWorkAsync()
        {
            lock (state.Queue)
            {
                return state.Queue.Count != 0 ? Task.CompletedTask : state.Available.Task;
            }
        }

        public bool RunOne()
        {
            (SendOrPostCallback Callback, object? State) work;
            lock (state.Queue)
            {
                if (state.Queue.Count == 0)
                {
                    return false;
                }

                work = state.Queue.Dequeue();
                if (state.Queue.Count == 0)
                {
                    state.Available = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }

            SynchronizationContext? previous = Current;
            try
            {
                // A distinct context instance prevents Task's same-context inline shortcut,
                // exposing each async handoff without sleeps, threads or frame timing guesses.
                SetSynchronizationContext(new QueuedContext(state));
                work.Callback(work.State);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }

            return true;
        }

        public void RunAll()
        {
            int count = 0;
            while (RunOne())
            {
                if (++count > 100)
                {
                    throw new InvalidOperationException("The test UI continuation queue did not drain.");
                }
            }
        }
    }

    sealed class Loader : IUIAssetLoader
    {
        public readonly Dictionary<string, GameObject> Prefabs = new();
        public TaskCompletionSource<GameObject?>? Delay;
        public int Loads, Releases;
        public Task<GameObject?> LoadPrefabAsync(string path)
        {
            Loads++;
            return Delay?.Task ?? Task.FromResult(Prefabs.TryGetValue(path, out GameObject? prefab) ? prefab : null);
        }

        public void ReleasePrefab(GameObject prefab)
        {
            Assert.Contains(prefab, Prefabs.Values);
            Releases++;
        }
    }

    sealed class ChoicePopup : UIResultPopup<string>
    {
        public bool FailClose = false;
        public bool Select(string value) => TryComplete(value);
        public Action<string> Capture() => CaptureCompletion();
        protected override void OnOpen(object? args) => (args as Action<ChoicePopup>)?.Invoke(this);
        protected override void OnClose()
        {
            if (FailClose)
            {
                throw new InvalidOperationException("Dialog close failure.");
            }
        }
    }

    sealed class ConfirmPopup : UIResultPopup<bool>
    {
        public void Select(bool value) => Complete(value);
        protected override void OnOpen(object? args) => (args as Action<ConfirmPopup>)?.Invoke(this);
    }

    sealed class PlainPopup : UIPopup
    {
    }
}
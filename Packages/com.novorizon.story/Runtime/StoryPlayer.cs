using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Story
{
    /// <summary>One presentation owner per player. Unity adapters must be started on Unity's context.</summary>
    public sealed class StoryPlayer : IDisposable, IAsyncDisposable
    {
        private sealed class WallClock : IStoryClock
        {
            public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken);
        }

        private readonly object gate = new object();
        private readonly IStoryPresenter presenter;
        private readonly IStoryCommands commands;
        private readonly IStoryClock clock;
        private readonly int maximumSteps;
        private StorySession active;
        private bool disposed;

        public StoryPlayer(IStoryPresenter presenter, IStoryCommands commands = null, IStoryClock clock = null, int maximumSteps = 10000)
        {
            this.presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
            if (maximumSteps <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumSteps));
            }
            this.commands = commands;
            this.clock = clock ?? new WallClock();
            this.maximumSteps = maximumSteps;
        }

        public bool IsPlaying
        {
            get
            {
                lock (gate)
                {
                    return active != null;
                }
            }
        }

        public StorySession Play(StorySequence sequence, CancellationToken cancellationToken = default, StoryCheckpoint checkpoint = null)
        {
            if (!TryPlay(sequence, out var session, cancellationToken, checkpoint))
            {
                throw new StoryBusyException();
            }
            return session;
        }

        public bool TryPlay(StorySequence sequence, out StorySession session, CancellationToken cancellationToken = default, StoryCheckpoint checkpoint = null)
        {
            if (sequence == null)
            {
                throw new ArgumentNullException(nameof(sequence));
            }
            checkpoint?.Validate(sequence);
            lock (gate)
            {
                if (disposed)
                {
                    throw new ObjectDisposedException(nameof(StoryPlayer));
                }
                if (active != null)
                {
                    session = null;
                    return false;
                }
                session = new StorySession(this, presenter, sequence, cancellationToken, checkpoint);
                active = session;
            }
            session.Start(commands, clock, maximumSteps);
            return true;
        }

        internal void Release(StorySession session)
        {
            lock (gate)
            {
                if (ReferenceEquals(active, session))
                {
                    active = null;
                }
            }
        }

        public Task StopAsync()
        {
            StorySession current;
            lock (gate)
            {
                current = active;
            }
            if (current == null)
            {
                return Task.CompletedTask;
            }
            current.Cancel();
            return current.Completion;
        }

        public void Dispose()
        {
            StorySession current;
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                current = active;
            }
            current?.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            Dispose();
            await StopAsync();
        }
    }

    /// <summary>
    /// A single run, including its cleanup. Ports must cooperate with cancellation for prompt stop;
    /// an ignored token leaves the player Busy until that actual operation and cleanup settle.
    /// </summary>
    public sealed class StorySession
    {
        // CancelAfter lets exceptions from cancellation callbacks escape its timer thread.
        // This timer owns delivery explicitly so port failures can become StoryResult errors.
        private sealed class StepTimeout
        {
            private readonly object gate = new object();
            private readonly CancellationTokenSource source;
            private readonly Timer timer;
            private bool closed;
            private bool fired;
            private Exception error;
            private TaskCompletionSource<bool> delivered;

            internal StepTimeout(CancellationTokenSource source, double seconds)
            {
                this.source = source;
                if (seconds > 0)
                {
                    timer = new Timer(Expire, null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
                }
            }

            internal bool Fired
            {
                get
                {
                    lock (gate)
                    {
                        return fired;
                    }
                }
            }

            internal Exception Error
            {
                get
                {
                    lock (gate)
                    {
                        return error;
                    }
                }
            }

            private void Expire(object state)
            {
                TaskCompletionSource<bool> delivery;
                lock (gate)
                {
                    if (closed)
                    {
                        return;
                    }
                    fired = true;
                    delivered = delivery = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
                try
                {
                    source.Cancel();
                }
                catch (Exception callbackError)
                {
                    lock (gate)
                    {
                        error = callbackError;
                    }
                }
                finally
                {
                    delivery.TrySetResult(true);
                }
            }

            internal Task FinishAsync()
            {
                Task pending;
                lock (gate)
                {
                    closed = true;
                    pending = delivered?.Task ?? Task.CompletedTask;
                }
                timer?.Dispose();
                return pending;
            }
        }

        private readonly object gate = new object();
        private readonly StoryPlayer owner;
        private readonly IStoryPresenter presenter;
        private readonly StorySequence sequence;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly CancellationTokenRegistration externalCancellation;
        private readonly TaskCompletionSource<StoryResult> completion = new TaskCompletionSource<StoryResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<StorySelection> selections = new List<StorySelection>();
        private StoryStepContext context;
        private StoryNode current;
        private StoryCheckpoint checkpoint;
        private StoryStatus? stopReason;
        private Exception stopError;
        private TaskCompletionSource<bool> stopDelivered;
        private TaskCompletionSource<bool> controlsSettled;
        private int controlCalls;
        private bool closing;
        private bool finished;
        private long stepVersion;

        public Guid Id
        {
            get;
        } = Guid.NewGuid();
        public Task<StoryResult> Completion => completion.Task;

        public string CurrentNodeId
        {
            get
            {
                lock (gate)
                {
                    return current?.Id;
                }
            }
        }

        public StoryCheckpoint Checkpoint
        {
            get
            {
                lock (gate)
                {
                    return checkpoint;
                }
            }
        }

        internal StorySession(StoryPlayer owner, IStoryPresenter presenter, StorySequence sequence, CancellationToken token, StoryCheckpoint restored)
        {
            this.owner = owner;
            this.presenter = presenter;
            this.sequence = sequence;
            if (restored != null)
            {
                selections.AddRange(restored.Selections);
            }
            checkpoint = restored ?? new StoryCheckpoint(sequence.Id, sequence.Version, sequence.EntryNodeId);
            current = sequence.GetNode(checkpoint.NodeId);
            externalCancellation = token.Register(() => RequestStop(StoryStatus.Cancelled));
        }

        internal void Start(IStoryCommands commands, IStoryClock clock, int maximumSteps)
        {
            // Run always converts execution/cleanup errors into a StoryResult.
            _ = RunAsync(commands, clock, maximumSteps);
        }

        public bool Advance()
        {
            StoryStepContext observed;
            lock (gate)
            {
                if (finished || closing || stopReason.HasValue || context == null || current?.Kind != StoryNodeKind.Say)
                {
                    return false;
                }
                observed = context;
                if (controlCalls++ == 0)
                {
                    controlsSettled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            try
            {
                presenter.Advance(observed);
                return true;
            }
            catch (Exception error)
            {
                RequestStop(StoryStatus.Failed, error);
                return false;
            }
            finally
            {
                TaskCompletionSource<bool> settled = null;
                lock (gate)
                {
                    if (--controlCalls == 0)
                    {
                        settled = controlsSettled;
                    }
                }
                settled?.TrySetResult(true);
            }
        }

        public bool Skip() => RequestStop(StoryStatus.Skipped);

        public bool Cancel() => RequestStop(StoryStatus.Cancelled);

        private bool RequestStop(StoryStatus reason, Exception error = null)
        {
            TaskCompletionSource<bool> delivered;
            lock (gate)
            {
                if (finished)
                {
                    return false;
                }
                if (reason == StoryStatus.Skipped && !current.CanSkip)
                {
                    return false;
                }
                if (error != null)
                {
                    stopError = Combine(stopError, error);
                }
                if (stopReason.HasValue)
                {
                    return false;
                }
                stopReason = reason;
                stopDelivered = delivered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            try
            {
                cancellation.Cancel();
            }
            catch (Exception callbackError)
            {
                lock (gate)
                {
                    stopError = Combine(stopError, callbackError);
                }
            }
            finally
            {
                delivered.TrySetResult(true);
            }
            return true;
        }

        private async Task RunAsync(IStoryCommands commands, IStoryClock clock, int maximumSteps)
        {
            StoryStatus status = StoryStatus.Completed;
            Exception error = null;
            bool attemptedOpen = false;
            try
            {
                ThrowIfStopped();
                attemptedOpen = true;
                await presenter.OpenAsync(sequence, Id, cancellation.Token);
                ThrowIfStopped();
                string nodeId = Checkpoint.NodeId;
                int count = 0;
                while (nodeId != null)
                {
                    ThrowIfStopped();
                    if (count == maximumSteps)
                    {
                        throw new InvalidOperationException("Story exceeded its configured maximum step count.");
                    }
                    count++;
                    StoryNode node = sequence.GetNode(nodeId);
                    StoryStepContext step;
                    lock (gate)
                    {
                        ThrowIfStopped();
                        current = node;
                        checkpoint = new StoryCheckpoint(sequence.Id, sequence.Version, nodeId, selections);
                        context = step = new StoryStepContext(Id, ++stepVersion, sequence, nodeId, selections);
                    }
                    StoryStepOutcome outcome = await ExecuteStepAsync(node, step, commands, clock);
                    ThrowIfStopped();
                    string next = sequence.Next(node, outcome);
                    if (node.Kind == StoryNodeKind.Choice)
                    {
                        lock (gate)
                        {
                            selections.Add(new StorySelection(node.Id, outcome.ChoiceId));
                        }
                    }
                    nodeId = next;
                }
            }
            catch (OperationCanceledException cancelled)
            {
                lock (gate)
                {
                    if (stopReason.HasValue)
                    {
                        status = stopReason.Value;
                    }
                    else
                    {
                        status = StoryStatus.Failed;
                        error = cancelled;
                    }
                }
            }
            catch (Exception executionError)
            {
                status = StoryStatus.Failed;
                error = executionError;
            }
            Task pendingControls;
            lock (gate)
            {
                closing = true;
                pendingControls = controlCalls == 0 ? Task.CompletedTask : controlsSettled.Task;
            }
            await pendingControls;
            if (attemptedOpen)
            {
                try
                {
                    await presenter.CloseAsync(Id);
                }
                catch (Exception closeError)
                {
                    error = Combine(error, closeError);
                    status = StoryStatus.Failed;
                }
            }
            await PublishAsync(status, error);
        }

        private void ThrowIfStopped()
        {
            lock (gate)
            {
                if (stopReason.HasValue)
                {
                    throw new OperationCanceledException(cancellation.Token);
                }
                cancellation.Token.ThrowIfCancellationRequested();
            }
        }

        private async Task<StoryStepOutcome> ExecuteStepAsync(StoryNode node, StoryStepContext step, IStoryCommands commands, IStoryClock clock)
        {
            using (var source = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token))
            {
                var timeout = new StepTimeout(source, node.TimeoutSeconds);
                StoryStepOutcome outcome = StoryStepOutcome.Continue;
                Exception error = null;
                try
                {
                    ThrowIfStopped();
                    outcome = await ExecuteAsync(node, step, commands, clock, source.Token);
                    source.Token.ThrowIfCancellationRequested();
                    ThrowIfStopped();
                }
                catch (Exception executionError)
                {
                    error = executionError;
                }
                await timeout.FinishAsync();
                lock (gate)
                {
                    if (timeout.Fired && !stopReason.HasValue)
                    {
                        error = new TimeoutException("Story node timed out: " + node.Id, error);
                    }
                }
                if (timeout.Error != null)
                {
                    error = Combine(error, timeout.Error);
                }
                if (error != null)
                {
                    ExceptionDispatchInfo.Capture(error).Throw();
                }
                return outcome;
            }
        }

        private async Task<StoryStepOutcome> ExecuteAsync(
            StoryNode node,
            StoryStepContext step,
            IStoryCommands commands,
            IStoryClock clock,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            switch (node.Kind)
            {
                case StoryNodeKind.Wait:
                    await clock.DelayAsync(TimeSpan.FromSeconds(node.DurationSeconds), token);
                    return StoryStepOutcome.Continue;
                case StoryNodeKind.Command:
                    if (commands == null)
                    {
                        throw new InvalidOperationException("Provide a project IStoryCommands adapter before executing command " + node.CommandId);
                    }
                    await commands.ExecuteAsync(node.CommandId, node.Parameters, step, token);
                    return StoryStepOutcome.Continue;
                case StoryNodeKind.End:
                    return StoryStepOutcome.Continue;
                default:
                    return await presenter.ExecuteAsync(node, step, token);
            }
        }

        private async Task PublishAsync(StoryStatus status, Exception error)
        {
            StoryResult result;
            while (true)
            {
                Task wait = null;
                lock (gate)
                {
                    if (stopDelivered != null && !stopDelivered.Task.IsCompleted)
                    {
                        wait = stopDelivered.Task;
                    }
                    else
                    {
                        finished = true;
                        if (stopError != null)
                        {
                            error = Combine(error, stopError);
                            status = StoryStatus.Failed;
                        }
                        else if (status == StoryStatus.Completed && stopReason.HasValue)
                        {
                            status = stopReason.Value;
                        }
                        result = new StoryResult(status, sequence, checkpoint, error);
                        context = null;
                        break;
                    }
                }
                await wait;
            }
            externalCancellation.Dispose();
            cancellation.Dispose();
            owner.Release(this);
            completion.TrySetResult(result);
        }

        private static Exception Combine(Exception first, Exception second) => first == null ? second : new AggregateException(first, second);
    }
}

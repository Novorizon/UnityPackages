using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Story
{
    /// <summary>One presentation owner per player. Unity adapters must be started on Unity's context.</summary>
    /// <remarks>启动和停止剧情会话，管理当前会话对演出器的占用。</remarks>
    public sealed class StoryPlayer : IDisposable, IAsyncDisposable
    {
        /// <summary>表示剧情 Player中的Wall 时钟。</summary>
        private sealed class WallClock : IStoryClock
        {
            /// <summary>根据传入参数取得DelayAsync。</summary>
            public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken) => Task.Delay(duration, cancellationToken);
        }

        private readonly object gate = new object();
        private readonly IStoryPresenter presenter;
        private readonly IStoryCommands commands;
        private readonly IStoryClock clock;
        private readonly int maximumSteps;
        private StorySession active;
        private bool disposed;

        /// <summary>使用调用方提供的presenter、commands、clock创建 StoryPlayer。</summary>
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

        /// <summary>启动一个剧情会话，也可以从指定的剧情检查点恢复。</summary>
        public StorySession Play(StorySequence sequence, CancellationToken cancellationToken = default, StoryCheckpoint checkpoint = null)
        {
            if (!TryPlay(sequence, out var session, cancellationToken, checkpoint))
            {
                throw new StoryBusyException();
            }
            return session;
        }

        /// <summary>尝试处理 Play，返回是否成功。</summary>
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

        /// <summary>结束剧情 Player对相关资源的持有，并执行该类型的清理流程。</summary>
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

        /// <summary>发出停止请求，返回可等待的停止任务。</summary>
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

        /// <summary>结束剧情 Player对相关资源的持有，并执行该类型的清理流程。</summary>
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

        /// <summary>等待资源和后台操作完成异步清理。</summary>
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
    /// <remarks>提供剧情会话的实现。</remarks>
    public sealed class StorySession
    {
        // CancelAfter lets exceptions from cancellation callbacks escape its timer thread.
        // This timer owns delivery explicitly so port failures can become StoryResult errors.
        /// <summary>表示剧情会话中的步骤 Timeout。</summary>
        private sealed class StepTimeout
        {
            private readonly object gate = new object();
            private readonly CancellationTokenSource source;
            private readonly Timer timer;
            private bool closed;
            private bool fired;
            private Exception error;
            private TaskCompletionSource<bool> delivered;

            /// <summary>使用调用方提供的source、seconds创建 StepTimeout。</summary>
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

            /// <summary>调用 source.Cancel 完成Expire。</summary>
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

            /// <summary>异步结束步骤 Timeout。</summary>
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

        /// <summary>使用调用方提供的owner、presenter、sequence创建 StorySession。</summary>
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

        /// <summary>启动剧情会话。</summary>
        internal void Start(IStoryCommands commands, IStoryClock clock, int maximumSteps)
        {
            // Run always converts execution/cleanup errors into a StoryResult.
            _ = RunAsync(commands, clock, maximumSteps);
        }

        /// <summary>推进剧情会话。</summary>
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

        /// <summary>跳过剧情会话。</summary>
        public bool Skip() => RequestStop(StoryStatus.Skipped);

        /// <summary>取消剧情会话。</summary>
        public bool Cancel() => RequestStop(StoryStatus.Cancelled);

        /// <summary>返回请求 Stop的判断结果。</summary>
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

        // 按节点执行剧情，统一处理正常完成、停止和执行异常。
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

        /// <summary>在已停止时抛出异常。</summary>
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

        /// <summary>异步执行步骤。</summary>
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

        /// <summary>异步执行当前操作。</summary>
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

        // 结束会话并发布最终结果，调用方可等待会话清理完成。
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

        /// <summary>根据传入参数取得Combine。</summary>
        private static Exception Combine(Exception first, Exception second) => first == null ? second : new AggregateException(first, second);
    }
}

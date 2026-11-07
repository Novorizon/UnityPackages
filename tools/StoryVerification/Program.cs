using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Story;

internal static class Program
{
    private static int passed;

    private static async Task Main()
    {
        await Run("linear flow, commands and clock preserve explicit ownership", Linear);
        await Run("choice branches by stable option ID and snapshots selection", Branch);
        await Run("Busy includes cleanup and old sessions cannot advance replacements", BusyCleanup);
        await Run("caller cancellation is not completion and prevents later commands", Cancel);
        await Run("skip has distinct result and respects unskippable nodes", Skip);
        await Run("noncooperative operation keeps Busy until it settles", NoncooperativeCancel);
        await Run("open failure still cleans up and cannot complete story", OpenFailure);
        await Run("execution and cleanup exceptions both remain observable", CleanupFailure);
        await Run("unexpected cancellation from a port is a failure", UnexpectedCancellation);
        await Run("cooperative node timeout fails instead of completing", Timeout);
        await Run("noncooperative timeout waits then fails", NoncooperativeTimeout);
        await Run("invalid presenter choice fails and never executes next command", InvalidChoice);
        await Run("advance epochs ignore prior node callbacks", AdvanceEpochs);
        await Run("checkpoint resume uses stable IDs and exact content version", Resume);
        await Run("checkpoint history and command identity survive logical resume", CheckpointHistory);
        await Run("graph validation rejects missing/unreachable/instant cycles", GraphValidation);
        await Run("model input collections cannot mutate running content", ImmutableInputs);
        await Run("run budget bounds immediate interactive cycles", StepBudget);
        await Run("cancellation callback failures do not hang completion", CancellationCallbackFailure);
        await Run("dispose cancels and waits for presentation cleanup", Dispose);
        await Run("already cancelled caller never opens presentation", InitiallyCancelled);
        await Run("stop during close cannot publish Completed", CancelDuringClose);
        await Run("command timeout and failures use terminal failure", CommandErrors);
        await Run("cleanup failure takes precedence over caller cancellation", CancelAndCloseFailure);
        await Run("timeout callback failure settles instead of escaping timer", TimeoutCallbackFailure);
        await Run("inline Advance failure stays Busy and fails before cleanup", ReentrantAdvanceFailure);
        await Run("skip cannot cross into unskippable command during transition", SkipTransition);
        await Run("unskippable entry also rejects skip during loading", SkipDuringOpen);
        Check(passed == 28, "Expected scenario count");
        Console.WriteLine("PASS " + passed + " story runtime scenarios; pure managed runtime, no Unity/media execution.");
    }

    private static async Task Run(string name, Func<Task> action)
    {
        await action().WaitAsync(TimeSpan.FromSeconds(8));
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static StorySequence Sequence(params StoryNode[] nodes) => new StorySequence("intro", "1", nodes);
    private static StoryNode End(string id = "end") => new StoryNode(id, StoryNodeKind.End);
    private static StoryNode Say(string id = "say", string next = null, bool canSkip = true, double timeout = 0) => new StoryNode(id, StoryNodeKind.Say, nextId: next, textKey: "line." + id, canSkip: canSkip, timeoutSeconds: timeout);
    private static TaskCompletionSource<T> Gate<T>() => new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task Linear()
    {
        var stage = new Presenter();
        var commands = new Commands();
        var clock = new Clock();
        using var player = new StoryPlayer(stage, commands, clock);
        var sequence = Sequence(
            Say(),
            new StoryNode("portrait", StoryNodeKind.SetPortrait, resourceId: "hero.happy", slotId: "left"),
            new StoryNode("background", StoryNodeKind.SetBackground, resourceId: "room"),
            new StoryNode("wait", StoryNodeKind.Wait, durationSeconds: 2),
            new StoryNode("reward", StoryNodeKind.Command, commandId: "quest.signal"),
            End());
        StoryResult result = await player.Play(sequence).Completion;
        Check(result.Status == StoryStatus.Completed, "Completed result");
        Equal(stage.Calls, "open", "say", "portrait", "background", "close");
        Check(clock.Seconds == 2 && commands.Calls == 1, "Dedicated ports");
        Check(result.NodeId == "end" && !player.IsPlaying, "Cleanup before completion");
        Check(commands.Context.CommandInstanceId == "intro/1/reward", "Stable command content identity");
    }

    private static async Task Branch()
    {
        var stage = new Presenter { Execute = (_, _, _) => Task.FromResult(StoryStepOutcome.Choose("right")) };
        using var player = new StoryPlayer(stage);
        var options = new[] { new StoryChoice("left", "option.left", "left-end"), new StoryChoice("right", "option.right", "right-end") };
        var result = await player.Play(Sequence(new StoryNode("choice", StoryNodeKind.Choice, choices: options), End("left-end"), End("right-end"))).Completion;
        Check(result.Status == StoryStatus.Completed && result.NodeId == "right-end", "Target selected by ID");
        Check(result.Selections.Count == 1 && result.Selections[0].ChoiceId == "right", "Selection returned");
    }

    private static async Task BusyCleanup()
    {
        var close = Gate<bool>();
        var stage = new Presenter { Close = _ => close.Task };
        using var player = new StoryPlayer(stage);
        var first = player.Play(Sequence(End()));
        Check(player.IsPlaying && !first.Completion.IsCompleted, "Still owns presentation");
        Throws<StoryBusyException>(() => player.Play(Sequence(End())));
        Check(!player.TryPlay(Sequence(End()), out var rejected) && rejected == null, "TryPlay Busy result");
        close.SetResult(true);
        Check((await first.Completion).Status == StoryStatus.Completed, "Close settled");
        stage.Close = _ => Task.CompletedTask;
        var second = player.Play(Sequence(End()));
        Check(!first.Advance() && !first.Cancel() && !first.Skip(), "Stale session controls rejected");
        Check((await second.Completion).Status == StoryStatus.Completed, "Replacement can play");
    }

    private static async Task Cancel()
    {
        var entered = Gate<bool>();
        var stage = new Presenter
        {
            Execute = async (_, _, token) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(-1, token);
                return StoryStepOutcome.Continue;
            }
        };
        var commands = new Commands();
        using var player = new StoryPlayer(stage, commands);
        using var cancel = new CancellationTokenSource();
        var session = player.Play(Sequence(Say(), new StoryNode("reward", StoryNodeKind.Command, commandId: "reward"), End()), cancel.Token);
        await entered.Task;
        cancel.Cancel();
        var result = await session.Completion;
        Check(result.Status == StoryStatus.Cancelled && commands.Calls == 0 && stage.CloseCount == 1, "Cancellation cannot execute subsequent side effects");
    }

    private static async Task Skip()
    {
        var entered = Gate<bool>();
        var stage = new Presenter
        {
            Execute = async (_, _, token) =>
            {
                entered.TrySetResult(true);
                await Task.Delay(-1, token);
                return StoryStepOutcome.Continue;
            }
        };
        using var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(Say(), End()));
        await entered.Task;
        Check(session.Skip(), "Skip accepted");
        Check((await session.Completion).Status == StoryStatus.Skipped, "Skip distinct from completion");
        entered = Gate<bool>();
        session = player.Play(Sequence(Say(canSkip: false), End()));
        await entered.Task;
        Check(!session.Skip(), "Unskippable node");
        session.Cancel();
        Check((await session.Completion).Status == StoryStatus.Cancelled, "Cancel always available");
    }

    private static async Task NoncooperativeCancel()
    {
        var held = Gate<StoryStepOutcome>();
        var stage = new Presenter { Execute = (_, _, _) => held.Task };
        var commands = new Commands();
        using var player = new StoryPlayer(stage, commands);
        var session = player.Play(Sequence(Say(), new StoryNode("command", StoryNodeKind.Command, commandId: "reward"), End()));
        session.Cancel();
        Check(player.IsPlaying && !session.Completion.IsCompleted && stage.CloseCount == 0, "Ignored cancellation cannot detach live operation");
        held.SetResult(StoryStepOutcome.Continue);
        Check((await session.Completion).Status == StoryStatus.Cancelled && commands.Calls == 0, "Late return cannot continue");
    }

    private static async Task OpenFailure()
    {
        var stage = new Presenter { Open = (_, _, _) => throw new InvalidOperationException("load failed") };
        using var player = new StoryPlayer(stage);
        var result = await player.Play(Sequence(End())).Completion;
        Check(result.Status == StoryStatus.Failed && result.Error.Message == "load failed" && stage.CloseCount == 1, "Failed open cleans partial stage");
    }

    private static async Task CleanupFailure()
    {
        var close = Gate<bool>();
        var stage = new Presenter { Execute = (_, _, _) => throw new ArgumentException("execution"), Close = _ => close.Task };
        using var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(Say(), End()));
        Check(player.IsPlaying && !session.Completion.IsCompleted, "Error does not bypass cleanup");
        close.SetException(new InvalidOperationException("cleanup"));
        var result = await session.Completion;
        Check(result.Status == StoryStatus.Failed && result.Error is AggregateException aggregate && aggregate.InnerExceptions.Count == 2, "Both errors observed");
    }

    private static async Task UnexpectedCancellation()
    {
        var stage = new Presenter { Execute = (_, _, _) => throw new OperationCanceledException("wrong operation") };
        using var player = new StoryPlayer(stage);
        var result = await player.Play(Sequence(Say(), End())).Completion;
        Check(result.Status == StoryStatus.Failed && result.Error is OperationCanceledException, "No caller cancellation requested");
    }

    private static async Task Timeout()
    {
        var stage = new Presenter
        {
            Execute = async (_, _, token) =>
            {
                await Task.Delay(-1, token);
                return StoryStepOutcome.Continue;
            }
        };
        using var player = new StoryPlayer(stage);
        var result = await player.Play(Sequence(Say(timeout: .02), End())).Completion;
        Check(result.Status == StoryStatus.Failed && result.Error is TimeoutException && stage.CloseCount == 1, "Timeout result");
    }

    private static async Task NoncooperativeTimeout()
    {
        var held = Gate<StoryStepOutcome>();
        var stage = new Presenter { Execute = (_, _, _) => held.Task };
        using var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(Say(timeout: .01), End()));
        await Task.Delay(40);
        Check(player.IsPlaying && !session.Completion.IsCompleted, "Timeout doesn't release ownership of ignored operation");
        held.SetResult(StoryStepOutcome.Continue);
        var result = await session.Completion;
        Check(result.Status == StoryStatus.Failed && result.Error is TimeoutException, "Late successful return cannot conceal timeout");
    }

    private static async Task InvalidChoice()
    {
        var stage = new Presenter { Execute = (_, _, _) => Task.FromResult(StoryStepOutcome.Choose("old-choice")) };
        var commands = new Commands();
        using var player = new StoryPlayer(stage, commands);
        var result = await player.Play(Sequence(new StoryNode("choice", StoryNodeKind.Choice, choices: new[] { new StoryChoice("valid", "option", "command") }), new StoryNode("command", StoryNodeKind.Command, commandId: "reward"), End())).Completion;
        Check(result.Status == StoryStatus.Failed && commands.Calls == 0, "Foreign selection rejected");
    }

    private static async Task AdvanceEpochs()
    {
        var gates = new Dictionary<long, TaskCompletionSource<StoryStepOutcome>>();
        var contexts = new List<StoryStepContext>();
        var stage = new Presenter();
        stage.Execute = (_, context, _) =>
        {
            contexts.Add(context);
            var gate = Gate<StoryStepOutcome>();
            gates.Add(context.StepVersion, gate);
            return gate.Task;
        };
        stage.Advanced = context =>
        {
            if (stage.Current != null && stage.Current.SessionId == context.SessionId && stage.Current.StepVersion == context.StepVersion)
            {
                gates[context.StepVersion].TrySetResult(StoryStepOutcome.Continue);
            }
        };
        using var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(Say("a"), Say("b"), End()));
        Check(session.Advance(), "Advance first line");
        await Until(() => contexts.Count == 2);
        stage.Advance(contexts[0]);
        Check(!gates[contexts[1].StepVersion].Task.IsCompleted, "Late old callback cannot complete new node");
        session.Advance();
        Check((await session.Completion).Status == StoryStatus.Completed, "Second line advanced");
    }

    private static async Task Resume()
    {
        var commands = new Commands();
        var stage = new Presenter();
        using var player = new StoryPlayer(stage, commands);
        var sequence = Sequence(new StoryNode("command", StoryNodeKind.Command, nextId: "say", commandId: "reward"), Say(next: "end"), End());
        var checkpoint = new StoryCheckpoint("intro", "1", "say");
        var result = await player.Play(sequence, checkpoint: checkpoint).Completion;
        Check(result.Status == StoryStatus.Completed && commands.Calls == 0, "Resume by stable node ID");
        Throws<ArgumentException>(() => player.Play(sequence, checkpoint: new StoryCheckpoint("intro", "2", "say")));
        Throws<ArgumentException>(() => player.Play(sequence, checkpoint: new StoryCheckpoint("intro", "1", "missing")));
        Throws<ArgumentException>(() => player.Play(sequence, checkpoint: new StoryCheckpoint("other", "1", "say")));
    }

    private static async Task CheckpointHistory()
    {
        var stage = new Presenter { Execute = (node, _, _) => Task.FromResult(node.Kind == StoryNodeKind.Choice ? StoryStepOutcome.Choose("yes") : StoryStepOutcome.Continue) };
        var commands = new Commands();
        using var player = new StoryPlayer(stage, commands);
        var sequence = Sequence(new StoryNode("choice", StoryNodeKind.Choice, choices: new[] { new StoryChoice("yes", "yes", "reward") }), new StoryNode("reward", StoryNodeKind.Command, commandId: "reward"), End());
        var first = await player.Play(sequence).Completion;
        var identity = commands.Context.CommandInstanceId;
        var checkpoint = new StoryCheckpoint(sequence.Id, sequence.Version, "reward", first.Selections);
        var restored = await player.Play(sequence, checkpoint: checkpoint).Completion;
        Check(restored.Selections.Count == 1 && commands.Context.CommandInstanceId == identity, "Project can scope stable content key to its logical quest/run");
        Throws<ArgumentException>(() => player.Play(sequence, checkpoint: new StoryCheckpoint(sequence.Id, sequence.Version, "reward", new[] { new StorySelection("choice", "unknown") })));
    }

    private static Task GraphValidation()
    {
        Throws<ArgumentException>(() => Sequence(Say(next: "missing"), End()));
        Throws<ArgumentException>(() => Sequence(End(), Say("unused", "end")));
        Throws<ArgumentException>(() => Sequence(new StoryNode("spin", StoryNodeKind.Command, nextId: "spin", commandId: "nop")));
        Throws<ArgumentException>(() => Sequence(new StoryNode("spin", StoryNodeKind.Wait, nextId: "spin", durationSeconds: 0)));
        Throws<ArgumentException>(() => Sequence(End("same"), End("same")));
        Throws<ArgumentException>(() => Sequence(Say()));
        Throws<ArgumentException>(() => new StoryNode("choice", StoryNodeKind.Choice));
        Throws<ArgumentOutOfRangeException>(() => new StoryNode("wait", StoryNodeKind.Wait, durationSeconds: double.NaN));
        Throws<ArgumentOutOfRangeException>(() => new StoryNode("wait", StoryNodeKind.Wait, timeoutSeconds: double.PositiveInfinity));
        return Task.CompletedTask;
    }

    private static async Task ImmutableInputs()
    {
        var options = new List<StoryChoice> { new StoryChoice("yes", "yes", "end") };
        var args = new Dictionary<string, string> { ["amount"] = "1" };
        var choice = new StoryNode("choice", StoryNodeKind.Choice, choices: options, parameters: args);
        var nodes = new List<StoryNode> { choice, End() };
        var sequence = Sequence(nodes.ToArray());
        options.Clear();
        args["amount"] = "999";
        nodes.Clear();
        Check(sequence.Nodes.Count == 2 && choice.Choices.Count == 1 && choice.Parameters["amount"] == "1", "Immutable snapshots");
        var stage = new Presenter { Execute = (_, _, _) => Task.FromResult(StoryStepOutcome.Choose("yes")) };
        using var player = new StoryPlayer(stage);
        Check((await player.Play(sequence).Completion).Status == StoryStatus.Completed, "Snapshots remain playable");
    }

    private static async Task StepBudget()
    {
        var stage = new Presenter();
        using var player = new StoryPlayer(stage, maximumSteps: 5);
        var result = await player.Play(Sequence(Say("loop", "loop"))).Completion;
        Check(result.Status == StoryStatus.Failed && result.Error.Message.Contains("maximum step"), "Immediate boundary mock cannot runaway");
    }

    private static async Task CancellationCallbackFailure()
    {
        var entered = Gate<bool>();
        var stage = new Presenter
        {
            Execute = async (_, _, token) =>
            {
                using var registration = token.Register(() => throw new InvalidOperationException("callback"));
                entered.SetResult(true);
                await Task.Delay(-1, token);
                return StoryStepOutcome.Continue;
            }
        };
        using var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(Say(), End()));
        await entered.Task;
        session.Cancel();
        var result = await session.Completion;
        Check(result.Status == StoryStatus.Failed && result.Error != null && stage.CloseCount == 1, "Callback error captured after cancellation delivery");
    }

    private static async Task Dispose()
    {
        var entered = Gate<bool>();
        var closing = Gate<bool>();
        var stage = new Presenter
        {
            Execute = async (_, _, token) =>
            {
                entered.SetResult(true);
                await Task.Delay(-1, token);
                return StoryStepOutcome.Continue;
            },
            Close = _ => closing.Task
        };
        var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(Say(), End()));
        await entered.Task;
        var disposing = player.DisposeAsync().AsTask();
        await Until(() => stage.CloseCount == 1);
        Check(!disposing.IsCompleted && player.IsPlaying, "Await cleanup");
        Throws<ObjectDisposedException>(() => player.Play(Sequence(End())));
        closing.SetResult(true);
        await disposing;
        Check((await session.Completion).Status == StoryStatus.Cancelled && !player.IsPlaying, "Disposed after close");
    }

    private static async Task InitiallyCancelled()
    {
        var stage = new Presenter();
        using var player = new StoryPlayer(stage);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var result = await player.Play(Sequence(End()), cancel.Token).Completion;
        Check(result.Status == StoryStatus.Cancelled && stage.Calls.Count == 0 && !player.IsPlaying, "No presentation owned");
    }

    private static async Task CancelDuringClose()
    {
        var closing = Gate<bool>();
        var stage = new Presenter { Close = _ => closing.Task };
        using var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(End()));
        Check(session.Cancel(), "Cancellation remains meaningful until cleanup completes");
        closing.SetResult(true);
        Check((await session.Completion).Status == StoryStatus.Cancelled, "No false terminal success during close");
    }

    private static async Task CommandErrors()
    {
        var stage = new Presenter();
        var commands = new Commands { Execute = (_, _, _, _) => throw new InvalidOperationException("project side effect rejected") };
        using var player = new StoryPlayer(stage, commands);
        var sequence = Sequence(new StoryNode("command", StoryNodeKind.Command, commandId: "save", timeoutSeconds: .01), End());
        var result = await player.Play(sequence).Completion;
        Check(result.Status == StoryStatus.Failed && stage.CloseCount == 1, "Command failure cleans presentation");
        commands.Execute = async (_, _, _, token) => await Task.Delay(-1, token);
        result = await player.Play(sequence).Completion;
        Check(result.Status == StoryStatus.Failed && result.Error is TimeoutException, "Command timeout cannot reach End");
    }

    private static async Task CancelAndCloseFailure()
    {
        var closing = Gate<bool>();
        var stage = new Presenter { Close = _ => closing.Task };
        using var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(End()));
        session.Cancel();
        closing.SetException(new InvalidOperationException("release failed"));
        var result = await session.Completion;
        Check(result.Status == StoryStatus.Failed && result.Error.Message == "release failed", "Cleanup errors remain visible after cancellation");
    }

    private static async Task TimeoutCallbackFailure()
    {
        var pending = new TaskCompletionSource<StoryStepOutcome>();
        CancellationTokenRegistration registration = default;
        var stage = new Presenter
        {
            Execute = (_, _, token) =>
            {
                registration = token.Register(() =>
                {
                    pending.TrySetCanceled(token);
                    throw new InvalidOperationException("timer callback failed");
                });
                return pending.Task;
            }
        };
        using var player = new StoryPlayer(stage);
        var result = await player.Play(Sequence(Say(timeout: .02), End())).Completion;
        registration.Dispose();
        Check(result.Status == StoryStatus.Failed && result.Error is AggregateException, "Timer callback error reaches result");
        var errors = ((AggregateException)result.Error).Flatten().InnerExceptions;
        Check(errors.Any(error => error is TimeoutException) && errors.Any(error => error.Message == "timer callback failed"), "Both timeout and failing delivery preserved");
        Check(stage.CloseCount == 1 && !player.IsPlaying, "Timer delivery ends before release");
    }

    private static async Task ReentrantAdvanceFailure()
    {
        var pending = new TaskCompletionSource<StoryStepOutcome>();
        var stage = new Presenter { Execute = (_, _, _) => pending.Task };
        using var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(Say(), End()));
        stage.Advanced = _ =>
        {
            pending.SetResult(StoryStepOutcome.Continue);
            Check(player.IsPlaying && !session.Completion.IsCompleted && stage.CloseCount == 0, "Inline continuation cannot release in-flight controls");
            Throws<StoryBusyException>(() => player.Play(Sequence(End())));
            throw new InvalidOperationException("Advance failed after completing line");
        };
        Check(!session.Advance(), "Throwing presenter control fails");
        var result = await session.Completion;
        Check(result.Status == StoryStatus.Failed && result.Error.Message.Contains("Advance failed"), "Inline error cannot become Completed");
        Check(stage.CloseCount == 1 && !player.IsPlaying, "Close follows the finished control");
    }

    private static async Task SkipTransition()
    {
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var line = new TaskCompletionSource<StoryStepOutcome>();
            var command = Gate<bool>();
            var stage = new Presenter { Execute = (_, _, _) => line.Task };
            var commands = new Commands { Execute = (_, _, _, _) => command.Task };
            using var player = new StoryPlayer(stage, commands);
            var session = player.Play(Sequence(Say(), new StoryNode("command", StoryNodeKind.Command, commandId: "critical", canSkip: false), End()));
            using var start = new Barrier(2);
            bool skipped = false;
            await Task.WhenAll(
                Task.Run(() =>
                {
                    start.SignalAndWait();
                    skipped = session.Skip();
                }),
                Task.Run(() =>
                {
                    start.SignalAndWait();
                    line.SetResult(StoryStepOutcome.Continue);
                }));
            Check(!skipped || commands.Calls == 0, "Accepted skip cannot dispatch later unskippable command");
            if (!skipped)
            {
                Check(commands.Calls == 1, "Unskippable command won transition");
                session.Cancel();
            }
            command.TrySetResult(true);
            Check((await session.Completion).Status == (skipped ? StoryStatus.Skipped : StoryStatus.Cancelled), "Transition terminal reason is stable");
        }
    }

    private static async Task SkipDuringOpen()
    {
        var opening = Gate<bool>();
        var stage = new Presenter { Open = (_, _, _) => opening.Task };
        using var player = new StoryPlayer(stage);
        var session = player.Play(Sequence(Say(canSkip: false), End()));
        Check(!session.Skip() && !session.Advance(), "Pending unskippable entry cannot be skipped or advanced");
        session.Cancel();
        opening.SetResult(true);
        Check((await session.Completion).Status == StoryStatus.Cancelled, "Caller cancel works while loading");
    }

    private static async Task Until(Func<bool> predicate)
    {
        for (int count = 0; count < 1000 && !predicate(); count++)
        {
            await Task.Delay(1);
        }
        Check(predicate(), "Timed out waiting for scenario state");
    }

    private static void Equal(IEnumerable<string> values, params string[] expected) => Check(values.SequenceEqual(expected), "Unexpected execution order: " + string.Join(",", values));
    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }

    private sealed class Presenter : IStoryPresenter
    {
        internal readonly List<string> Calls = new List<string>();
        internal Func<StorySequence, Guid, CancellationToken, Task> Open = (_, _, _) => Task.CompletedTask;
        internal Func<StoryNode, StoryStepContext, CancellationToken, Task<StoryStepOutcome>> Execute = (_, _, _) => Task.FromResult(StoryStepOutcome.Continue);
        internal Func<Guid, Task> Close = _ => Task.CompletedTask;
        internal Action<StoryStepContext> Advanced = _ =>
        {
        };
        internal StoryStepContext Current;
        internal int CloseCount;

        public Task OpenAsync(StorySequence sequence, Guid id, CancellationToken token)
        {
            Calls.Add("open");
            return Open(sequence, id, token);
        }

        public Task<StoryStepOutcome> ExecuteAsync(StoryNode node, StoryStepContext context, CancellationToken token)
        {
            Current = context;
            Calls.Add(node.Id);
            return Execute(node, context, token);
        }

        public void Advance(StoryStepContext context) => Advanced(context);

        public Task CloseAsync(Guid id)
        {
            Calls.Add("close");
            CloseCount++;
            Current = null;
            return Close(id);
        }
    }

    private sealed class Commands : IStoryCommands
    {
        internal int Calls;
        internal StoryStepContext Context;
        internal Func<string, IReadOnlyDictionary<string, string>, StoryStepContext, CancellationToken, Task> Execute = (_, _, _, _) => Task.CompletedTask;
        public Task ExecuteAsync(string commandId, IReadOnlyDictionary<string, string> parameters, StoryStepContext context, CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            return Execute(commandId, parameters, context, cancellationToken);
        }
    }

    private sealed class Clock : IStoryClock
    {
        internal double Seconds;
        public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Seconds += duration.TotalSeconds;
            return Task.CompletedTask;
        }
    }
}

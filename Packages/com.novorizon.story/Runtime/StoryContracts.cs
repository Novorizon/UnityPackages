using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Story
{
    public readonly struct StoryStepOutcome
    {
        public string ChoiceId
        {
            get;
        }
        private StoryStepOutcome(string choiceId)
        {
            ChoiceId = choiceId;
        }
        public static StoryStepOutcome Continue => default;
        public static StoryStepOutcome Choose(string choiceId) => new StoryStepOutcome(StoryValidation.Required(choiceId, nameof(choiceId)));
    }

    public interface IStoryPresenter
    {
        // Start and resume on the caller's synchronization context. Cancellation may arrive
        // on a worker; adapters must marshal Unity work and reject stale session/step IDs.
        Task OpenAsync(StorySequence sequence, Guid sessionId, CancellationToken cancellationToken);
        Task<StoryStepOutcome> ExecuteAsync(StoryNode node, StoryStepContext context, CancellationToken cancellationToken);
        void Advance(StoryStepContext context);
        // Retire callbacks/media and release owned assets before returning. Close is not
        // cancelled; the player remains Busy until cleanup and outstanding Advance calls end.
        Task CloseAsync(Guid sessionId);
    }

    public interface IStoryCommands
    {
        // The project owns side effects and idempotency. CommandInstanceId identifies
        // content; scope it with the business logical-run/quest ID for repeatable rewards.
        Task ExecuteAsync(string commandId, IReadOnlyDictionary<string, string> parameters, StoryStepContext context, CancellationToken cancellationToken);
    }

    public interface IStoryClock
    {
        Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
    }

    public sealed class StoryStepContext
    {
        public Guid SessionId
        {
            get;
        }
        public long StepVersion
        {
            get;
        }
        public string SequenceId
        {
            get;
        }
        public string SequenceVersion
        {
            get;
        }
        public string NodeId
        {
            get;
        }
        public string CommandInstanceId
        {
            get;
        }
        public IReadOnlyList<StorySelection> Selections
        {
            get;
        }

        internal StoryStepContext(Guid sessionId, long stepVersion, StorySequence sequence, string nodeId, IEnumerable<StorySelection> selections)
        {
            SessionId = sessionId;
            StepVersion = stepVersion;
            SequenceId = sequence.Id;
            SequenceVersion = sequence.Version;
            NodeId = nodeId;
            CommandInstanceId = Uri.EscapeDataString(sequence.Id) + "/" + Uri.EscapeDataString(sequence.Version) + "/" + Uri.EscapeDataString(nodeId);
            Selections = Array.AsReadOnly(selections.ToArray());
        }
    }

    public sealed class StorySelection
    {
        public string NodeId
        {
            get;
        }
        public string ChoiceId
        {
            get;
        }
        public StorySelection(string nodeId, string choiceId)
        {
            NodeId = StoryValidation.Required(nodeId, nameof(nodeId));
            ChoiceId = StoryValidation.Required(choiceId, nameof(choiceId));
        }
    }

    public sealed class StoryCheckpoint
    {
        // Stable IDs before the current node. Restoring may re-execute that node, including
        // a Command. Map these immutable properties to a project serialization DTO.
        public string SequenceId
        {
            get;
        }
        public string SequenceVersion
        {
            get;
        }
        public string NodeId
        {
            get;
        }
        public IReadOnlyList<StorySelection> Selections
        {
            get;
        }

        public StoryCheckpoint(string sequenceId, string sequenceVersion, string nodeId, IEnumerable<StorySelection> selections = null)
        {
            SequenceId = StoryValidation.Required(sequenceId, nameof(sequenceId));
            SequenceVersion = StoryValidation.Required(sequenceVersion, nameof(sequenceVersion));
            NodeId = StoryValidation.Required(nodeId, nameof(nodeId));
            var values = (selections ?? Array.Empty<StorySelection>()).ToArray();
            if (values.Any(value => value == null))
            {
                throw new ArgumentException("Checkpoint selections cannot contain null.", nameof(selections));
            }
            Selections = Array.AsReadOnly(values);
        }

        internal void Validate(StorySequence sequence)
        {
            if (sequence.Id != SequenceId || sequence.Version != SequenceVersion || !sequence.ContainsNode(NodeId))
            {
                throw new ArgumentException("Checkpoint sequence/version/node does not match the selected content.");
            }
            foreach (var selection in Selections)
            {
                var node = sequence.GetNode(selection.NodeId);
                if (node.Kind != StoryNodeKind.Choice || !node.Choices.Any(choice => choice.Id == selection.ChoiceId))
                {
                    throw new ArgumentException("Checkpoint includes an unknown choice.");
                }
            }
        }
    }

    public enum StoryStatus
    {
        Completed,
        Skipped,
        Cancelled,
        Failed
    }

    public sealed class StoryResult
    {
        public StoryStatus Status
        {
            get;
        }
        public string SequenceId
        {
            get;
        }
        public string SequenceVersion
        {
            get;
        }
        public string NodeId
        {
            get;
        }
        public IReadOnlyList<StorySelection> Selections
        {
            get;
        }
        public Exception Error
        {
            get;
        }
        public StoryCheckpoint Checkpoint
        {
            get;
        }

        internal StoryResult(StoryStatus status, StorySequence sequence, StoryCheckpoint checkpoint, Exception error)
        {
            Status = status;
            SequenceId = sequence.Id;
            SequenceVersion = sequence.Version;
            NodeId = checkpoint.NodeId;
            Selections = checkpoint.Selections;
            Error = error;
            Checkpoint = checkpoint;
        }
    }

    public sealed class StoryBusyException : InvalidOperationException
    {
        public StoryBusyException() : base("This story player already has an active session, including presentation cleanup.")
        {
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Game.Framework.Story
{
    /// <summary>剧情步骤对应的Outcome。携带操作结束时的状态和相关数据。</summary>
    public readonly struct StoryStepOutcome
    {
        public string ChoiceId
        {
            get;
        }
        /// <summary>使用调用方提供的choiceId创建 StoryStepOutcome。</summary>
        private StoryStepOutcome(string choiceId)
        {
            ChoiceId = choiceId;
        }
        public static StoryStepOutcome Continue => default;
        /// <summary>选择剧情步骤 Outcome。</summary>
        public static StoryStepOutcome Choose(string choiceId) => new StoryStepOutcome(StoryValidation.Required(choiceId, nameof(choiceId)));
    }

    /// <summary>定义剧情演出器的调用契约。</summary>
    public interface IStoryPresenter
    {
        // Start and resume on the caller's synchronization context. Cancellation may arrive
        // on a worker; adapters must marshal Unity work and reject stale session/step IDs.
        /// <summary>异步打开剧情演出器。</summary>
        Task OpenAsync(StorySequence sequence, Guid sessionId, CancellationToken cancellationToken);
        /// <summary>异步执行当前操作。</summary>
        Task<StoryStepOutcome> ExecuteAsync(StoryNode node, StoryStepContext context, CancellationToken cancellationToken);
        /// <summary>推进剧情演出器。</summary>
        void Advance(StoryStepContext context);
        // Retire callbacks/media and release owned assets before returning. Close is not
        // cancelled; the player remains Busy until cleanup and outstanding Advance calls end.
        /// <summary>异步关闭剧情演出器。</summary>
        Task CloseAsync(Guid sessionId);
    }

    /// <summary>定义剧情指令的调用契约。</summary>
    public interface IStoryCommands
    {
        // The project owns side effects and idempotency. CommandInstanceId identifies
        // content; scope it with the business logical-run/quest ID for repeatable rewards.
        /// <summary>异步执行当前操作。</summary>
        Task ExecuteAsync(string commandId, IReadOnlyDictionary<string, string> parameters, StoryStepContext context, CancellationToken cancellationToken);
    }

    /// <summary>定义剧情时钟的调用契约。</summary>
    public interface IStoryClock
    {
        /// <summary>根据传入参数取得DelayAsync。</summary>
        Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
    }

    /// <summary>剧情步骤对应的上下文。携带一次操作所需的上下文。</summary>
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

        /// <summary>使用调用方提供的sessionId、stepVersion、sequence创建 StoryStepContext。</summary>
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

    /// <summary>提供剧情选择结果的实现。</summary>
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
        /// <summary>使用调用方提供的nodeId、choiceId创建 StorySelection。</summary>
        public StorySelection(string nodeId, string choiceId)
        {
            NodeId = StoryValidation.Required(nodeId, nameof(nodeId));
            ChoiceId = StoryValidation.Required(choiceId, nameof(choiceId));
        }
    }

    /// <summary>提供剧情检查点的实现。</summary>
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

        /// <summary>使用调用方提供的sequenceId、sequenceVersion、nodeId创建 StoryCheckpoint。</summary>
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

        /// <summary>验证传入的 sequence。</summary>
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

    /// <summary>定义剧情状态可选的枚举值。</summary>
    public enum StoryStatus
    {
        Completed,
        Skipped,
        Cancelled,
        Failed
    }

    /// <summary>剧情对应的结果。携带操作执行结果和相关数据。</summary>
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

        /// <summary>使用调用方提供的status、sequence、checkpoint创建 StoryResult。</summary>
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

    /// <summary>剧情 Busy对应的异常。报告此操作特有的异常情况。</summary>
    public sealed class StoryBusyException : InvalidOperationException
    {
        /// <summary>创建 StoryBusyException 并采用默认配置。</summary>
        public StoryBusyException() : base("This story player already has an active session, including presentation cleanup.")
        {
        }
    }
}

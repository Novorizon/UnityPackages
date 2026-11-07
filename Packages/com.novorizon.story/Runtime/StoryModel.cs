using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.Framework.Story
{
    public enum StoryNodeKind
    {
        Say,
        Choice,
        SetPortrait,
        SetBackground,
        PlayVideo,
        Wait,
        Command,
        End
    }

    public sealed class StoryChoice
    {
        public string Id
        {
            get;
        }
        public string TextKey
        {
            get;
        }
        public string TargetNodeId
        {
            get;
        }

        public StoryChoice(string id, string textKey, string targetNodeId)
        {
            Id = StoryValidation.Required(id, nameof(id));
            TextKey = StoryValidation.Required(textKey, nameof(textKey));
            TargetNodeId = StoryValidation.Required(targetNodeId, nameof(targetNodeId));
        }
    }

    public sealed class StoryNode
    {
        public string Id
        {
            get;
        }
        public StoryNodeKind Kind
        {
            get;
        }
        public string NextId
        {
            get;
        }
        public string TextKey
        {
            get;
        }
        public string SpeakerKey
        {
            get;
        }
        public string ResourceId
        {
            get;
        }
        public string SlotId
        {
            get;
        }
        public IReadOnlyList<StoryChoice> Choices
        {
            get;
        }
        public double DurationSeconds
        {
            get;
        }
        public bool CanSkip
        {
            get;
        }
        public string CommandId
        {
            get;
        }
        public IReadOnlyDictionary<string, string> Parameters
        {
            get;
        }
        public double TimeoutSeconds
        {
            get;
        }

        public StoryNode(
            string id,
            StoryNodeKind kind,
            string nextId = null,
            string textKey = null,
            string speakerKey = null,
            string resourceId = null,
            string slotId = null,
            IEnumerable<StoryChoice> choices = null,
            double durationSeconds = 0,
            bool canSkip = true,
            string commandId = null,
            IReadOnlyDictionary<string, string> parameters = null,
            double timeoutSeconds = 0)
        {
            Id = StoryValidation.Required(id, nameof(id));
            if (!Enum.IsDefined(typeof(StoryNodeKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            StoryValidation.Seconds(durationSeconds, nameof(durationSeconds));
            StoryValidation.Seconds(timeoutSeconds, nameof(timeoutSeconds));
            Kind = kind;
            NextId = string.IsNullOrEmpty(nextId) ? null : StoryValidation.Required(nextId, nameof(nextId));
            TextKey = textKey;
            SpeakerKey = speakerKey;
            ResourceId = resourceId;
            SlotId = slotId;
            DurationSeconds = durationSeconds;
            CanSkip = canSkip;
            CommandId = commandId;
            TimeoutSeconds = timeoutSeconds;
            var options = (choices ?? Array.Empty<StoryChoice>()).ToArray();
            if (options.Any(choice => choice == null) || options.Select(choice => choice.Id).Distinct(StringComparer.Ordinal).Count() != options.Length)
            {
                throw new ArgumentException("Choices must be nonnull with unique IDs.", nameof(choices));
            }
            Choices = Array.AsReadOnly(options);
            var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
            if (parameters != null)
            {
                foreach (var pair in parameters)
                {
                    arguments.Add(StoryValidation.Required(pair.Key, nameof(parameters)), pair.Value ?? string.Empty);
                }
            }
            Parameters = new ReadOnlyDictionary<string, string>(arguments);
            if (kind == StoryNodeKind.Say)
            {
                StoryValidation.Required(textKey, nameof(textKey));
            }
            if (kind == StoryNodeKind.Choice && options.Length == 0)
            {
                throw new ArgumentException("Choice nodes require at least one option.", nameof(choices));
            }
            if (kind != StoryNodeKind.Choice && options.Length != 0)
            {
                throw new ArgumentException("Only Choice nodes can have options.", nameof(choices));
            }
            if (kind == StoryNodeKind.SetPortrait)
            {
                StoryValidation.Required(slotId, nameof(slotId));
            }
            if (kind == StoryNodeKind.PlayVideo)
            {
                StoryValidation.Required(resourceId, nameof(resourceId));
            }
            if (kind == StoryNodeKind.Command)
            {
                StoryValidation.Required(commandId, nameof(commandId));
            }
            if ((kind == StoryNodeKind.End || kind == StoryNodeKind.Choice) && NextId != null)
            {
                throw new ArgumentException("End/Choice nodes cannot have a separate NextId.", nameof(nextId));
            }
        }
    }

    public sealed class StorySequence
    {
        private readonly Dictionary<string, StoryNode> byId;
        private readonly Dictionary<string, string> nextIds;
        public string Id
        {
            get;
        }
        public string Version
        {
            get;
        }
        public string EntryNodeId
        {
            get;
        }
        public IReadOnlyList<StoryNode> Nodes
        {
            get;
        }

        public StorySequence(string id, string version, IEnumerable<StoryNode> nodes, string entryNodeId = null)
        {
            Id = StoryValidation.Required(id, nameof(id));
            Version = StoryValidation.Required(version, nameof(version));
            var entries = (nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray();
            if (entries.Length == 0 || entries.Any(node => node == null))
            {
                throw new ArgumentException("A sequence requires nonnull nodes.", nameof(nodes));
            }
            byId = new Dictionary<string, StoryNode>(StringComparer.Ordinal);
            nextIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var node in entries)
            {
                if (byId.ContainsKey(node.Id))
                {
                    throw new ArgumentException("Duplicate story node: " + node.Id, nameof(nodes));
                }
                byId.Add(node.Id, node);
            }
            EntryNodeId = entryNodeId ?? entries[0].Id;
            if (!byId.ContainsKey(EntryNodeId))
            {
                throw new ArgumentException("Entry node does not exist.", nameof(entryNodeId));
            }
            for (int index = 0; index < entries.Length; index++)
            {
                var node = entries[index];
                if (node.Kind == StoryNodeKind.End || node.Kind == StoryNodeKind.Choice)
                {
                    foreach (var choice in node.Choices)
                    {
                        RequireTarget(choice.TargetNodeId);
                    }
                    continue;
                }
                string next = node.NextId ?? (index + 1 < entries.Length ? entries[index + 1].Id : null);
                if (next == null)
                {
                    throw new ArgumentException("A final node must explicitly End: " + node.Id, nameof(nodes));
                }
                RequireTarget(next);
                nextIds.Add(node.Id, next);
            }
            Nodes = Array.AsReadOnly(entries);
            ValidateGraph();
        }

        public bool ContainsNode(string id) => id != null && byId.ContainsKey(id);

        public StoryNode GetNode(string id)
        {
            if (id == null || !byId.TryGetValue(id, out var node))
            {
                throw new ArgumentException("Unknown story node: " + id, nameof(id));
            }
            return node;
        }

        internal string Next(StoryNode node, StoryStepOutcome outcome)
        {
            if (node.Kind == StoryNodeKind.End)
            {
                return null;
            }
            if (node.Kind == StoryNodeKind.Choice)
            {
                foreach (var option in node.Choices)
                {
                    if (option.Id == outcome.ChoiceId)
                    {
                        return option.TargetNodeId;
                    }
                }
                throw new InvalidOperationException("Presenter returned an unknown choice for node " + node.Id + ": " + outcome.ChoiceId);
            }
            if (outcome.ChoiceId != null)
            {
                throw new InvalidOperationException("Only Choice nodes can return a choice outcome.");
            }
            return nextIds[node.Id];
        }

        private void RequireTarget(string id)
        {
            if (!byId.ContainsKey(id))
            {
                throw new ArgumentException("Story target does not exist: " + id);
            }
        }

        private IEnumerable<string> Targets(StoryNode node)
        {
            if (node.Kind == StoryNodeKind.Choice)
            {
                foreach (var choice in node.Choices)
                {
                    yield return choice.TargetNodeId;
                }
            }
            else if (nextIds.TryGetValue(node.Id, out var next))
            {
                yield return next;
            }
        }

        private void ValidateGraph()
        {
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(EntryNodeId);
            while (pending.Count != 0)
            {
                string id = pending.Pop();
                if (!reachable.Add(id))
                {
                    continue;
                }
                foreach (string target in Targets(byId[id]))
                {
                    pending.Push(target);
                }
            }
            if (reachable.Count != byId.Count)
            {
                throw new ArgumentException("The sequence contains unreachable nodes.");
            }
            // Remove guaranteed interaction/time boundaries, then use Kahn's algorithm to
            // detect a remaining cycle without recursive traversal or a large editor stack.
            var immediate = byId.Values.Where(node => !IsBoundary(node)).ToDictionary(node => node.Id, _ => 0, StringComparer.Ordinal);
            foreach (string id in immediate.Keys.ToArray())
            {
                foreach (string target in Targets(byId[id]))
                {
                    if (immediate.ContainsKey(target))
                    {
                        immediate[target]++;
                    }
                }
            }
            var ready = new Queue<string>(immediate.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            int visited = 0;
            while (ready.Count > 0)
            {
                string id = ready.Dequeue();
                visited++;
                foreach (string target in Targets(byId[id]))
                {
                    if (immediate.ContainsKey(target) && --immediate[target] == 0)
                    {
                        ready.Enqueue(target);
                    }
                }
            }
            if (visited != immediate.Count)
            {
                throw new ArgumentException("The sequence contains a cycle without a dialogue, choice, video or positive wait boundary.");
            }
        }

        private static bool IsBoundary(StoryNode node) => node.Kind == StoryNodeKind.Say || node.Kind == StoryNodeKind.Choice || node.Kind == StoryNodeKind.PlayVideo || (node.Kind == StoryNodeKind.Wait && node.DurationSeconds > 0);
    }

    internal static class StoryValidation
    {
        internal static string Required(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("A nonempty ID/key without NUL characters is required.", name);
            }
            return value;
        }

        internal static void Seconds(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > int.MaxValue / 1000d)
            {
                throw new ArgumentOutOfRangeException(name, "Duration must be finite, nonnegative and supported by CancellationTokenSource/Task.Delay.");
            }
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Framework.Story.Unity
{
    /// <summary>提供剧情选项数据的实现。</summary>
    [Serializable]
    public sealed class StoryChoiceData
    {
        public string id;
        public string textKey;
        public string targetNodeId;
    }

    /// <summary>提供剧情参数数据的实现。</summary>
    [Serializable]
    public sealed class StoryParameterData
    {
        public string key;
        public string value;
    }

    /// <summary>提供剧情节点数据的实现。</summary>
    [Serializable]
    public sealed class StoryNodeData
    {
        public string id;
        public StoryNodeKind kind;
        public string nextId;
        public string textKey;
        public string speakerKey;
        public string resourceId;
        public string slotId;
        public StoryChoiceData[] choices = Array.Empty<StoryChoiceData>();
        public double durationSeconds;
        public bool canSkip = true;
        public string commandId;
        public StoryParameterData[] parameters = Array.Empty<StoryParameterData>();
        public double timeoutSeconds;

        /// <summary>构建剧情节点数据。</summary>
        internal StoryNode Build()
        {
            var options = new List<StoryChoice>();
            foreach (var choice in choices ?? Array.Empty<StoryChoiceData>())
            {
                if (choice == null)
                {
                    throw new InvalidOperationException("Story node '" + id + "' has a null choice.");
                }
                options.Add(new StoryChoice(choice.id, choice.textKey, choice.targetNodeId));
            }
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var parameter in parameters ?? Array.Empty<StoryParameterData>())
            {
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.key) || values.ContainsKey(parameter.key))
                {
                    throw new InvalidOperationException("Story node '" + id + "' has an empty or duplicate parameter.");
                }
                values.Add(parameter.key, parameter.value);
            }
            return new StoryNode(
                id,
                kind,
                nextId,
                textKey,
                speakerKey,
                resourceId,
                slotId,
                options,
                durationSeconds,
                canSkip,
                commandId,
                values,
                timeoutSeconds);
        }
    }

    /// <summary>提供剧情序列资源的实现。</summary>
    [CreateAssetMenu(menuName = "Novorizon/Story/Sequence", fileName = "StorySequence")]
    public sealed class StorySequenceAsset : ScriptableObject
    {
        public string sequenceId = "story";
        public string version = "1";
        public string entryNodeId;
        public StoryNodeData[] nodes = Array.Empty<StoryNodeData>();

        /// <summary>Build a validated immutable runtime snapshot before starting playback.</summary>
        /// <remarks>构建剧情序列资源。</remarks>
        public StorySequence Build()
        {
            var result = new List<StoryNode>();
            foreach (var node in nodes ?? Array.Empty<StoryNodeData>())
            {
                if (node == null)
                {
                    throw new InvalidOperationException("Story contains a null node.");
                }
                result.Add(node.Build());
            }
            return new StorySequence(sequenceId, version, result, string.IsNullOrWhiteSpace(entryNodeId) ? null : entryNodeId);
        }
    }

}

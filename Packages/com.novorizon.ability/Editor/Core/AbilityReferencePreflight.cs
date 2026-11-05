using System;
using System.Collections.Generic;
using Game.Ability.Configuration;

namespace Game.Ability.Editor.Core
{
    /// <summary>
    /// A nonrecursive authoring guard, run before the production registry's graph
    /// validation. SCC weights bound simple reference paths conservatively; this is neither
    /// runtime execution depth nor a prohibition on event/interval cycles.
    /// </summary>
    public static class AbilityReferencePreflight
    {
        public const int DefaultMaximumReferencePath = 256;
        public const int MaximumAllowedReferencePath = 1024;
        public const string DiagnosticCode = "EDITOR_REFERENCE_COMPLEXITY";

        public static IReadOnlyList<AbilityEditorDiagnostic> Validate(IEnumerable<JsonAbilityDocument> documents, int maximumReferencePath = DefaultMaximumReferencePath)
        {
            List<KeyValuePair<string, JsonAbilityDocument>> sources = new List<KeyValuePair<string, JsonAbilityDocument>>();
            if (documents != null)
                foreach (JsonAbilityDocument document in documents) sources.Add(new KeyValuePair<string, JsonAbilityDocument>(null, document));
            return ValidateSources(sources, maximumReferencePath);
        }

        public static IReadOnlyList<AbilityEditorDiagnostic> ValidateSources(IEnumerable<KeyValuePair<string, JsonAbilityDocument>> sources, int maximumReferencePath = DefaultMaximumReferencePath)
        {
            Graph graph = new Graph();
            if (sources != null)
                foreach (KeyValuePair<string, JsonAbilityDocument> source in sources) graph.AddDocument(source.Value, source.Key);
            return graph.Validate(maximumReferencePath);
        }

        /// <summary>
        /// Validates already-loaded bundles, including explicit code providers, before any
        /// Registry.LoadProviders/IsValid/ApplyTo call enters production graph validation.
        /// Callers should cache these bundles rather than executing a provider twice.
        /// </summary>
        public static IReadOnlyList<AbilityEditorDiagnostic> ValidateBundles(IEnumerable<AbilityDefinitionBundle> bundles, int maximumReferencePath = DefaultMaximumReferencePath)
        {
            Graph graph = new Graph();
            if (bundles != null)
                foreach (AbilityDefinitionBundle bundle in bundles) graph.AddBundle(bundle);
            return graph.Validate(maximumReferencePath);
        }

        private sealed class Node
        {
            public string Name;
            public string FilePath;
            public bool IsAbility;
            public readonly HashSet<string> ModifierReferences = new HashSet<string>(StringComparer.Ordinal);
        }

        private readonly struct Frame
        {
            public readonly int Node;
            public readonly int NextEdge;
            public Frame(int node, int nextEdge) { Node = node; NextEdge = nextEdge; }
        }

        private sealed class Graph
        {
            private readonly List<Node> nodes = new List<Node>();
            private readonly Dictionary<string, int> indices = new Dictionary<string, int>(StringComparer.Ordinal);

            public void AddDocument(JsonAbilityDocument document, string path)
            {
                if (document == null) return;
                if (document.modifiers != null)
                    foreach (JsonModifier modifier in document.modifiers)
                    {
                        if (modifier == null) continue;
                        Node node = Add(false, modifier.name, path);
                        if (node == null) continue;
                        Reference(node, modifier.auraModifierName);
                        Actions(node, modifier.onCreated); Actions(node, modifier.onRefresh); Actions(node, modifier.onDestroy);
                        Actions(node, modifier.intervalActions); Actions(node, modifier.triggerActions);
                    }
                if (document.abilities != null)
                    foreach (JsonAbility ability in document.abilities)
                    {
                        if (ability == null) continue;
                        Node node = Add(true, ability.name, path);
                        if (node == null) continue;
                        Reference(node, ability.intrinsicModifierName);
                        Actions(node, ability.actions); Actions(node, ability.onProjectileHit);
                        Actions(node, ability.onToggleOn); Actions(node, ability.onToggleOff);
                    }
            }

            public void AddBundle(AbilityDefinitionBundle bundle)
            {
                if (bundle == null) return;
                foreach (ModifierDefinitionRegistration registration in bundle.Modifiers)
                {
                    ModifierDefinition modifier = registration?.Definition;
                    if (modifier == null) continue;
                    Node node = Add(false, modifier.Name, registration.Origin?.SourcePath);
                    if (node == null) continue;
                    Reference(node, modifier.AuraModifierName);
                    Actions(node, modifier.OnCreatedActions); Actions(node, modifier.OnRefreshActions); Actions(node, modifier.OnDestroyActions);
                    Actions(node, modifier.IntervalActions); Actions(node, modifier.TriggerActions);
                }
                foreach (AbilityDefinitionRegistration registration in bundle.Abilities)
                {
                    AbilityDefinition ability = registration?.Definition;
                    if (ability == null) continue;
                    Node node = Add(true, ability.Name, registration.Origin?.SourcePath);
                    if (node == null) continue;
                    Reference(node, ability.IntrinsicModifierName);
                    Actions(node, ability.Actions); Actions(node, ability.OnProjectileHitActions);
                    Actions(node, ability.OnToggleOnActions); Actions(node, ability.OnToggleOffActions);
                }
            }

            public IReadOnlyList<AbilityEditorDiagnostic> Validate(int budget)
            {
                if (budget < 1 || budget > MaximumAllowedReferencePath)
                    throw new ArgumentOutOfRangeException(nameof(budget), "The editor authoring reference budget must be between 1 and " + MaximumAllowedReferencePath + ". It is independent of runtime execution depth.");
                List<AbilityEditorDiagnostic> diagnostics = new List<AbilityEditorDiagnostic>();
                if (nodes.Count == 0) return diagnostics;

                List<int>[] reverse = NewLists(nodes.Count);
                int[][] edges = new int[nodes.Count][];
                for (int index = 0; index < nodes.Count; index++)
                {
                    List<int> targets = new List<int>();
                    foreach (string name in nodes[index].ModifierReferences)
                        if (indices.TryGetValue(Key(false, name), out int target))
                        {
                            targets.Add(target);
                            reverse[target].Add(index);
                        }
                    targets.Sort();
                    edges[index] = targets.ToArray();
                }

                // Kosaraju uses explicit stacks in both passes. No path enumeration occurs;
                // SCC traversal and DAG dynamic programming are O(V + E). Sorting node/edge
                // indices once gives stable diagnostics without recursive or exponential work.
                List<int> finish = FinishingOrder(edges);
                int[] component = NewFilled(nodes.Count, -1);
                List<List<int>> members = new List<List<int>>();
                Stack<int> pending = new Stack<int>();
                for (int order = finish.Count - 1; order >= 0; order--)
                {
                    int root = finish[order];
                    if (component[root] >= 0) continue;
                    int identifier = members.Count;
                    List<int> group = new List<int>();
                    members.Add(group);
                    component[root] = identifier;
                    pending.Push(root);
                    while (pending.Count > 0)
                    {
                        int node = pending.Pop();
                        group.Add(node);
                        foreach (int parent in reverse[node])
                            if (component[parent] < 0) { component[parent] = identifier; pending.Push(parent); }
                    }
                    group.Sort();
                }

                HashSet<int>[] dag = new HashSet<int>[members.Count];
                int[] indegree = new int[members.Count];
                int[] upperBound = new int[members.Count];
                int[] predecessor = NewFilled(members.Count, -1);
                for (int index = 0; index < dag.Length; index++) { dag[index] = new HashSet<int>(); upperBound[index] = members[index].Count; }
                for (int index = 0; index < edges.Length; index++)
                    foreach (int target in edges[index])
                        if (component[index] != component[target] && dag[component[index]].Add(component[target])) indegree[component[target]]++;

                Queue<int> ready = new Queue<int>();
                for (int index = 0; index < dag.Length; index++) if (indegree[index] == 0) ready.Enqueue(index);
                int longest = 0;
                while (ready.Count > 0)
                {
                    int current = ready.Dequeue();
                    if (upperBound[current] > upperBound[longest]) longest = current;
                    foreach (int target in dag[current])
                    {
                        int candidate = upperBound[current] + members[target].Count;
                        if (candidate > upperBound[target]) { upperBound[target] = candidate; predecessor[target] = current; }
                        if (--indegree[target] == 0) ready.Enqueue(target);
                    }
                }
                if (upperBound[longest] <= budget) return diagnostics;

                List<int> path = new List<int>();
                for (int current = longest; current >= 0; current = predecessor[current]) path.Add(current);
                path.Reverse();
                Node source = nodes[members[path[0]][0]];
                List<string> labels = new List<string>();
                for (int index = 0; index < path.Count && index < 12; index++)
                {
                    List<int> group = members[path[index]];
                    Node node = nodes[group[0]];
                    labels.Add((node.IsAbility ? "ability " : "modifier ") + node.Name + (group.Count > 1 ? " [强连通组 " + group.Count + "]" : string.Empty));
                }
                if (path.Count > labels.Count) labels.Add("… (" + path.Count + " groups)");
                diagnostics.Add(new AbilityEditorDiagnostic
                {
                    Severity = AbilityValidationSeverity.Error,
                    Code = DiagnosticCode,
                    FilePath = source.FilePath,
                    ReferenceChain = string.Join(" → ", labels),
                    Message = "编辑器引用复杂度预检：凝聚图路径的保守上界为 " + upperBound[longest] + " 个定义，超过作者预算 " + budget + "。包含技能起点及 Modifier 的生命周期、事件、周期和光环引用；这是静态作者保护，不是运行时调用深度，也不表示事件环本身非法。请拆分引用链或减少强连通组规模。"
                });
                return diagnostics;
            }

            private Node Add(bool ability, string name, string path)
            {
                if (string.IsNullOrEmpty(name)) return null;
                string key = Key(ability, name);
                if (indices.TryGetValue(key, out int index)) return nodes[index];
                Node node = new Node { Name = name, FilePath = path, IsAbility = ability };
                indices.Add(key, nodes.Count);
                nodes.Add(node);
                return node;
            }

            private static string Key(bool ability, string name) => (ability ? "A\0" : "M\0") + name;
            private static void Reference(Node node, string name) { if (!string.IsNullOrEmpty(name)) node.ModifierReferences.Add(name); }

            private static void Actions(Node node, IEnumerable<JsonAction> actions)
            {
                if (actions != null)
                    foreach (JsonAction action in actions)
                        if (action != null && action.actionType == (int)ActionType.AddModifier) Reference(node, action.modifierName);
            }

            private static void Actions(Node node, IEnumerable<ActionDefinition> actions)
            {
                if (actions != null)
                    foreach (ActionDefinition action in actions)
                        if (action != null && action.ActionType == ActionType.AddModifier) Reference(node, action.ModifierName);
            }

            private static List<int>[] NewLists(int count)
            {
                List<int>[] lists = new List<int>[count];
                for (int index = 0; index < count; index++) lists[index] = new List<int>();
                return lists;
            }

            private static int[] NewFilled(int count, int value)
            {
                int[] result = new int[count];
                for (int index = 0; index < count; index++) result[index] = value;
                return result;
            }

            private static List<int> FinishingOrder(int[][] edges)
            {
                List<int> order = new List<int>(edges.Length);
                bool[] visited = new bool[edges.Length];
                Stack<Frame> stack = new Stack<Frame>();
                for (int root = 0; root < edges.Length; root++)
                {
                    if (visited[root]) continue;
                    visited[root] = true;
                    stack.Push(new Frame(root, 0));
                    while (stack.Count > 0)
                    {
                        Frame frame = stack.Pop();
                        if (frame.NextEdge == edges[frame.Node].Length) { order.Add(frame.Node); continue; }
                        stack.Push(new Frame(frame.Node, frame.NextEdge + 1));
                        int target = edges[frame.Node][frame.NextEdge];
                        if (!visited[target]) { visited[target] = true; stack.Push(new Frame(target, 0)); }
                    }
                }
                return order;
            }
        }
    }
}

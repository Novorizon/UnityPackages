using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    public sealed partial class AbilitySystem
    {
        private readonly HashSet<Modifier> auraChildren = new HashSet<Modifier>();
        private readonly Stack<AuraUpdateFrame> auraUpdateFrames = new Stack<AuraUpdateFrame>();
        private readonly Stack<AuraUpdateFrame> availableAuraFrames = new Stack<AuraUpdateFrame>();

        internal void RegisterAuraChild(Modifier modifier)
        {
            if (modifier.IsAuraApplied && modifier.SourceAura != null) auraChildren.Add(modifier);
        }

        internal void UnregisterAuraChild(Modifier modifier)
        {
            auraChildren.Remove(modifier);
        }

        internal bool HasAuraLease(Modifier modifier)
        {
            if (!modifier.IsAuraApplied || auraUpdateFrames.Count == 0) return false;
            AuraUpdateFrame frame = auraUpdateFrames.Peek();
            return frame.Generation == RuntimeGeneration && frame.Covered.Contains(modifier);
        }

        private void BeginAuraUpdate()
        {
            AuraUpdateFrame frame = availableAuraFrames.Count > 0 ? availableAuraFrames.Pop() : new AuraUpdateFrame();
            frame.Generation = RuntimeGeneration;
            auraUpdateFrames.Push(frame);
            if (auraChildren.Count == 0) return;

            List<Modifier> children = RuntimeSnapshotPool<Modifier>.Rent();
            children.AddRange(auraChildren);
            try
            {
                // The world exposes current positions, not motion history. Freeze coverage
                // at this update boundary before modifier/source insertion order can matter.
                foreach (Modifier child in children)
                {
                    if (frame.Generation != RuntimeGeneration) break;
                    using (BeginExecution())
                    {
                        if (!IsModifierRegistered(child) || !IsAuraSourceValid(child.SourceAura)) continue;
                        Modifier source = child.SourceAura;
                        if (!frame.Queries.TryGetValue(source, out TargetQuery query))
                        {
                            query = CreateAuraQuery(source);
                            frame.Queries.Add(source, query);
                        }
                        float radius = source.Definition.AuraRadius;
                        if (!CombatSnapshots.IsFinite(radius) || radius < 0f || !IsFinite(source.Parent.Position) || !IsFinite(child.Parent.Position) || !AbilityGeometry.WithinDistance(source.Parent.Position, child.Parent.Position, radius) || !query.IsValid(child.Parent))
                        {
                            continue;
                        }
                        if (frame.Generation != RuntimeGeneration || !IsModifierRegistered(child) || !IsAuraSourceValid(source)) continue;
                        frame.Covered.Add(child);
                        child.RenewAuraLease();
                    }
                }
            }
            finally { RuntimeSnapshotPool<Modifier>.Release(children); }
        }

        private void ScheduleAuraRefresh(Modifier aura)
        {
            if (!IsAuraSourceValid(aura)) return;
            if (auraUpdateFrames.Count == 0 || auraUpdateFrames.Peek().IsFlushing)
            {
                ApplyAuraRefresh(aura);
                return;
            }

            AuraUpdateFrame frame = auraUpdateFrames.Peek();
            if (frame.Generation != RuntimeGeneration) return;
            if (frame.RefreshCounts.TryGetValue(aura, out int count)) frame.RefreshCounts[aura] = count == int.MaxValue ? count : count + 1;
            else
            {
                frame.RefreshCounts.Add(aura, 1);
                frame.Sources.Add(aura);
            }
        }

        private void FlushAuraUpdates()
        {
            if (auraUpdateFrames.Count == 0) return;
            AuraUpdateFrame frame = auraUpdateFrames.Peek();
            frame.IsFlushing = true;
            try
            {
                for (int sourceIndex = 0; sourceIndex < frame.Sources.Count; sourceIndex++)
                {
                    if (frame.Generation != RuntimeGeneration) return;
                    Modifier source = frame.Sources[sourceIndex];
                    // Each source is an independent execution chain, like its ordinary tick.
                    // Refreshes caused recursively while flushing execute in that same chain.
                    using (BeginExecution())
                    {
                        int count = frame.RefreshCounts[source];
                        for (int i = 0; i < count && IsAuraSourceValid(source); i++)
                        {
                            if (!TryConsumeExecutionBudget("Aura deferred refresh")) break;
                            ApplyAuraRefresh(source);
                        }
                    }
                }
            }
            finally { frame.IsFlushing = false; }
        }

        private void EndAuraUpdate()
        {
            if (auraUpdateFrames.Count == 0) return;
            AuraUpdateFrame frame = auraUpdateFrames.Pop();
            frame.Clear();
            if (availableAuraFrames.Count < 8) availableAuraFrames.Push(frame);
        }

        private void ResetAuraState()
        {
            auraChildren.Clear();
            // Active Update finally blocks still own these frames. Invalidate, but do not
            // remove/reuse them while cleanup callbacks are on the stack.
            foreach (AuraUpdateFrame frame in auraUpdateFrames) frame.Clear();
        }

        private bool IsAuraSourceValid(Modifier aura)
        {
            return IsRuntimeAvailable && aura != null && !aura.IsDestroyed && aura.IsRuntimeCurrent && IsModifierRegistered(aura) && aura.Parent != null && !IsUnitRemoving(aura.Parent) && !IsUnitRemoving(aura.Caster) && (aura.Parent.IsAlive || !aura.Definition.RemoveOnDeath) && !string.IsNullOrEmpty(aura.Definition.AuraModifierName);
        }

        private TargetQuery CreateAuraQuery(Modifier source)
        {
            ModifierDefinition definition = source.Definition;
            return new TargetQuery { Engine = this, Caster = source.Parent, Team = definition.AuraTargetTeam, Types = definition.AuraTargetType, Flags = definition.AuraTargetFlags };
        }

        private void ApplyAuraRefresh(Modifier aura)
        {
            if (!IsAuraSourceValid(aura)) return;
            ModifierDefinition definition = aura.Definition;
            List<IUnit> units = RuntimeSnapshotPool<IUnit>.Rent();
            try
            {
                FindUnits(aura.Parent.Position, definition.AuraRadius, CreateAuraQuery(aura), units);
                long generation = RuntimeGeneration;
                for (int i = 0; i < units.Count; i++)
                {
                    if (generation != RuntimeGeneration || !IsAuraSourceValid(aura)) break;
                    AddModifier(aura.Parent, units[i], aura.Ability, definition.AuraModifierName, new ModifierApplyOptions { Duration = Mathf.Max(0.1f, definition.AuraDuration), IsAura = true, SourceModifier = aura, Level = aura.Level, IncrementStacksOnRefresh = definition.AuraIncrementsStacks });
                }
            }
            finally { RuntimeSnapshotPool<IUnit>.Release(units); }
        }

        private sealed class AuraUpdateFrame
        {
            internal long Generation;
            internal bool IsFlushing;
            internal readonly HashSet<Modifier> Covered = new HashSet<Modifier>();
            internal readonly Dictionary<Modifier, TargetQuery> Queries = new Dictionary<Modifier, TargetQuery>();
            internal readonly Dictionary<Modifier, int> RefreshCounts = new Dictionary<Modifier, int>();
            internal readonly List<Modifier> Sources = new List<Modifier>();

            internal void Clear()
            {
                Generation = -1;
                IsFlushing = false;
                Covered.Clear();
                Queries.Clear();
                RefreshCounts.Clear();
                Sources.Clear();
            }
        }
    }
}

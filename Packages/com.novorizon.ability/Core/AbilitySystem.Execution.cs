using System;
using System.Collections.Generic;

namespace Game.Ability
{
    public sealed partial class AbilitySystem
    {
        private readonly HashSet<int> removedUnitIds = new HashSet<int>();
        private readonly HashSet<int> removingUnitIds = new HashSet<int>();
        private readonly Dictionary<int, IUnit> registeredUnits = new Dictionary<int, IUnit>();
        private int executionDepth;
        private int executionOperations;
        private bool executionAborted;
        private long executionGeneration;

        public int MaxExecutionDepth { get; set; } = 64;
        public int MaxExecutionOperations { get; set; } = 2048;
        public int MaxPeriodicCallbacksPerUpdate { get; set; } = 128;
        public long RuntimeGeneration { get; private set; }
        public bool IsRuntimeAvailable => !IsClearingRuntime && (executionDepth == 0 || executionGeneration == RuntimeGeneration);

        // Removed abilities remain valid historical attribution within their original battle.
        // A foreign engine or a cleared battle must never revive that attribution.
        internal bool IsAbilitySourceValid(Ability ability) => ability == null || ReferenceEquals(ability.Engine, this) && ability.RuntimeGenerationAtCreation == RuntimeGeneration && ability.WasRegistered && !IsUnitRemoving(ability.Owner);

        /// <summary>A binding must explicitly re-register an id after its previous owner left.</summary>
        public bool RegisterUnit(IUnit unit)
        {
            if (unit == null || !IsRuntimeAvailable || removingUnitIds.Contains(unit.EntityId)) return false;
            long generation = RuntimeGeneration;
            if (registeredUnits.TryGetValue(unit.EntityId, out IUnit previous) && !ReferenceEquals(previous, unit)) RemoveUnit(previous);
            if (generation != RuntimeGeneration || IsClearingRuntime) return false;
            removedUnitIds.Remove(unit.EntityId);
            registeredUnits[unit.EntityId] = unit;
            return true;
        }

        public bool IsUnitRemoving(IUnit unit)
        {
            return unit != null && (removedUnitIds.Contains(unit.EntityId) || registeredUnits.TryGetValue(unit.EntityId, out IUnit canonical) && !ReferenceEquals(canonical, unit));
        }

        private static bool IsFinite(UnityEngine.Vector3 value)
        {
            return CombatSnapshots.IsFinite(value.x) && CombatSnapshots.IsFinite(value.y) && CombatSnapshots.IsFinite(value.z);
        }

        internal ExecutionScope BeginExecution()
        {
            if (executionDepth == 0)
            {
                executionOperations = 0;
                executionAborted = false;
                executionGeneration = RuntimeGeneration;
            }
            executionDepth++;
            return new ExecutionScope(this);
        }

        internal bool TryConsumeExecutionBudget(string operation, bool countOperation = true)
        {
            if (executionAborted) return false;
            if (countOperation) executionOperations++;
            if (executionDepth <= Math.Max(1, MaxExecutionDepth) && executionOperations <= Math.Max(1, MaxExecutionOperations)) return true;
            executionAborted = true;
            RaiseEvent(new RuntimeEvent
            {
                EventType = RuntimeEventType.ExecutionAborted,
                Message = "Execution budget exceeded at " + operation + "; depth=" + executionDepth + ", operations=" + executionOperations + "."
            });
            return false;
        }

        public bool HasAbilityScript(string name) => name != null && abilityFactories.ContainsKey(name);
        public bool HasModifierScript(string name) => name != null && modifierFactories.ContainsKey(name);

        /// <summary>Publishing definitions is atomic and separate from runtime initialization.</summary>
        public bool ReplaceDefinitions(IEnumerable<AbilityDefinition> abilities, IEnumerable<ModifierDefinition> modifierValues)
        {
            if (IsClearingRuntime || abilitiesByUnit.Count != 0 || modifiers.Count != 0 || projectiles.Count != 0 || thinkers.Count != 0) return false;
            var nextAbilities = new Dictionary<string, AbilityDefinition>(StringComparer.Ordinal);
            var nextModifiers = new Dictionary<string, ModifierDefinition>(StringComparer.Ordinal);
            if (abilities != null)
            {
                foreach (AbilityDefinition definition in abilities)
                {
                    if (definition == null || string.IsNullOrWhiteSpace(definition.Name) || nextAbilities.ContainsKey(definition.Name)) return false;
                    nextAbilities.Add(definition.Name, definition);
                }
            }
            if (modifierValues != null)
            {
                foreach (ModifierDefinition definition in modifierValues)
                {
                    if (definition == null || string.IsNullOrWhiteSpace(definition.Name) || nextModifiers.ContainsKey(definition.Name)) return false;
                    nextModifiers.Add(definition.Name, definition);
                }
            }
            abilityDefinitions.Clear();
            modifierDefinitions.Clear();
            foreach (var pair in nextAbilities) abilityDefinitions.Add(pair.Key, pair.Value);
            foreach (var pair in nextModifiers) modifierDefinitions.Add(pair.Key, pair.Value);
            return true;
        }

        internal struct ExecutionScope : IDisposable
        {
            private AbilitySystem owner;
            public ExecutionScope(AbilitySystem owner) { this.owner = owner; }
            public void Dispose()
            {
                if (owner == null) return;
                owner.executionDepth--;
                owner = null;
            }
        }
    }
}

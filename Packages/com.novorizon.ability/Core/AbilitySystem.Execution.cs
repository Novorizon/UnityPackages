using System;
using System.Collections.Generic;

namespace Game.Ability
{
    /// <summary>提供技能系统的实现。</summary>
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
        /// <summary>判断是否为技能来源有效。</summary>
        internal bool IsAbilitySourceValid(Ability ability) => ability == null || ReferenceEquals(ability.Engine, this) && ability.RuntimeGenerationAtCreation == RuntimeGeneration && ability.WasRegistered && !IsUnitRemoving(ability.Owner);

        /// <summary>A binding must explicitly re-register an id after its previous owner left.</summary>
        /// <remarks>登记单位。</remarks>
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

        /// <summary>判断是否为单位 Removing。</summary>
        public bool IsUnitRemoving(IUnit unit)
        {
            return unit != null && (removedUnitIds.Contains(unit.EntityId) || registeredUnits.TryGetValue(unit.EntityId, out IUnit canonical) && !ReferenceEquals(canonical, unit));
        }

        /// <summary>检查数值是否既非无穷大也非 NaN。</summary>
        private static bool IsFinite(UnityEngine.Vector3 value)
        {
            return CombatSnapshots.IsFinite(value.x) && CombatSnapshots.IsFinite(value.y) && CombatSnapshots.IsFinite(value.z);
        }

        /// <summary>开始执行。</summary>
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

        /// <summary>尝试处理 Consume 执行预算，返回是否成功。</summary>
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

        /// <summary>检查是否存在技能脚本。</summary>
        public bool HasAbilityScript(string name) => name != null && abilityFactories.ContainsKey(name);
        /// <summary>检查是否存在状态修饰器脚本。</summary>
        public bool HasModifierScript(string name) => name != null && modifierFactories.ContainsKey(name);

        /// <summary>Publishing definitions is atomic and separate from runtime initialization.</summary>
        /// <remarks>替换定义。</remarks>
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

        /// <summary>执行对应的作用域。保存作用域内需要统一管理的信息。</summary>
        internal struct ExecutionScope : IDisposable
        {
            private AbilitySystem owner;
            /// <summary>使用调用方提供的owner创建 ExecutionScope。</summary>
            public ExecutionScope(AbilitySystem owner) { this.owner = owner; }
            /// <summary>结束执行作用域对相关资源的持有，并执行该类型的清理流程。</summary>
            public void Dispose()
            {
                if (owner == null) return;
                owner.executionDepth--;
                owner = null;
            }
        }
    }
}

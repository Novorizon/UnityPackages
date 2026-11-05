using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Ability.Configuration;
using UnityEngine;

namespace Game.Ability.Editor.Core
{
    /// <summary>Explicit, bounded input for one disposable editor simulation. These are not production balance settings.</summary>
    public sealed class AbilitySandboxOptions
    {
        public int MaxExecutionDepth = 64;
        public int MaxExecutionOperations = 2048;
        public float SimulationSeconds = 5f;
        public float TickSeconds = 0.05f;
        public int TargetCount = 8;
    }

    public sealed class AbilitySandboxUnitResult
    {
        public int EntityId;
        public int TeamId;
        public float Health;
        public float MaxHealth;
        public UnitState States;
        public float ArmorBonus;
        public float MoveSpeedBonus;
        public float AttackSpeedBonus;
    }

    /// <summary>Value-only evidence for this particular scene and time window; never a proof about every possible event path.</summary>
    public sealed class AbilitySandboxReport
    {
        public bool Success;
        public string Message;
        public bool CastSucceeded;
        public CastFailureReason CastFailureReason;
        public long CastId;
        public int ActualLevel;
        public bool ExecutionAborted;
        public bool HasPendingWork;
        public float SimulatedSeconds;
        public int CompletedSteps;
        public float DamageTotal;
        public float HealingTotal;
        public float ManaSpent;
        public int RuntimeObjectsBeforeCleanup;
        public int RemainingRuntimeObjects;
        public bool CleanupSucceeded;
        public bool LogTruncated;
        public readonly List<string> BudgetMessages = new List<string>();
        public readonly List<string> Events = new List<string>();
        public readonly List<string> ValidationMessages = new List<string>();
        public readonly List<AbilitySandboxUnitResult> Units = new List<AbilitySandboxUnitResult>();
    }

    /// <summary>
    /// Runs configured abilities in a new engine, with private unit/world/resource/presentation adapters.
    /// No custom AbilityScript/ModifierScript factory is discovered or executed. Providers are supplied
    /// explicitly by the caller; the normal UI supplies JSON providers from its selected workspace.
    /// </summary>
    public static class AbilitySandbox
    {
        private const int MaximumLogEntries = 512;
        private const float InitialHealth = 5000f;
        private const float MaximumHealth = 10000f;

        public static AbilitySandboxReport Run(IEnumerable<IAbilityDefinitionProvider> providers, string abilityName, int level, AbilitySandboxOptions options = null)
        {
            AbilitySandboxReport report = new AbilitySandboxReport();
            options = options ?? new AbilitySandboxOptions();
            if (!ValidateOptions(options, out string optionError))
            {
                report.Message = optionError;
                report.CleanupSucceeded = true;
                return report;
            }

            AbilitySystem engine = new AbilitySystem
            {
                MaxExecutionDepth = options.MaxExecutionDepth,
                MaxExecutionOperations = options.MaxExecutionOperations
            };
            SandboxWorld world = new SandboxWorld();
            SandboxPresentation presentation = new SandboxPresentation();
            SandboxResources resources = new SandboxResources();
            Action<RuntimeEvent> listener = value => RecordEvent(report, world.Time, value);
            engine.EventRaised += listener;
            bool started = false;
            try
            {
                engine.Initialize(world, presentation);
                AbilityDefinitionRegistry registry = new AbilityDefinitionRegistry();
                // LoadProvider records registrations without validating the complete
                // graph. The editor's iterative preflight must run before ApplyTo can enter the
                // production graph walker, including for a very long but acyclic input chain.
                if (providers == null) registry.LoadProvider(null);
                else foreach (IAbilityDefinitionProvider provider in providers) registry.LoadProvider(provider);
                AbilityDefinitionBundle merged = new AbilityDefinitionBundle();
                merged.Abilities.AddRange(registry.Abilities.Values);
                merged.Modifiers.AddRange(registry.Modifiers.Values);
                merged.Projectiles.AddRange(registry.Projectiles.Values);
                bool referenceError = false;
                foreach (AbilityEditorDiagnostic issue in AbilityReferencePreflight.ValidateBundles(new[] { merged }))
                {
                    if (report.ValidationMessages.Count < MaximumLogEntries)
                        report.ValidationMessages.Add(issue.Severity + " · " + issue.Code + " · " + issue.Message + (string.IsNullOrEmpty(issue.ReferenceChain) ? string.Empty : " · " + issue.ReferenceChain));
                    else report.LogTruncated = true;
                    if (issue.Severity == AbilityValidationSeverity.Error) referenceError = true;
                }
                if (referenceError)
                {
                    report.Message = "沙盒引用预检未通过；请先缩短过深创建链或修正引用环。";
                    return report;
                }
                if (!registry.ApplyTo(engine))
                {
                    AddValidationMessages(report, registry.LastApplyValidation ?? registry.Validation);
                    report.Message = "未能发布沙盒定义。请先修正配置；需要自定义脚本的定义必须由业务提供专用测试适配器。";
                    return report;
                }
                if (!registry.TryResolveAbilityName(abilityName, out string resolvedName) || !engine.TryGetAbilityDefinition(resolvedName, out AbilityDefinition definition))
                {
                    report.Message = "找不到技能：" + (abilityName ?? "<empty>");
                    return report;
                }

                SandboxUnit caster = new SandboxUnit(1, 1, Vector3.zero, UnitType.Hero);
                world.Units.Add(caster);
                engine.RegisterUnit(caster);
                int actualLevel = Math.Max(1, Math.Min(level, definition.MaxLevel));
                BuildTargets(world, engine, definition, actualLevel, options.TargetCount);
                Ability ability = engine.AddAbility(caster, resolvedName, actualLevel, resources);
                if (ability == null)
                {
                    report.Message = "沙盒未能授予技能；请查看执行预算和事件记录。";
                    return report;
                }
                report.ActualLevel = ability.Level;
                if ((definition.Behavior & AbilityBehavior.Passive) != 0)
                {
                    report.CastSucceeded = true;
                    started = true;
                }
                else if (!report.ExecutionAborted)
                {
                    CastResult cast = engine.IssueOrder(BuildOrder(ability, world.Units[1]));
                    report.CastSucceeded = cast.Success;
                    report.CastFailureReason = cast.FailureReason;
                    report.CastId = cast.CastId;
                    started = cast.Success;
                    if (!started) report.Message = "沙盒施法被拒绝：" + cast.FailureReason + (string.IsNullOrEmpty(cast.Message) ? string.Empty : " · " + cast.Message);
                }

                // Each update is explicit simulation time; there is no Editor update subscription,
                // task, coroutine, resource request, scene object or reference to a live battle.
                int maximumSteps = (int)Math.Ceiling(options.SimulationSeconds / options.TickSeconds);
                for (int i = 0; started && !report.ExecutionAborted && i < maximumSteps; i++)
                {
                    float remaining = options.SimulationSeconds - world.Time;
                    if (remaining <= 0.000001f) break;
                    float delta = Math.Min(options.TickSeconds, remaining);
                    world.Advance(delta);
                    engine.Update(delta);
                    report.CompletedSteps++;
                }
                report.SimulatedSeconds = world.Time;
                if (report.ExecutionAborted) report.Message = "本次试运行触发执行预算中止；查看深度和操作数。";
                else if (started)
                {
                    report.HasPendingWork = ability.Phase == AbilityPhase.Casting || ability.Phase == AbilityPhase.Channeling || engine.Projectiles.Count != 0 || engine.Thinkers.Count != 0;
                    report.Message = report.HasPendingWork
                        ? "本次窗口内未触发预算中止，但仍有前摇、引导、弹道或 Thinker 未完成；可增加模拟时间。"
                        : "本次场景与时间窗口执行完成；不代表所有目标数量、事件或业务脚本路径均已验证。";
                }
                report.Success = started && !report.ExecutionAborted;
                CaptureUnits(report, engine, world);
            }
            catch (Exception error)
            {
                report.Success = false;
                report.Message = "沙盒执行失败：" + error.GetType().Name + " · " + error.Message;
            }
            finally
            {
                report.SimulatedSeconds = world.Time;
                report.ManaSpent = SandboxResources.StartingMana - resources.Mana;
                foreach (SandboxUnit unit in world.Units)
                {
                    report.DamageTotal += unit.DamageTaken;
                    report.HealingTotal += unit.HealingReceived;
                }
                report.RuntimeObjectsBeforeCleanup = ObjectCount(engine, world);
                try
                {
                    engine.ClearRuntime();
                    report.RemainingRuntimeObjects = ObjectCount(engine, world) + presentation.ActiveCount;
                    report.CleanupSucceeded = report.RemainingRuntimeObjects == 0;
                }
                catch (Exception error)
                {
                    report.CleanupSucceeded = false;
                    report.Success = false;
                    report.Message = (report.Message ?? string.Empty) + " 清理失败：" + error.Message;
                }
                finally
                {
                    engine.EventRaised -= listener;
                    presentation.StopAll();
                }
                if (!report.CleanupSucceeded) report.Success = false;
            }
            return report;
        }

        private static bool ValidateOptions(AbilitySandboxOptions options, out string error)
        {
            error = null;
            if (options.TargetCount < 1 || options.TargetCount > 128) error = "沙盒目标数量必须为 1–128。";
            else if (options.MaxExecutionDepth < 1 || options.MaxExecutionDepth > 128) error = "沙盒调用深度预算必须为 1–128。";
            else if (options.MaxExecutionOperations < 1 || options.MaxExecutionOperations > 8192) error = "沙盒操作预算必须为 1–8192。";
            else if (!Finite(options.SimulationSeconds) || options.SimulationSeconds < 0f || options.SimulationSeconds > 10f) error = "沙盒模拟时间必须为 0–10 秒。";
            else if (!Finite(options.TickSeconds) || options.TickSeconds < 0.01f || options.TickSeconds > 1f || Math.Ceiling(options.SimulationSeconds / options.TickSeconds) > 1000d) error = "沙盒步长必须为 0.01–1 秒，且总步数不超过 1000。";
            return error == null;
        }

        private static void BuildTargets(SandboxWorld world, AbilitySystem engine, AbilityDefinition definition, int level, int count)
        {
            bool friendlyOnly = definition.TargetTeam == TargetTeam.Friendly;
            int team = friendlyOnly ? 1 : 2;
            UnitType type = PickUnitType(definition.TargetType);
            float range = Math.Max(0f, definition.CastRange.GetValue(level));
            float radius = Math.Max(0f, definition.AoeRadius.GetValue(level));
            float distance = range > 0f ? Math.Min(5f, range * 0.5f) : radius > 0f ? Math.Min(5f, radius * 0.25f) : 2f;
            if ((definition.Behavior & AbilityBehavior.NoTarget) != 0 && radius > 0f) distance = Math.Min(distance, radius * 0.25f);
            float spacing = radius > 0f ? Math.Max(0.001f, Math.Min(0.025f, radius / 128f)) : 0.025f;
            for (int i = 0; i < count; i++)
            {
                SandboxUnit unit = new SandboxUnit(i + 2, team, new Vector3(distance + (i % 8) * spacing, 0f, (i / 8) * spacing), type);
                world.Units.Add(unit);
                engine.RegisterUnit(unit);
            }
        }

        private static CastOrder BuildOrder(Ability ability, SandboxUnit primaryTarget)
        {
            AbilityBehavior behavior = ability.Definition.Behavior;
            CastOrder order = new CastOrder { Caster = ability.Owner, AbilityName = ability.Definition.Name, Direction = Vector3.right };
            if ((behavior & AbilityBehavior.NoTarget) == 0 && (behavior & (AbilityBehavior.UnitTarget | AbilityBehavior.OptionalUnitTarget)) != 0) order.Target = primaryTarget;
            else if ((behavior & AbilityBehavior.NoTarget) == 0 && (behavior & AbilityBehavior.PointTarget) != 0)
            {
                order.TargetPosition = primaryTarget.Position;
                order.HasTargetPosition = true;
            }
            return order;
        }

        private static UnitType PickUnitType(UnitType allowed)
        {
            foreach (UnitType candidate in new[] { UnitType.Hero, UnitType.Basic, UnitType.Creep, UnitType.Building, UnitType.Ward, UnitType.Other })
                if ((allowed & candidate) != 0) return candidate;
            return UnitType.Hero;
        }

        private static void AddValidationMessages(AbilitySandboxReport report, AbilityValidationReport validation)
        {
            if (validation == null) return;
            foreach (AbilityValidationIssue issue in validation.Issues)
            {
                if (report.ValidationMessages.Count >= MaximumLogEntries) { report.LogTruncated = true; break; }
                report.ValidationMessages.Add(issue.Severity + " · " + issue.Code + " · " + issue.Message + (string.IsNullOrEmpty(issue.ReferenceChain) ? string.Empty : " · " + issue.ReferenceChain));
            }
        }

        private static void RecordEvent(AbilitySandboxReport report, float time, RuntimeEvent value)
        {
            if (value == null) return;
            if (value.EventType == RuntimeEventType.ExecutionAborted)
            {
                report.ExecutionAborted = true;
                if (report.BudgetMessages.Count < 32) report.BudgetMessages.Add(value.Message ?? "Execution budget exceeded.");
            }
            if (report.Events.Count >= MaximumLogEntries) { report.LogTruncated = true; return; }
            StringBuilder line = new StringBuilder();
            line.Append(time.ToString("0.000", CultureInfo.InvariantCulture)).Append("s · ").Append(value.EventType);
            if (value.CastId != 0) line.Append(" · cast#").Append(value.CastId);
            if (value.Ability != null) line.Append(" · ").Append(value.Ability.Definition.Name);
            if (value.Modifier != null) line.Append(" · ").Append(value.Modifier.Name);
            if (value.Target != null) line.Append(" · target#").Append(value.Target.EntityId);
            if (value.Value != 0f) line.Append(" · ").Append(value.Value.ToString("0.###", CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(value.Message)) line.Append(" · ").Append(value.Message);
            report.Events.Add(line.ToString());
        }

        private static void CaptureUnits(AbilitySandboxReport report, AbilitySystem engine, SandboxWorld world)
        {
            foreach (SandboxUnit unit in world.Units)
            {
                UnitState states = UnitState.None;
                foreach (UnitState state in Enum.GetValues(typeof(UnitState)))
                    if (state != UnitState.None && engine.HasState(unit, state)) states |= state;
                report.Units.Add(new AbilitySandboxUnitResult
                {
                    EntityId = unit.EntityId, TeamId = unit.TeamId, Health = unit.CurrentHealth, MaxHealth = unit.MaxHealth, States = states,
                    ArmorBonus = engine.GetModifierProperty(unit, ModifierProperty.ArmorBonus, null),
                    MoveSpeedBonus = engine.GetModifierProperty(unit, ModifierProperty.MoveSpeedBonusPercent, null),
                    AttackSpeedBonus = engine.GetModifierProperty(unit, ModifierProperty.AttackSpeedBonus, null)
                });
            }
        }

        private static int ObjectCount(AbilitySystem engine, SandboxWorld world)
        {
            int count = engine.Modifiers.Count + engine.Projectiles.Count + engine.Thinkers.Count;
            foreach (SandboxUnit unit in world.Units) count += engine.GetAbilities(unit).Count;
            return count;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private sealed class SandboxUnit : ICombatUnit
        {
            public SandboxUnit(int entityId, int teamId, Vector3 position, UnitType unitType)
            {
                EntityId = entityId; TeamId = teamId; Position = position; UnitType = unitType;
            }

            public int EntityId { get; }
            public int TeamId { get; }
            public UnitType UnitType { get; }
            public bool IsAlive => CurrentHealth > 0f;
            public bool IsMagicImmune => false;
            public bool IsInvulnerable => false;
            public Vector3 Position { get; }
            public float CurrentHealth { get; private set; } = InitialHealth;
            public float MaxHealth => MaximumHealth;
            public float BaseArmor => 0f;
            public float BaseMagicResistancePercent => 0f;
            public float BasePhysicalDamageBlock => 0f;
            public float DamageTaken { get; private set; }
            public float HealingReceived { get; private set; }
            public bool IsVisibleToTeam(int teamId) => true;
            public void ApplyDamage(DamageResult result) { ApplyFinalDamage(result); }
            public void Heal(HealInfo info) { ApplyFinalHeal(info); }

            public DamageApplicationResult ApplyFinalDamage(DamageResult result)
            {
                float applied = result == null ? 0f : Math.Min(CurrentHealth, Math.Max(0f, result.FinalAmount));
                CurrentHealth -= applied; DamageTaken += applied;
                return new DamageApplicationResult(applied, CurrentHealth <= 0f);
            }

            public HealApplicationResult ApplyFinalHeal(HealInfo info)
            {
                float applied = info == null ? 0f : Math.Min(MaxHealth - CurrentHealth, Math.Max(0f, info.Amount));
                CurrentHealth += applied; HealingReceived += applied;
                return new HealApplicationResult(applied);
            }
        }

        private sealed class SandboxWorld : IWorld
        {
            public readonly List<SandboxUnit> Units = new List<SandboxUnit>();
            public float Time { get; private set; }
            public void Advance(float delta) { Time += delta; }
            public bool HasLineOfSight(IUnit viewer, Vector3 position) => true;
            public void FindUnits(Vector3 center, float radius, TargetQuery query, IList<IUnit> results)
            {
                float squared = radius * radius;
                foreach (SandboxUnit unit in Units)
                    if ((unit.Position - center).sqrMagnitude <= squared) results.Add(unit);
            }
        }

        private sealed class SandboxResources : IResourceOwner
        {
            public const float StartingMana = 1000000f;
            public float Mana { get; private set; } = StartingMana;
            public bool HasMana(float amount) => amount <= Mana;
            public bool SpendMana(float amount)
            {
                if (!HasMana(amount)) return false;
                Mana -= Math.Max(0f, amount);
                return true;
            }
        }

        private sealed class SandboxPresentation : IPresentation
        {
            private readonly List<SandboxHandle> handles = new List<SandboxHandle>();
            public int ActiveCount { get { int count = 0; foreach (SandboxHandle handle in handles) if (handle.IsActive) count++; return count; } }
            public void PlayEffect(string effectName, Vector3 position) { }
            public void PlayEffect(string effectName, IUnit target) { }
            public void PlaySound(string soundName, Vector3 position) { }
            public IPresentationHandle PlayPersistentEffect(string effectName, IUnit target)
            {
                SandboxHandle handle = new SandboxHandle(effectName, target != null ? target.EntityId : 0);
                handles.Add(handle); return handle;
            }
            public void StopAll() { foreach (SandboxHandle handle in handles) handle.Stop(); }
        }

        private sealed class SandboxHandle : IPresentationHandle
        {
            public SandboxHandle(string effectName, int targetEntityId) { EffectName = effectName; TargetEntityId = targetEntityId; }
            public string EffectName { get; }
            public int TargetEntityId { get; }
            public bool IsActive { get; private set; } = true;
            public void Stop() { IsActive = false; }
        }
    }
}

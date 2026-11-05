using System;
using System.Collections.Generic;

namespace Game.Ability
{
    /// <summary>
    /// Dota-style level-scaled number. If no per-level values are provided, BaseValue is used.
    /// </summary>
    [Serializable]
    public sealed class LevelValue
    {
        public float BaseValue;
        public readonly List<float> ValuesByLevel = new List<float>();

        public static LevelValue Constant(float value)
        {
            return new LevelValue { BaseValue = value };
        }

        public float GetValue(int level)
        {
            if (ValuesByLevel.Count == 0)
            {
                return BaseValue;
            }

            int index = Math.Max(0, level - 1);
            if (index >= ValuesByLevel.Count)
            {
                index = ValuesByLevel.Count - 1;
            }

            return ValuesByLevel[index];
        }
    }

    /// <summary>
    /// Charge and replenish rules for abilities such as stored casts.
    /// </summary>
    [Serializable]
    public sealed class ChargeDefinition
    {
        public int MaxCharges;
        public float RestoreTime;
        public bool StartFull = true;
        public bool UsesCooldown = true;
    }

    /// <summary>
    /// Data-only ability configuration. Runtime state lives in Ability, not in this definition.
    /// </summary>
    [Serializable]
    public sealed class AbilityDefinition
    {
        public string Name;
        public string DisplayName;
        public string Description;
        public string Icon;
        public int MaxLevel = 1;
        public AbilityBehavior Behavior = AbilityBehavior.NoTarget;
        public TargetTeam TargetTeam = TargetTeam.Enemy;
        public UnitType TargetType = UnitType.All;
        public TargetFlags TargetFlags = TargetFlags.None;
        public LevelValue CastRange = LevelValue.Constant(0f);
        public LevelValue AoeRadius = LevelValue.Constant(0f);
        public LevelValue CastPoint = LevelValue.Constant(0f);
        public LevelValue CastBackswing = LevelValue.Constant(0f);
        public LevelValue ChannelTime = LevelValue.Constant(0f);
        public LevelValue Cooldown = LevelValue.Constant(0f);
        public LevelValue ManaCost = LevelValue.Constant(0f);
        public ChargeDefinition Charges;
        public string IntrinsicModifierName;
        // Named tuning values for custom C# scripts, matching Dota's "special values" idea.
        public readonly Dictionary<string, LevelValue> SpecialValues = new Dictionary<string, LevelValue>();
        // Data-driven actions for simple abilities that do not need custom AbilityScript code.
        public readonly List<ActionDefinition> Actions = new List<ActionDefinition>();
        // Hit behavior belongs to the launching ability, not a caster-wide event listener.
        public readonly List<ActionDefinition> OnProjectileHitActions = new List<ActionDefinition>();
        // Toggle lifecycle is distinct from a normal cast; it never implicitly runs Actions.
        public readonly List<ActionDefinition> OnToggleOnActions = new List<ActionDefinition>();
        public readonly List<ActionDefinition> OnToggleOffActions = new List<ActionDefinition>();

        public float GetSpecialValue(string name, int level)
        {
            if (string.IsNullOrEmpty(name) || !SpecialValues.TryGetValue(name, out LevelValue value))
            {
                return 0f;
            }

            return value.GetValue(level);
        }
    }

    /// <summary>
    /// One data-driven operation executed by ActionRunner.
    /// </summary>
    [Serializable]
    public sealed class ActionDefinition
    {
        public ActionType ActionType;
        public ActionTarget Target = ActionTarget.ContextTargets;
        public LevelValue Value = LevelValue.Constant(0f);
        public string ValueSpecialName;
        // Event sources multiply the configured number, e.g. 0.2 * applied damage.
        public ActionValueSource ValueSource = ActionValueSource.LevelOrSpecial;
        public LevelValue Duration = LevelValue.Constant(float.NaN);
        public string DurationSpecialName;
        public DamageType DamageType = DamageType.Magical;
        public DamageFlags DamageFlags = DamageFlags.None;
        public string ModifierName;
        public bool PurgePositiveBuffs;
        public bool PurgeDebuffs = true;
        public bool PurgeOnlyPurgable = true;
        public string ProjectileName;
        public ProjectileDefinition Projectile;
        public string EffectName;
        public string SoundName;

        public float ResolveValue(Ability ability)
        {
            if (ValueSource != ActionValueSource.LevelOrSpecial) throw new InvalidOperationException("Event-valued actions must be resolved with a CastContext.");
            return ResolveNumber(ValueSpecialName, Value, ability, ability != null ? ability.Level : 1);
        }

        public float ResolveDuration(Ability ability)
        {
            return ResolveNumber(DurationSpecialName, Duration, ability, ability != null ? ability.Level : 1);
        }

        public float ResolveValue(CastContext context)
        {
            float value = ResolveNumber(ValueSpecialName, Value, context.Ability, context.Level);
            if (ValueSource == ActionValueSource.LevelOrSpecial) return value;
            if (context.Event == null) throw new InvalidOperationException("Event-valued action requires a modifier event context.");
            switch (ValueSource)
            {
                case ActionValueSource.EventDamage:
                    if (context.Event.DamageResult == null) throw new InvalidOperationException("EventDamage requires a damage result.");
                    return value * context.Event.DamageResult.AppliedAmount;
                case ActionValueSource.EventHealing:
                    if (context.Event.HealInfo == null) throw new InvalidOperationException("EventHealing requires a heal result.");
                    return value * context.Event.HealInfo.Amount;
                case ActionValueSource.EventValue:
                    return value * context.Event.Value;
                default:
                    throw new InvalidOperationException("Unknown action value source: " + ValueSource);
            }
        }

        public float ResolveDuration(CastContext context)
        {
            return ResolveNumber(DurationSpecialName, Duration, context.Ability, context.Level);
        }

        private static float ResolveNumber(string specialName, LevelValue value, Ability ability, int level)
        {
            if (string.IsNullOrEmpty(specialName))
            {
                return value.GetValue(Math.Max(1, level));
            }

            if (ability == null || !ability.Definition.SpecialValues.TryGetValue(specialName, out LevelValue special))
            {
                // A missing duration must never silently become zero (the permanent sentinel).
                throw new InvalidOperationException("Action references missing special value '" + specialName + "'.");
            }

            return special.GetValue(Math.Max(1, level));
        }
    }

    /// <summary>
    /// Data-only modifier configuration. The runtime Modifier owns duration, stacks, and timers.
    /// </summary>
    [Serializable]
    public sealed class ModifierDefinition
    {
        public string Name;
        public string DisplayName;
        public bool IsHidden;
        public bool IsDebuff;
        public bool IsPurgable = true;
        public bool RemoveOnDeath = true;
        public float Duration;
        public float Interval;
        public int MaxStack = 1;
        public ModifierAttribute Attributes = ModifierAttribute.None;
        public readonly Dictionary<ModifierProperty, float> Properties = new Dictionary<ModifierProperty, float>();
        // Uses the source modifier's current level; cannot duplicate a fixed property key.
        public readonly Dictionary<ModifierProperty, LevelValue> LevelProperties = new Dictionary<ModifierProperty, LevelValue>();
        public UnitState States = UnitState.None;
        // Lifecycle action lists let config-only modifiers cover common buff/debuff behavior.
        public readonly List<ActionDefinition> OnCreatedActions = new List<ActionDefinition>();
        public readonly List<ActionDefinition> OnRefreshActions = new List<ActionDefinition>();
        public readonly List<ActionDefinition> OnDestroyActions = new List<ActionDefinition>();
        public readonly List<ActionDefinition> IntervalActions = new List<ActionDefinition>();
        public ModifierEventType TriggerEventType = ModifierEventType.None;
        public ModifierEventScope TriggerEventScope = ModifierEventScope.Related;
        // A reflected hit is still observable; reactive skills can opt out of reflecting it again.
        public DamageFlags TriggerExcludedDamageFlags = DamageFlags.None;
        public readonly List<ActionDefinition> TriggerActions = new List<ActionDefinition>();
        // Persistent presentation owned by each Modifier runtime instance.
        public string SustainedEffectName;
        // Aura source modifiers periodically refresh this modifier on valid nearby units.
        public string AuraModifierName;
        public float AuraRadius;
        public float AuraDuration = 0.5f;
        public float AuraThinkInterval = 0.25f;
        // Maintenance refresh normally renews duration only. Pulse stacking is opt-in.
        public bool AuraIncrementsStacks;
        public TargetTeam AuraTargetTeam = TargetTeam.Friendly;
        public UnitType AuraTargetType = UnitType.All;
        public TargetFlags AuraTargetFlags = TargetFlags.None;

        public float GetPropertyValue(ModifierProperty property, int level)
        {
            if (LevelProperties.TryGetValue(property, out LevelValue value)) return value != null ? value.GetValue(Math.Max(1, level)) : 0f;
            return Properties.TryGetValue(property, out float constant) ? constant : 0f;
        }
    }

    /// <summary>
    /// Runtime projectile tuning shared by tracking and linear projectiles.
    /// </summary>
    [Serializable]
    public sealed class ProjectileDefinition
    {
        public string Name;
        public float Speed = 900f;
        public float Radius = 96f;
        public float Distance = 1200f;
        public float MaxLifetime = 10f;
        public TrackingTargetPolicy TrackingTargetPolicy = TrackingTargetPolicy.KeepTracking;
        public bool DeleteOnHit = true;
        public bool ProvidesVision;
        public float VisionRadius;
        public TargetTeam TargetTeam = TargetTeam.Enemy;
        public UnitType TargetType = UnitType.All;
        public TargetFlags TargetFlags = TargetFlags.None;
        public string EffectName;
        public string SoundName;
    }
}


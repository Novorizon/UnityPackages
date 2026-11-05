using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    /// <summary>
    /// A single command from gameplay/UI/AI into the ability system.
    /// </summary>
    public sealed class CastOrder
    {
        public IUnit Caster;
        public string AbilityName;
        public IUnit Target;
        public Vector3 TargetPosition;
        public Vector3 Direction;
        public bool HasTargetPosition;
        public bool Queue;
        public bool AllowHidden;
    }

    /// <summary>
    /// Result returned to callers immediately after a cast request is validated or rejected.
    /// </summary>
    public sealed class CastResult
    {
        public bool Success;
        public CastFailureReason FailureReason;
        public string Message;
        public long CastId;
        public bool Queued;

        public static CastResult Ok()
        {
            return new CastResult { Success = true };
        }

        public static CastResult Fail(CastFailureReason reason, string message = null)
        {
            return new CastResult { Success = false, FailureReason = reason, Message = message };
        }
    }

    /// <summary>
    /// Immutable-enough context assembled for one spell execution.
    /// It carries the primary target plus any area targets resolved at cast time.
    /// </summary>
    public sealed class CastContext
    {
        private readonly List<IUnit> targets = new List<IUnit>();

        public AbilitySystem Engine;
        public Ability Ability;
        public IUnit Caster;
        public IUnit Target;
        public Vector3 TargetPosition;
        public Vector3 Direction;
        public int Level = 1;
        public long CastId;
        public Modifier Modifier;
        public ModifierEvent Event;
        // Retains the independent lifetime of an already launched projectile after ability removal.
        public Projectile Projectile;
        internal bool IsTerminated;
        public IReadOnlyList<IUnit> Targets => targets;

        internal void AddTarget(IUnit target)
        {
            if (target != null && !targets.Contains(target))
            {
                targets.Add(target);
            }
        }
    }

    /// <summary>
    /// Options used when applying or refreshing a modifier.
    /// Numbers and Objects are escape hatches for custom C# scripts.
    /// </summary>
    public sealed class ModifierApplyOptions
    {
        public float Duration = float.NaN;
        public int StackCount = 1;
        public int Level = 1;
        public bool IncrementStacksOnRefresh = true;
        public bool IsAura;
        public Modifier SourceModifier;
        public readonly Dictionary<string, float> Numbers = new Dictionary<string, float>();
        public readonly Dictionary<string, object> Objects = new Dictionary<string, object>();
    }

    /// <summary>
    /// Input to the engine damage pipeline.
    /// </summary>
    public sealed class DamageInfo
    {
        public AbilitySystem Engine;
        public IUnit Attacker;
        public IUnit Victim;
        public Ability Ability;
        public float Amount;
        public DamageType DamageType = DamageType.Magical;
        public DamageFlags Flags = DamageFlags.None;
        public DamageCategory Category = DamageCategory.Spell;
        public long AttackId;
    }

    /// <summary>
    /// Final damage result after immunity checks and modifier multipliers.
    /// </summary>
    public sealed class DamageResult
    {
        public IUnit Attacker;
        public IUnit Victim;
        public Ability Ability;
        public float OriginalAmount;
        public float FinalAmount;
        public DamageType DamageType;
        public DamageFlags Flags;
        public bool Blocked;
        public string BlockReason;
        public DamageCategory Category;
        public long AttackId;
        public float AppliedAmount;
        public bool Killed;
    }

    /// <summary>
    /// Input to the heal pipeline. The Game adapter applies the actual HP change.
    /// </summary>
    public sealed class HealInfo
    {
        public IUnit Source;
        public IUnit Target;
        public Ability Ability;
        public float Amount;
    }

    /// <summary>
    /// Internal event delivered to modifiers so they can react to orders, casts, damage, and more.
    /// </summary>
    public sealed class ModifierEvent
    {
        public ModifierEventType EventType;
        public long CastId;
        public long AttackId;
        public AbilitySystem Engine;
        public IUnit Source;
        public IUnit Target;
        public Ability Ability;
        public Modifier Modifier;
        public DamageInfo DamageInfo;
        public DamageResult DamageResult;
        public HealInfo HealInfo;
        public CastOrder Order;
        public Projectile Projectile;
        public Vector3 Position;
        public float Value;
    }

    /// <summary>
    /// Context passed when querying additive modifier properties.
    /// </summary>
    public sealed class ModifierPropertyContext
    {
        public AbilitySystem Engine;
        public IUnit Unit;
        public Ability Ability;
        public DamageInfo DamageInfo;
        public DamageResult DamageResult;
    }

    /// <summary>
    /// Factory request for projectile creation.
    /// </summary>
    public sealed class ProjectileRequest
    {
        public ProjectileDefinition Definition;
        public Ability Ability;
        public IUnit Caster;
        public IUnit Source;
        public IUnit Target;
        public Vector3 Origin;
        public Vector3 Direction;
        public bool Tracking;
    }

    /// <summary>
    /// Factory request for area thinkers, such as persistent ground effects.
    /// </summary>
    public sealed class ThinkerRequest
    {
        public string Name;
        public Ability Ability;
        public IUnit Caster;
        public Vector3 Position;
        public float Duration;
        public float Interval;
        public float Radius;
        public ThinkerScript Script;
    }

    /// <summary>
    /// Public runtime notification stream for presentation, logging, and tests.
    /// </summary>
    public sealed class RuntimeEvent
    {
        public RuntimeEventType EventType;
        public long CastId;
        public long AttackId;
        public Ability Ability;
        public Modifier Modifier;
        public Projectile Projectile;
        public Thinker Thinker;
        public IUnit Caster;
        public IUnit Target;
        public Vector3 Position;
        public float Value;
        public CastFailureReason FailureReason;
        public string Message;
    }
}


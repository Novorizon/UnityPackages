using System;

namespace Game.Ability
{
    /// <summary>
    /// Bit flags describing how an ability is aimed and executed.
    /// </summary>
    [Flags]
    public enum AbilityBehavior
    {
        None = 0,
        Hidden = 1 << 0,
        Passive = 1 << 1,
        NoTarget = 1 << 2,
        UnitTarget = 1 << 3,
        PointTarget = 1 << 4,
        Aoe = 1 << 5,
        Channelled = 1 << 6,
        Toggle = 1 << 7,
        Immediate = 1 << 8,
        RootDisables = 1 << 9,
        AutoCast = 1 << 10,
        OptionalUnitTarget = 1 << 11,
        Directional = 1 << 12
    }

    /// <summary>
    /// Team relationship allowed by a target query.
    /// </summary>
    [Flags]
    public enum TargetTeam
    {
        None = 0,
        Friendly = 1 << 0,
        Enemy = 1 << 1,
        Both = Friendly | Enemy
    }

    /// <summary>
    /// Broad unit categories used by target filters.
    /// </summary>
    [Flags]
    public enum UnitType
    {
        None = 0,
        Hero = 1 << 0,
        Basic = 1 << 1,
        Building = 1 << 2,
        Creep = 1 << 3,
        Ward = 1 << 4,
        Other = 1 << 5,
        All = Hero | Basic | Building | Creep | Ward | Other
    }

    /// <summary>
    /// Additional targeting exceptions and restrictions.
    /// </summary>
    [Flags]
    public enum TargetFlags
    {
        None = 0,
        Dead = 1 << 0,
        MagicImmuneEnemies = 1 << 1,
        Invulnerable = 1 << 2,
        IncludeSelf = 1 << 3,
        ExcludeSelf = 1 << 4,
        VisibleOnly = 1 << 5,
        IgnoreLineOfSight = 1 << 6,
        Untargetable = 1 << 7
    }

    /// <summary>
    /// States contributed by units or modifiers.
    /// </summary>
    [Flags]
    public enum UnitState
    {
        None = 0,
        Stunned = 1 << 0,
        Silenced = 1 << 1,
        Muted = 1 << 2,
        Rooted = 1 << 3,
        Disarmed = 1 << 4,
        Hexed = 1 << 5,
        Invulnerable = 1 << 6,
        MagicImmune = 1 << 7,
        OutOfGame = 1 << 8,
        CommandRestricted = 1 << 9,
        NoUnitCollision = 1 << 10,
        Untargetable = 1 << 11
    }

    /// <summary>
    /// Damage category used by immunity and modifier calculations.
    /// </summary>
    public enum DamageType
    {
        None = 0,
        Physical = 1,
        Magical = 2,
        Pure = 3
    }

    /// <summary>
    /// Per-damage-call exceptions for the damage pipeline.
    /// </summary>
    [Flags]
    public enum DamageFlags
    {
        None = 0,
        NoSpellAmplification = 1 << 0,
        HpLoss = 1 << 1,
        NonLethal = 1 << 2,
        IgnoreBlock = 1 << 3,
        Reflected = 1 << 4,
        NoDamageMultiplier = 1 << 5,
        IgnoreInvulnerable = 1 << 6,
        PiercesSpellImmunity = 1 << 7
    }

    /// <summary>
    /// Runtime behavior flags for modifier stacking, persistence, and aura semantics.
    /// </summary>
    [Flags]
    public enum ModifierAttribute
    {
        None = 0,
        Permanent = 1 << 0,
        Multiple = 1 << 1,
        IgnoreInvulnerable = 1 << 2,
        Aura = 1 << 3,
        NoDurationRefresh = 1 << 4,
        StackIndependent = 1 << 5
    }

    /// <summary>
    /// Additive property channels queried by business systems and damage calculations.
    /// </summary>
    public enum ModifierProperty
    {
        None = 0,
        MoveSpeedBonusPercent = 1,
        AttackSpeedBonus = 2,
        DamageOutgoingPercent = 3,
        DamageIncomingPercent = 4,
        SpellAmplifyPercent = 5,
        ArmorBonus = 6,
        HealthRegen = 7,
        CooldownReductionPercent = 8,
        CastRangeBonus = 9
    }

    /// <summary>
    /// Events broadcast to modifiers for reactive behavior.
    /// </summary>
    public enum ModifierEventType
    {
        None = 0,
        OrderIssued = 1,
        AbilityExecuted = 2,
        AbilityFullyCast = 3,
        ChannelFinished = 4,
        DamageCalculated = 5,
        DamageDealt = 6,
        DamageTaken = 7,
        Healed = 8,
        AttackStart = 9,
        AttackLanded = 10,
        Death = 11,
        ProjectileHit = 12,
        ModifierAdded = 13,
        ModifierRemoved = 14,
        Respawn = 15
    }

    /// <summary>
    /// Configured modifiers are relation-scoped by default. Global delivery must be explicit;
    /// custom ModifierScript implementations may still inspect every broadcast themselves.
    /// </summary>
    public enum ModifierEventScope
    {
        Related = 0,
        Global = 1
    }

    /// <summary>
    /// Stable cast rejection reasons for UI, AI, and tests.
    /// </summary>
    public enum CastFailureReason
    {
        None = 0,
        MissingAbility = 1,
        AbilityHidden = 2,
        AbilityPassive = 3,
        NotTrained = 4,
        NotActivated = 5,
        DeadCaster = 6,
        Stunned = 7,
        Silenced = 8,
        Rooted = 9,
        Cooldown = 10,
        NoCharges = 11,
        NotEnoughMana = 12,
        InvalidTarget = 13,
        OutOfRange = 14,
        NoVision = 15,
        Channeling = 16,
        Casting = 17,
        CustomRejected = 18,
        CommandRestricted = 19,
        RuntimeUnavailable = 20,
        QueueFull = 21
    }

    /// <summary>
    /// Runtime phase for a single ability instance.
    /// </summary>
    public enum AbilityPhase
    {
        Idle = 0,
        Casting = 1,
        Channeling = 2,
        Backswing = 3
    }

    /// <summary>
    /// Public event stream emitted by AbilitySystem.
    /// </summary>
    public enum RuntimeEventType
    {
        AbilityAdded = 1,
        AbilityRemoved = 2,
        CastStarted = 3,
        CastFailed = 4,
        SpellStarted = 5,
        AbilityFullyCast = 6,
        ChannelStarted = 7,
        ChannelFinished = 8,
        ToggleChanged = 9,
        CooldownStarted = 10,
        CooldownFinished = 11,
        ChargeChanged = 12,
        ModifierAdded = 13,
        ModifierRemoved = 14,
        DamageApplied = 15,
        HealApplied = 16,
        ProjectileCreated = 17,
        ProjectileHit = 18,
        ProjectileDestroyed = 19,
        ThinkerCreated = 20,
        ThinkerDestroyed = 21,
        CastInterrupted = 22,
        CastCompleted = 23,
        ExecutionAborted = 24,
        UnitDied = 25,
        UnitRespawned = 26
    }

    /// <summary>
    /// Data-driven operation kinds supported by ActionRunner.
    /// </summary>
    public enum ActionType
    {
        None = 0,
        Damage = 1,
        Heal = 2,
        AddModifier = 3,
        Purge = 4,
        CreateTrackingProjectile = 5,
        CreateLinearProjectile = 6,
        PlayEffect = 7,
        PlaySound = 8
    }

    /// <summary>
    /// Target resolution mode for a data-driven action.
    /// </summary>
    public enum ActionTarget
    {
        None = 0,
        Caster = 1,
        PrimaryTarget = 2,
        ContextTargets = 3,
        Point = 4,
        Parent = 5,
        EventSource = 6,
        EventTarget = 7
    }

    public enum ActionValueSource
    {
        LevelOrSpecial = 0,
        EventDamage = 1,
        EventHealing = 2,
        EventValue = 3
    }
}


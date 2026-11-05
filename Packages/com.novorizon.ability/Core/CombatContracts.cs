namespace Game.Ability
{
    /// <summary>Damage origin and damage type are separate axes. A physical spell is still a spell.</summary>
    public enum DamageCategory
    {
        Spell = 0,
        Attack = 1,
        HpLoss = 2
    }

    /// <summary>A launched attack token. Only its originating engine may resolve it, once.</summary>
    public sealed class AttackRecord
    {
        internal AttackRecord(AbilitySystem engine, long attackId, long generation, DamageInfo request)
        {
            Engine = engine;
            AttackId = attackId;
            Generation = generation;
            Request = CombatSnapshots.Clone(request);
        }

        internal AbilitySystem Engine { get; }
        internal long Generation { get; }
        internal DamageInfo Request { get; }
        internal AttackRecordState State { get; set; }
        internal DamageResult CommittedResult { get; set; }
        public long AttackId { get; }
        public IUnit Attacker => Request.Attacker;
        public IUnit Victim => Request.Victim;
        public Ability Ability => Request.Ability;
        public bool IsPending => State == AttackRecordState.Pending;
        public bool IsResolved => State == AttackRecordState.Resolved;
        public bool IsCancelled => State == AttackRecordState.Cancelled;
        public DamageResult Result => CombatSnapshots.Clone(CommittedResult);
    }

    internal enum AttackRecordState { Pending, Resolving, Resolved, Cancelled }

    /// <summary>
    /// Optional precise combat boundary. Implementations apply the supplied final amount exactly
    /// once and report the HP change; armor, resistance and block belong to AbilitySystem.
    /// Units without this interface retain the legacy IUnit boundary, but cannot accept NonLethal.
    /// </summary>
    public interface ICombatUnit : IUnit
    {
        float CurrentHealth { get; }
        float MaxHealth { get; }
        float BaseArmor { get; }
        float BaseMagicResistancePercent { get; }
        float BasePhysicalDamageBlock { get; }
        /// <summary>
        /// Applies HP once and returns the actual committed change. Project notifications that
        /// run after the HP commit must be isolated by the adapter so they cannot discard this
        /// result by throwing. The engine cannot infer an unreported commit from an exception.
        /// </summary>
        DamageApplicationResult ApplyFinalDamage(DamageResult result);
        /// <summary>
        /// Applies healing once and returns the actual committed change. As with damage,
        /// isolate post-commit project callbacks and always return the committed HP fact.
        /// </summary>
        HealApplicationResult ApplyFinalHeal(HealInfo info);
    }

    public sealed class DamageApplicationResult
    {
        public DamageApplicationResult(float appliedAmount, bool killed = false)
        {
            AppliedAmount = appliedAmount;
            Killed = killed;
        }

        public float AppliedAmount { get; }
        public bool Killed { get; }
    }

    public sealed class HealApplicationResult
    {
        public HealApplicationResult(float appliedAmount) { AppliedAmount = appliedAmount; }
        public float AppliedAmount { get; }
    }

    public sealed class HealResult
    {
        internal HealResult(IUnit source, IUnit target, Ability ability, float requestedAmount, float appliedAmount, string failureReason = null)
        {
            Source = source;
            Target = target;
            Ability = ability;
            RequestedAmount = requestedAmount;
            AppliedAmount = appliedAmount;
            FailureReason = failureReason;
        }

        public IUnit Source { get; }
        public IUnit Target { get; }
        public Ability Ability { get; }
        public float RequestedAmount { get; }
        public float AppliedAmount { get; }
        public string FailureReason { get; }
        public bool Applied => AppliedAmount > 0f;
    }

    /// <summary>Mutable legacy DTOs are never shared between observers or with committed facts.</summary>
    internal static class CombatSnapshots
    {
        internal static DamageInfo Clone(DamageInfo value)
        {
            return value == null ? null : new DamageInfo
            {
                Engine = value.Engine, Attacker = value.Attacker, Victim = value.Victim, Ability = value.Ability,
                Amount = value.Amount, DamageType = value.DamageType, Flags = value.Flags, Category = value.Category, AttackId = value.AttackId
            };
        }

        internal static DamageResult Clone(DamageResult value)
        {
            return value == null ? null : new DamageResult
            {
                Attacker = value.Attacker, Victim = value.Victim, Ability = value.Ability,
                OriginalAmount = value.OriginalAmount, FinalAmount = value.FinalAmount,
                DamageType = value.DamageType, Flags = value.Flags, Blocked = value.Blocked,
                BlockReason = value.BlockReason, Category = value.Category, AppliedAmount = value.AppliedAmount, Killed = value.Killed, AttackId = value.AttackId
            };
        }

        internal static HealInfo Clone(HealInfo value)
        {
            return value == null ? null : new HealInfo { Source = value.Source, Target = value.Target, Ability = value.Ability, Amount = value.Amount };
        }

        internal static CastOrder Clone(CastOrder value)
        {
            return value == null ? null : new CastOrder
            {
                Caster = value.Caster, AbilityName = value.AbilityName, Target = value.Target,
                TargetPosition = value.TargetPosition, Direction = value.Direction,
                HasTargetPosition = value.HasTargetPosition, Queue = value.Queue, AllowHidden = value.AllowHidden
            };
        }

        internal static ModifierEvent CloneEvent(ModifierEvent value)
        {
            return value == null ? null : new ModifierEvent
            {
                EventType = value.EventType, CastId = value.CastId, AttackId = value.AttackId, Engine = value.Engine, Source = value.Source, Target = value.Target,
                Ability = value.Ability, Modifier = value.Modifier, DamageInfo = Clone(value.DamageInfo),
                DamageResult = Clone(value.DamageResult), HealInfo = Clone(value.HealInfo), Order = Clone(value.Order),
                Projectile = value.Projectile, Position = value.Position, Value = value.Value
            };
        }

        internal static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        internal static float NonNegative(float value) { return IsFinite(value) ? System.Math.Max(0f, value) : 0f; }
    }
}

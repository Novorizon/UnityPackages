namespace Game.Ability
{
    /// <summary>Damage origin and damage type are separate axes. A physical spell is still a spell.</summary>
    /// <remarks>定义伤害 Category可选的枚举值。</remarks>
    public enum DamageCategory
    {
        Spell = 0,
        Attack = 1,
        HpLoss = 2
    }

    /// <summary>A launched attack token. Only its originating engine may resolve it, once.</summary>
    /// <remarks>普攻对应的记录。记录一次操作及其相关数据。</remarks>
    public sealed class AttackRecord
    {
        /// <summary>使用调用方提供的engine、attackId、generation创建 AttackRecord。</summary>
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

    /// <summary>定义普攻记录状态可选的枚举值。</summary>
    internal enum AttackRecordState { Pending, Resolving, Resolved, Cancelled }

    /// <summary>
    /// Optional precise combat boundary. Implementations apply the supplied final amount exactly
    /// once and report the HP change; armor, resistance and block belong to AbilitySystem.
    /// Units without this interface retain the legacy IUnit boundary, but cannot accept NonLethal.
    /// </summary>
    /// <remarks>定义战斗单位的调用契约。</remarks>
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
        /// <remarks>应用最终伤害。</remarks>
        DamageApplicationResult ApplyFinalDamage(DamageResult result);
        /// <summary>
        /// Applies healing once and returns the actual committed change. As with damage,
        /// isolate post-commit project callbacks and always return the committed HP fact.
        /// </summary>
        /// <remarks>应用最终治疗。</remarks>
        HealApplicationResult ApplyFinalHeal(HealInfo info);
    }

    /// <summary>伤害应用对应的结果。携带操作执行结果和相关数据。</summary>
    public sealed class DamageApplicationResult
    {
        /// <summary>使用调用方提供的appliedAmount、killed创建 DamageApplicationResult。</summary>
        public DamageApplicationResult(float appliedAmount, bool killed = false)
        {
            AppliedAmount = appliedAmount;
            Killed = killed;
        }

        public float AppliedAmount { get; }
        public bool Killed { get; }
    }

    /// <summary>治疗应用对应的结果。携带操作执行结果和相关数据。</summary>
    public sealed class HealApplicationResult
    {
        /// <summary>使用调用方提供的appliedAmount创建 HealApplicationResult。</summary>
        public HealApplicationResult(float appliedAmount) { AppliedAmount = appliedAmount; }
        public float AppliedAmount { get; }
    }

    /// <summary>治疗对应的结果。携带操作执行结果和相关数据。</summary>
    public sealed class HealResult
    {
        /// <summary>使用调用方提供的source、target、ability创建 HealResult。</summary>
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
    /// <remarks>提供战斗快照的实现。</remarks>
    internal static class CombatSnapshots
    {
        /// <summary>克隆战斗快照。</summary>
        internal static DamageInfo Clone(DamageInfo value)
        {
            return value == null ? null : new DamageInfo
            {
                Engine = value.Engine, Attacker = value.Attacker, Victim = value.Victim, Ability = value.Ability,
                Amount = value.Amount, DamageType = value.DamageType, Flags = value.Flags, Category = value.Category, AttackId = value.AttackId
            };
        }

        /// <summary>克隆战斗快照。</summary>
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

        /// <summary>克隆战斗快照。</summary>
        internal static HealInfo Clone(HealInfo value)
        {
            return value == null ? null : new HealInfo { Source = value.Source, Target = value.Target, Ability = value.Ability, Amount = value.Amount };
        }

        /// <summary>克隆战斗快照。</summary>
        internal static CastOrder Clone(CastOrder value)
        {
            return value == null ? null : new CastOrder
            {
                Caster = value.Caster, AbilityName = value.AbilityName, Target = value.Target,
                TargetPosition = value.TargetPosition, Direction = value.Direction,
                HasTargetPosition = value.HasTargetPosition, Queue = value.Queue, AllowHidden = value.AllowHidden
            };
        }

        /// <summary>克隆事件。</summary>
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

        /// <summary>检查数值是否既非无穷大也非 NaN。</summary>
        internal static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        /// <summary>根据传入参数取得NonNegative。</summary>
        internal static float NonNegative(float value) { return IsFinite(value) ? System.Math.Max(0f, value) : 0f; }
    }
}

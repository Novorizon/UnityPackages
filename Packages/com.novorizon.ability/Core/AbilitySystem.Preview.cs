namespace Game.Ability
{
    /// <summary>提供技能系统的实现。</summary>
    public sealed partial class AbilitySystem
    {
        /// <summary>
        /// Checks whether an order can start now. It does not issue an order, allocate a cast id,
        /// change queues, run phase callbacks or consume resources. Script filters/getters must be read-only.
        /// A successful preview is advisory: world state may change before the real order is issued.
        /// </summary>
        /// <remarks>评估指令。</remarks>
        public CastResult EvaluateOrder(CastOrder source)
        {
            using (BeginExecution())
            {
                if (source?.Caster == null) return CastResult.Fail(CastFailureReason.InvalidTarget);
                if (!IsRuntimeAvailable || IsUnitRemoving(source.Caster)) return CastResult.Fail(CastFailureReason.RuntimeUnavailable);
                Ability ability = FindAbility(source.Caster, source.AbilityName);
                if (!ContainsAbility(ability) || !ReferenceEquals(ability.Owner, source.Caster)) return CastResult.Fail(CastFailureReason.MissingAbility);
                CastResult result = EvaluateOrderContext(ability, Targeting.NormalizeOrder(ability, source), out _);
                return new CastResult { Success = result.Success, FailureReason = result.FailureReason, Message = result.Message };
            }
        }

        /// <summary>评估指令上下文。</summary>
        internal CastResult EvaluateOrderContext(Ability ability, CastOrder order, out CastContext context, long castId = 0)
        {
            context = null;
            CastResult result = ability.CanCastBasic(order.AllowHidden);
            if (!result.Success) return result;
            if (!Targeting.BuildCastContext(this, ability, order, out context, out result)) return result;
            if (!IsRuntimeAvailable || !ContainsAbility(ability) || IsUnitRemoving(ability.Owner)) return CastResult.Fail(CastFailureReason.RuntimeUnavailable);
            context.CastId = castId;
            result = ability.Script.CastFilter(context) ?? CastResult.Ok();
            if (!result.Success) return result;
            return ability.CanCastBasic(order.AllowHidden);
        }
    }
}

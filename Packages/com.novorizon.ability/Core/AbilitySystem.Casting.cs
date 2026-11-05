using System;
using System.Collections.Generic;

namespace Game.Ability
{
    public sealed partial class AbilitySystem
    {
        public const int MaximumQueuedOrdersPerUnit = 32;
        private long nextCastId;
        private readonly Dictionary<int, Queue<PendingCast>> queuedCasts = new Dictionary<int, Queue<PendingCast>>();

        private sealed class PendingCast
        {
            public Ability Ability;
            public CastOrder Order;
            public long CastId;
        }

        internal static CastOrder CopyOrder(CastOrder order)
        {
            return order == null ? null : new CastOrder
            {
                Caster = order.Caster,
                AbilityName = order.AbilityName,
                Target = order.Target,
                TargetPosition = order.TargetPosition,
                Direction = order.Direction,
                HasTargetPosition = order.HasTargetPosition,
                Queue = order.Queue,
                AllowHidden = order.AllowHidden
            };
        }

        internal bool ContainsAbility(Ability ability) => ability != null && !ability.IsRemoved && IsAbilityRegistered(ability);

        internal bool HasConflictingCast(Ability requested)
        {
            if ((requested.Definition.Behavior & AbilityBehavior.Immediate) != 0) return false;
            return HasOrdinaryCast(requested.Owner, requested);
        }

        private bool HasOrdinaryCast(IUnit owner, Ability except = null)
        {
            if (owner == null || !abilitiesByUnit.TryGetValue(owner.EntityId, out List<Ability> abilities)) return false;
            for (int i = 0; i < abilities.Count; i++)
            {
                Ability ability = abilities[i];
                if (ability == null || ReferenceEquals(ability, except) || !ReferenceEquals(ability.Owner, owner)) continue;
                if ((ability.Definition.Behavior & AbilityBehavior.Immediate) == 0 && ability.IsBusy) return true;
            }
            return false;
        }

        internal CastResult IssueOrderCore(CastOrder source)
        {
            using (BeginExecution())
            {
                if (!TryConsumeExecutionBudget("IssueOrder")) return CastResult.Fail(CastFailureReason.CustomRejected, "Ability execution budget exceeded.");
                if (source == null || source.Caster == null) return CastResult.Fail(CastFailureReason.InvalidTarget);
                if (!IsRuntimeAvailable || IsUnitRemoving(source.Caster)) return CastResult.Fail(CastFailureReason.RuntimeUnavailable);
                Ability ability = FindAbility(source.Caster, source.AbilityName);
                if (ability == null || !ReferenceEquals(ability.Owner, source.Caster) || !ContainsAbility(ability))
                {
                    CastResult missing = CastResult.Fail(CastFailureReason.MissingAbility);
                    RaiseCastFailed(null, CopyOrder(source), missing);
                    return missing;
                }

                CastOrder order = Targeting.NormalizeOrder(ability, source);
                long id = ++nextCastId;
                // Reactive modifiers receive their own snapshot and cannot rewrite the accepted command.
                DispatchModifierEvent(new ModifierEvent { EventType = ModifierEventType.OrderIssued, Engine = this, Source = order.Caster, Target = order.Target, Order = CopyOrder(order), Ability = ability, Position = order.HasTargetPosition ? order.TargetPosition : order.Caster.Position, CastId = id });
                if (!IsRuntimeAvailable || IsUnitRemoving(order.Caster) || !ContainsAbility(ability)) return FailOrder(ability, order, id, CastFailureReason.RuntimeUnavailable);
                bool immediate = (ability.Definition.Behavior & AbilityBehavior.Immediate) != 0;
                bool waiting = !immediate && (HasOrdinaryCast(order.Caster) || queuedCasts.TryGetValue(order.Caster.EntityId, out Queue<PendingCast> existing) && existing.Count > 0);
                if (order.Queue && waiting)
                {
                    if (!queuedCasts.TryGetValue(order.Caster.EntityId, out Queue<PendingCast> queue))
                    {
                        queue = new Queue<PendingCast>();
                        queuedCasts.Add(order.Caster.EntityId, queue);
                    }
                    if (queue.Count >= MaximumQueuedOrdersPerUnit) return FailOrder(ability, order, id, CastFailureReason.QueueFull);
                    queue.Enqueue(new PendingCast { Ability = ability, Order = order, CastId = id });
                    return new CastResult { Success = true, CastId = id, Queued = true };
                }
                CastResult result = ability.BeginOrder(order, id);
                // A newly accepted ordinary order replaces previously queued commands.
                if (result.Success && !order.Queue && !immediate) ClearQueuedOrdersForUnit(order.Caster, id);
                return result;
            }
        }

        private CastResult FailOrder(Ability ability, CastOrder order, long id, CastFailureReason reason)
        {
            CastResult result = CastResult.Fail(reason);
            result.CastId = id;
            RaiseCastFailed(ability, order, result);
            return result;
        }

        /// <summary>Cancels the unit's current cast, channel and recovery. Queued commands are cleared by default.</summary>
        public void Stop(IUnit owner, bool clearQueue = true)
        {
            using (BeginExecution())
            {
                if (owner == null || !TryConsumeExecutionBudget("Stop")) return;
                var errors = new List<Exception>();
                if (clearQueue) Cleanup(() => ClearQueuedOrdersForUnit(owner), errors);
                if (abilitiesByUnit.TryGetValue(owner.EntityId, out List<Ability> abilities))
                {
                    Ability[] snapshot = abilities.ToArray();
                    for (int i = 0; i < snapshot.Length; i++)
                        if (ContainsAbility(snapshot[i]) && ReferenceEquals(snapshot[i].Owner, owner)) Cleanup(snapshot[i].Interrupt, errors);
                }
                if (errors.Count > 0) throw new AggregateException("Orders were stopped, but cancellation callbacks failed.", errors);
            }
        }

        public IReadOnlyList<CastOrder> GetQueuedOrders(IUnit owner)
        {
            List<CastOrder> snapshot = new List<CastOrder>();
            if (owner != null && queuedCasts.TryGetValue(owner.EntityId, out Queue<PendingCast> queue))
                foreach (PendingCast pending in queue)
                    if (ReferenceEquals(pending.Order.Caster, owner)) snapshot.Add(CopyOrder(pending.Order));
            return snapshot.AsReadOnly();
        }

        internal void ClearQueuedOrdersForUnit(IUnit owner, long beforeCastId = long.MaxValue)
        {
            ClearQueuedOrders(owner, pending => pending.CastId < beforeCastId);
        }

        internal void ClearQueuedOrdersForAbility(Ability ability)
        {
            if (ability != null) ClearQueuedOrders(ability.Owner, pending => ReferenceEquals(pending.Ability, ability));
        }

        private void ClearQueuedOrders(IUnit owner, Predicate<PendingCast> shouldCancel)
        {
            if (owner == null || !queuedCasts.TryGetValue(owner.EntityId, out Queue<PendingCast> queue)) return;
            queuedCasts.Remove(owner.EntityId);
            PendingCast[] existing = queue.ToArray();
            List<PendingCast> cancelled = new List<PendingCast>();
            Queue<PendingCast> retained = new Queue<PendingCast>();
            for (int i = 0; i < existing.Length; i++)
            {
                if (shouldCancel(existing[i])) cancelled.Add(existing[i]);
                else retained.Enqueue(existing[i]);
            }
            if (retained.Count > 0) queuedCasts.Add(owner.EntityId, retained);
            var errors = new List<Exception>();
            for (int i = 0; i < cancelled.Count; i++)
            {
                PendingCast pending = cancelled[i];
                Cleanup(() => RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.CastInterrupted, Ability = pending.Ability, Caster = pending.Order.Caster, Target = pending.Order.Target, Position = pending.Order.TargetPosition, CastId = pending.CastId, Message = "Queued order cancelled." }), errors);
            }
            if (errors.Count > 0) throw new AggregateException("Queued orders were cancelled, but callbacks failed.", errors);
        }

        internal void TickQueuedOrders()
        {
            long generation = RuntimeGeneration;
            int[] owners = new int[queuedCasts.Count];
            queuedCasts.Keys.CopyTo(owners, 0);
            for (int i = 0; i < owners.Length; i++)
            {
                if (generation != RuntimeGeneration) return;
                if (!queuedCasts.TryGetValue(owners[i], out Queue<PendingCast> queue) || queue.Count == 0) continue;
                PendingCast pending = queue.Peek();
                if (HasOrdinaryCast(pending.Order.Caster)) continue;
                queue.Dequeue();
                if (queue.Count == 0) queuedCasts.Remove(owners[i]);
                if (!TryConsumeExecutionBudget("QueuedOrder"))
                {
                    FailOrder(pending.Ability, pending.Order, pending.CastId, CastFailureReason.CustomRejected);
                    continue;
                }
                if (!IsRuntimeAvailable || IsUnitRemoving(pending.Order.Caster) || !ContainsAbility(pending.Ability))
                    FailOrder(pending.Ability, pending.Order, pending.CastId, CastFailureReason.RuntimeUnavailable);
                else pending.Ability.BeginOrder(pending.Order, pending.CastId);
            }
        }

        internal void ResetCastingState()
        {
            Queue<PendingCast>[] queues = new Queue<PendingCast>[queuedCasts.Count];
            queuedCasts.Values.CopyTo(queues, 0);
            queuedCasts.Clear();
            var errors = new List<Exception>();
            for (int i = 0; i < queues.Length; i++)
                foreach (PendingCast pending in queues[i])
                    Cleanup(() => RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.CastInterrupted, Ability = pending.Ability, Caster = pending.Order.Caster, Target = pending.Order.Target, Position = pending.Order.TargetPosition, CastId = pending.CastId, Message = "Ability runtime reset." }), errors);
            if (errors.Count > 0) throw new AggregateException("Queued orders were reset, but callbacks failed.", errors);
            // Keep IDs monotonic so clients can correlate delayed notifications across resets.
        }
    }
}

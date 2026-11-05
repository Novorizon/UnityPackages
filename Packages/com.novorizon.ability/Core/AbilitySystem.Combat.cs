using System;
using System.Collections.Generic;

namespace Game.Ability
{
    public sealed partial class AbilitySystem
    {
        private readonly HashSet<int> notifiedDeaths = new HashSet<int>();
        private readonly HashSet<int> processingDeaths = new HashSet<int>();
        private readonly Dictionary<int, IUnit> deferredRespawns = new Dictionary<int, IUnit>();
        private readonly Dictionary<long, AttackRecord> pendingAttacks = new Dictionary<long, AttackRecord>();
        private long nextAttackId;

        /// <summary>
        /// Freezes the request, calculates, allows controlled amount/block adjustments, commits once,
        /// then publishes isolated snapshots. Listeners cannot redirect a request or rewrite history.
        /// </summary>
        public DamageResult ApplyDamage(DamageInfo damageInfo)
        {
            return ApplyDamageCore(damageInfo, null);
        }

        private DamageResult ApplyDamageCore(DamageInfo damageInfo, AttackRecord attack)
        {
            using (BeginExecution())
            {
                DamageInfo request = CombatSnapshots.Clone(damageInfo);
                if (request != null && (request.Flags & DamageFlags.HpLoss) != 0) request.Category = DamageCategory.HpLoss;
                DamageResult result = CreateDamageResult(request);
                if (!IsRuntimeAvailable || !TryConsumeExecutionBudget("Damage")) return Block(result, "Runtime is unavailable or execution budget exhausted.");
                if (!IsValidDamageRequest(request)) return Block(result, "Invalid damage request or victim.");
                request.Engine = this;
                long generation = RuntimeGeneration;
                string rejection = CheckDamageImmunity(request);
                if (rejection != null) return Block(result, rejection);
                if (generation != RuntimeGeneration || !IsRuntimeAvailable || !AreDamageParticipantsCurrent(request)) return Block(result, "Damage was cancelled during validation.");

                result.FinalAmount = CalculateDamage(request, result);
                if (generation != RuntimeGeneration || !IsRuntimeAvailable || !AreDamageParticipantsCurrent(request)) return Block(result, "Damage was cancelled during property calculation.");
                if (!IsHealthRemoval(request)) AdjustCalculatedDamage(request, result);
                if (generation != RuntimeGeneration || !IsRuntimeAvailable || !AreDamageParticipantsCurrent(request)) return Block(result, "Damage was cancelled during calculation.");
                rejection = CheckDamageImmunity(request);
                if (rejection != null) return Block(result, rejection);
                if (generation != RuntimeGeneration || !IsRuntimeAvailable || !AreDamageParticipantsCurrent(request)) return Block(result, "Damage was cancelled during validation.");
                if (result.Blocked) return Block(result, result.BlockReason ?? "Damage was blocked.");
                result.FinalAmount = CombatSnapshots.NonNegative(result.FinalAmount);

                ICombatUnit preciseVictim = request.Victim as ICombatUnit;
                if ((request.Flags & DamageFlags.NonLethal) != 0)
                {
                    if (preciseVictim == null) return Block(result, "NonLethal requires the precise ICombatUnit health boundary.");
                    result.FinalAmount = Math.Min(result.FinalAmount, Math.Max(0f, preciseVictim.CurrentHealth - 1f));
                }
                if (result.FinalAmount <= 0f) return Block(result, "Damage has no positive amount.");

                if (preciseVictim != null)
                {
                    DamageApplicationResult applied = preciseVictim.ApplyFinalDamage(CombatSnapshots.Clone(result));
                    result.AppliedAmount = applied != null ? CombatSnapshots.NonNegative(applied.AppliedAmount) : 0f;
                    result.Killed = applied != null && applied.Killed;
                }
                else
                {
                    // Legacy adapters cannot report overkill/rounding. Precise business adapters use ICombatUnit.
                    request.Victim.ApplyDamage(CombatSnapshots.Clone(result));
                    result.AppliedAmount = result.FinalAmount;
                    result.Killed = !request.Victim.IsAlive;
                }
                result.FinalAmount = result.AppliedAmount;
                if (result.AppliedAmount <= 0f) return Block(result, "The victim did not apply damage.");
                // An attack's HP fact must be queryable even if a later observer fails.
                // This also closes reentrant resolution before publishing notifications.
                if (attack != null) CompleteAttack(attack, result);

                // A defeat/settlement callback may clear the battle while the adapter applies HP.
                // The HP fact is still returned, but no gameplay callbacks run in the new generation.
                if (generation == RuntimeGeneration && IsRuntimeAvailable)
                {
                    List<Exception> errors = null;
                    // HP removal is not a damage proc source (lifesteal, reflection, shields).
                    if (!IsHealthRemoval(request) && !IsUnitRemoving(request.Victim))
                    {
                        try { PublishDamage(ModifierEventType.DamageTaken, request, result); }
                        catch (Exception exception) { AddCombatError(ref errors, exception); }
                    }
                    if (!IsHealthRemoval(request) && generation == RuntimeGeneration && IsRuntimeAvailable && IsCombatContextCurrent(request.Attacker, request.Victim, request.Ability))
                    {
                        try { PublishDamage(ModifierEventType.DamageDealt, request, result); }
                        catch (Exception exception) { AddCombatError(ref errors, exception); }
                    }
                    if (generation == RuntimeGeneration && IsRuntimeAvailable && !IsUnitRemoving(request.Victim))
                    {
                        try { RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.DamageApplied, AttackId = request.AttackId, Ability = request.Ability, Caster = request.Attacker, Target = request.Victim, Value = result.AppliedAmount }); }
                        catch (Exception exception) { AddCombatError(ref errors, exception); }
                        if (result.Killed && generation == RuntimeGeneration && IsRuntimeAvailable)
                        {
                            try { NotifyDeath(request.Victim, request.Attacker, request.Ability); }
                            catch (Exception exception) { AddCombatError(ref errors, exception); }
                        }
                    }
                    if (errors != null) throw new AggregateException("Damage was applied, but combat callbacks failed.", errors);
                }
                return CombatSnapshots.Clone(result);
            }
        }

        private static DamageResult CreateDamageResult(DamageInfo request)
        {
            return request == null ? new DamageResult() : new DamageResult
            {
                Attacker = request.Attacker, Victim = request.Victim, Ability = request.Ability,
                OriginalAmount = request.Amount, DamageType = request.DamageType, Flags = request.Flags, Category = request.Category, AttackId = request.AttackId
            };
        }

        private bool IsValidDamageRequest(DamageInfo request)
        {
            if (!AreDamageParticipantsCurrent(request)) return false;
            if (!CombatSnapshots.IsFinite(request.Amount) || request.Amount < 0f || !Enum.IsDefined(typeof(DamageCategory), request.Category)) return false;
            const DamageFlags knownFlags = DamageFlags.NoSpellAmplification | DamageFlags.HpLoss | DamageFlags.NonLethal | DamageFlags.IgnoreBlock | DamageFlags.Reflected | DamageFlags.NoDamageMultiplier | DamageFlags.IgnoreInvulnerable | DamageFlags.PiercesSpellImmunity;
            if ((request.Flags & ~knownFlags) != 0) return false;
            if (request.DamageType != DamageType.Physical && request.DamageType != DamageType.Magical && request.DamageType != DamageType.Pure) return false;
            return true;
        }

        private bool AreDamageParticipantsCurrent(DamageInfo request)
        {
            if (request == null || request.Victim == null || !request.Victim.IsAlive || !IsCombatContextCurrent(request.Attacker, request.Victim, request.Ability)) return false;
            if (!(request.Victim is ICombatUnit precise)) return true;
            float health = precise.CurrentHealth;
            return CombatSnapshots.IsFinite(health) && health > 0f;
        }

        private bool IsCombatContextCurrent(IUnit source, IUnit target, Ability ability)
        {
            // Dead sources may own launched hits/DoT. Despawned or replaced sources cannot
            // issue new work, and a historical ability is valid only in its originating battle.
            return target != null && !IsUnitRemoving(target) && (source == null || !IsUnitRemoving(source)) && IsAbilitySourceValid(ability);
        }

        private string CheckDamageImmunity(DamageInfo request)
        {
            long generation = RuntimeGeneration;
            if (HasState(request.Victim, UnitState.OutOfGame)) return "Victim is out of game.";
            if (generation != RuntimeGeneration || !IsRuntimeAvailable || !IsCombatContextCurrent(request.Attacker, request.Victim, request.Ability)) return "Combat participants changed during immunity checks.";
            if (HasState(request.Victim, UnitState.Invulnerable) && (request.Flags & DamageFlags.IgnoreInvulnerable) == 0) return "Victim is invulnerable.";
            if (generation != RuntimeGeneration || !IsRuntimeAvailable || !IsCombatContextCurrent(request.Attacker, request.Victim, request.Ability)) return "Combat participants changed during immunity checks.";
            if (request.DamageType == DamageType.Magical && HasState(request.Victim, UnitState.MagicImmune) && (request.Flags & DamageFlags.PiercesSpellImmunity) == 0) return "Victim is magic immune.";
            return null;
        }

        private static bool IsHealthRemoval(DamageInfo request)
        {
            return request.Category == DamageCategory.HpLoss || (request.Flags & DamageFlags.HpLoss) != 0;
        }

        private float CalculateDamage(DamageInfo request, DamageResult result)
        {
            float amount = request.Amount;
            long generation = RuntimeGeneration;
            bool hpLoss = IsHealthRemoval(request);
            ModifierPropertyContext context = new ModifierPropertyContext { Engine = this, Ability = request.Ability, DamageInfo = CombatSnapshots.Clone(request), DamageResult = CombatSnapshots.Clone(result) };
            if (!hpLoss && (request.Flags & DamageFlags.NoDamageMultiplier) == 0)
            {
                if (request.Attacker != null)
                {
                    amount *= PositiveMultiplier(GetModifierProperty(request.Attacker, ModifierProperty.DamageOutgoingPercent, context));
                    if (generation != RuntimeGeneration || !IsRuntimeAvailable || !IsCombatContextCurrent(request.Attacker, request.Victim, request.Ability)) return 0f;
                    if (request.Category == DamageCategory.Spell && (request.Flags & DamageFlags.NoSpellAmplification) == 0)
                    {
                        amount *= PositiveMultiplier(GetModifierProperty(request.Attacker, ModifierProperty.SpellAmplifyPercent, context));
                        if (generation != RuntimeGeneration || !IsRuntimeAvailable || !IsCombatContextCurrent(request.Attacker, request.Victim, request.Ability)) return 0f;
                    }
                }
                amount *= PositiveMultiplier(GetModifierProperty(request.Victim, ModifierProperty.DamageIncomingPercent, context));
                if (generation != RuntimeGeneration || !IsRuntimeAvailable || !IsCombatContextCurrent(request.Attacker, request.Victim, request.Ability)) return 0f;
            }
            if (hpLoss) return CombatSnapshots.NonNegative(amount);

            ICombatUnit stats = request.Victim as ICombatUnit;
            if (request.DamageType == DamageType.Physical)
            {
                float block = stats != null ? CombatSnapshots.NonNegative(stats.BasePhysicalDamageBlock) : 0f;
                if ((request.Flags & DamageFlags.IgnoreBlock) == 0) amount = Math.Max(0f, amount - block);
                float armor = (stats != null && CombatSnapshots.IsFinite(stats.BaseArmor) ? stats.BaseArmor : 0f) + GetModifierProperty(request.Victim, ModifierProperty.ArmorBonus, context);
                if (CombatSnapshots.IsFinite(armor)) amount *= 1f - 0.06f * armor / (1f + 0.06f * Math.Abs(armor));
            }
            else if (request.DamageType == DamageType.Magical && stats != null)
            {
                float resistance = CombatSnapshots.IsFinite(stats.BaseMagicResistancePercent) ? stats.BaseMagicResistancePercent : 0f;
                amount *= Math.Max(0f, 1f - resistance / 100f);
            }
            return CombatSnapshots.NonNegative(amount);
        }

        private static float PositiveMultiplier(float percent)
        {
            return CombatSnapshots.IsFinite(percent) ? Math.Max(0f, 1f + percent / 100f) : 0f;
        }

        private void AdjustCalculatedDamage(DamageInfo request, DamageResult result)
        {
            long generation = RuntimeGeneration;
            var value = new ModifierEvent { EventType = ModifierEventType.DamageCalculated, Engine = this, Source = request.Attacker, Target = request.Victim, DamageInfo = request, DamageResult = result };
            List<Modifier> listeners = RentModifierEventListeners(value);
            try
            {
                for (int i = 0; i < listeners.Count; i++)
                {
                    Modifier modifier = listeners[i];
                    if (generation != RuntimeGeneration || !IsRuntimeAvailable || !IsCombatContextCurrent(request.Attacker, request.Victim, request.Ability)) return;
                    if (modifier == null || modifier.IsDestroyed || !IsModifierRegistered(modifier)) continue;
                    if (!TryConsumeExecutionBudget("DamageCalculated")) { Block(result, "Execution budget exhausted during damage calculation."); return; }
                    DamageResult editable = CombatSnapshots.Clone(result);
                    modifier.Script.OnEvent(new ModifierEvent
                    {
                        EventType = ModifierEventType.DamageCalculated, AttackId = request.AttackId, Engine = this, Source = request.Attacker,
                        Target = request.Victim, Ability = request.Ability, DamageInfo = CombatSnapshots.Clone(request), DamageResult = editable
                    });
                    // Only these channels are writable. Target, attribution, type and flags remain frozen.
                    result.FinalAmount = CombatSnapshots.NonNegative(editable.FinalAmount);
                    result.Blocked |= editable.Blocked;
                    if (editable.Blocked) result.BlockReason = editable.BlockReason;
                }
            }
            finally { RuntimeSnapshotPool<Modifier>.Release(listeners); }
        }

        private void PublishDamage(ModifierEventType type, DamageInfo request, DamageResult result)
        {
            DispatchModifierEvent(new ModifierEvent
            {
                EventType = type, AttackId = request.AttackId, Engine = this, Source = request.Attacker, Target = request.Victim,
                Ability = request.Ability, DamageInfo = CombatSnapshots.Clone(request), DamageResult = CombatSnapshots.Clone(result), Value = result.AppliedAmount
            });
        }

        private static DamageResult Block(DamageResult result, string reason)
        {
            result.Blocked = true;
            result.BlockReason = reason;
            result.FinalAmount = 0f;
            result.AppliedAmount = 0f;
            return result;
        }

        public void Heal(HealInfo healInfo) { HealWithResult(healInfo); }

        public HealResult HealWithResult(HealInfo healInfo)
        {
            using (BeginExecution())
            {
                HealInfo request = CombatSnapshots.Clone(healInfo);
                if (request == null) return new HealResult(null, null, null, 0f, 0f, "Missing heal request.");
                if (!IsRuntimeAvailable || !TryConsumeExecutionBudget("Heal") || request.Target == null || !request.Target.IsAlive || !IsCombatContextCurrent(request.Source, request.Target, request.Ability) || !CombatSnapshots.IsFinite(request.Amount) || request.Amount <= 0f)
                {
                    return new HealResult(request.Source, request.Target, request.Ability, request.Amount, 0f, "Heal was rejected.");
                }
                long generation = RuntimeGeneration;
                float appliedAmount;
                ICombatUnit precise = request.Target as ICombatUnit;
                if (precise != null)
                {
                    float missing = CombatSnapshots.NonNegative(precise.MaxHealth - precise.CurrentHealth);
                    if (missing <= 0f) return new HealResult(request.Source, request.Target, request.Ability, request.Amount, 0f);
                    if (generation != RuntimeGeneration || !IsRuntimeAvailable || !request.Target.IsAlive || !IsCombatContextCurrent(request.Source, request.Target, request.Ability))
                        return new HealResult(request.Source, request.Target, request.Ability, request.Amount, 0f, "Heal was cancelled before application.");
                    HealInfo application = CombatSnapshots.Clone(request);
                    application.Amount = Math.Min(application.Amount, missing);
                    HealApplicationResult applied = precise.ApplyFinalHeal(application);
                    appliedAmount = applied != null ? CombatSnapshots.NonNegative(applied.AppliedAmount) : 0f;
                }
                else
                {
                    request.Target.Heal(CombatSnapshots.Clone(request));
                    appliedAmount = request.Amount;
                }
                HealResult result = new HealResult(request.Source, request.Target, request.Ability, request.Amount, appliedAmount);
                if (appliedAmount > 0f && generation == RuntimeGeneration && IsRuntimeAvailable && !IsUnitRemoving(request.Target))
                {
                    HealInfo appliedInfo = CombatSnapshots.Clone(request);
                    appliedInfo.Amount = appliedAmount;
                    List<Exception> errors = null;
                    try { DispatchModifierEvent(new ModifierEvent { EventType = ModifierEventType.Healed, Engine = this, Source = request.Source, Target = request.Target, Ability = request.Ability, HealInfo = appliedInfo, Value = appliedAmount }); }
                    catch (Exception exception) { AddCombatError(ref errors, exception); }
                    if (generation == RuntimeGeneration && IsRuntimeAvailable && !IsUnitRemoving(request.Target))
                    {
                        try { RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.HealApplied, Ability = request.Ability, Caster = request.Source, Target = request.Target, Value = appliedAmount }); }
                        catch (Exception exception) { AddCombatError(ref errors, exception); }
                    }
                    if (errors != null) throw new AggregateException("Healing was applied, but combat callbacks failed.", errors);
                }
                return result;
            }
        }

        /// <summary>A complete immediate attack, using the same once-only path as a delayed attack.</summary>
        public DamageResult ApplyAttackDamage(DamageInfo damageInfo)
        {
            using (BeginExecution())
            {
                AttackRecord attack = BeginAttack(damageInfo);
                if (attack == null) return Block(CreateDamageResult(damageInfo), "The attack request is invalid or the attacker cannot attack.");
                return ResolveAttack(attack);
            }
        }

        /// <summary>
        /// Accepts an attack launch and freezes its combat request. After launch, stun or death of
        /// the attacker does not undo the hit; a miss/projectile cancellation calls CancelAttack.
        /// </summary>
        public AttackRecord BeginAttack(DamageInfo damageInfo)
        {
            using (BeginExecution())
            {
                DamageInfo request = CombatSnapshots.Clone(damageInfo);
                if (request == null || IsHealthRemoval(request) || !TryConsumeExecutionBudget("BeginAttack")) return null;
                long generation = RuntimeGeneration;
                request.Category = DamageCategory.Attack;
                if (!CanAttack(request.Attacker, request.Victim) || !IsValidDamageRequest(request) || generation != RuntimeGeneration) return null;
                request.Engine = this;
                request.AttackId = ++nextAttackId;
                AttackRecord attack = new AttackRecord(this, request.AttackId, generation, request);
                pendingAttacks.Add(attack.AttackId, attack);
                try { PublishAttack(ModifierEventType.AttackStart, request.Attacker, request.Victim, attack.AttackId); }
                catch { CancelAttack(attack); throw; }
                if (generation != RuntimeGeneration || !CanAttack(request.Attacker, request.Victim)) CancelAttack(attack);
                return attack;
            }
        }

        public DamageResult ResolveAttack(AttackRecord attack)
        {
            using (BeginExecution())
            {
                if (attack == null || attack.Engine != this) return Block(new DamageResult(), "Unknown attack record.");
                if (attack.State == AttackRecordState.Resolved) return attack.Result;
                if (attack.State != AttackRecordState.Pending || attack.Generation != RuntimeGeneration || !IsRuntimeAvailable || !pendingAttacks.ContainsKey(attack.AttackId)) return Block(CreateDamageResult(attack.Request), "Attack is cancelled, resolving, or from another battle.");
                if (IsUnitRemoving(attack.Attacker) || IsUnitRemoving(attack.Victim) || !TryConsumeExecutionBudget("ResolveAttack"))
                {
                    CancelAttack(attack);
                    return Block(CreateDamageResult(attack.Request), "Attack is cancelled.");
                }
                // Close the token before callbacks can re-enter resolution of the same attack.
                attack.State = AttackRecordState.Resolving;
                pendingAttacks.Remove(attack.AttackId);
                List<Exception> errors = null;
                DamageResult result = null;
                try
                {
                    result = ApplyDamageCore(attack.Request, attack);
                    if (attack.State != AttackRecordState.Resolved) CompleteAttack(attack, result);
                }
                catch (Exception exception)
                {
                    AddCombatError(ref errors, exception);
                    // A pre-commit failure closes the token without permitting a retry.
                    // Post-commit failures retain the exact HP result saved by ApplyDamageCore.
                    if (attack.State != AttackRecordState.Resolved) attack.State = AttackRecordState.Cancelled;
                    result = attack.CommittedResult;
                }
                if (result != null && !result.Blocked && attack.Generation == RuntimeGeneration && IsRuntimeAvailable && IsCombatContextCurrent(attack.Attacker, attack.Victim, attack.Ability))
                {
                    try { PublishAttack(ModifierEventType.AttackLanded, attack.Attacker, attack.Victim, attack.AttackId); }
                    catch (Exception exception) { AddCombatError(ref errors, exception); }
                }
                if (errors != null) throw new AggregateException("Attack resolution finished, but combat callbacks failed.", errors);
                return CombatSnapshots.Clone(result);
            }
        }

        private static void CompleteAttack(AttackRecord attack, DamageResult result)
        {
            attack.CommittedResult = CombatSnapshots.Clone(result);
            attack.State = AttackRecordState.Resolved;
        }

        private static void AddCombatError(ref List<Exception> errors, Exception exception)
        {
            if (errors == null) errors = new List<Exception>();
            errors.Add(exception);
        }

        public bool CancelAttack(AttackRecord attack)
        {
            if (attack == null || attack.Engine != this || attack.State != AttackRecordState.Pending) return false;
            pendingAttacks.Remove(attack.AttackId);
            attack.State = AttackRecordState.Cancelled;
            return true;
        }

        private void PublishAttack(ModifierEventType type, IUnit attacker, IUnit target, long attackId)
        {
            DispatchModifierEvent(new ModifierEvent { EventType = type, AttackId = attackId, Engine = this, Source = attacker, Target = target, Position = target.Position });
        }

        private bool CanAttack(IUnit attacker, IUnit target)
        {
            return IsRuntimeAvailable && attacker != null && attacker.IsAlive && target != null && target.IsAlive && !IsUnitRemoving(attacker) && !IsUnitRemoving(target) &&
                !HasState(attacker, UnitState.Stunned | UnitState.Disarmed | UnitState.CommandRestricted | UnitState.Hexed | UnitState.OutOfGame);
        }

        public void NotifyAttackStart(IUnit attacker, IUnit target)
        {
            using (BeginExecution())
            {
                if (!CanAttack(attacker, target) || !TryConsumeExecutionBudget("AttackStart")) return;
                DispatchModifierEvent(new ModifierEvent { EventType = ModifierEventType.AttackStart, Engine = this, Source = attacker, Target = target, Position = target.Position });
            }
        }

        public void NotifyAttackLanded(IUnit attacker, IUnit target)
        {
            using (BeginExecution())
            {
                // Lethal damage may already have killed the target. Do not require target.IsAlive here.
                if (!IsRuntimeAvailable || attacker == null || target == null || IsUnitRemoving(attacker) || IsUnitRemoving(target) || !TryConsumeExecutionBudget("AttackLanded")) return;
                DispatchModifierEvent(new ModifierEvent { EventType = ModifierEventType.AttackLanded, Engine = this, Source = attacker, Target = target, Position = target.Position });
            }
        }

        public void NotifyDeath(IUnit victim, IUnit killer = null, Ability ability = null)
        {
            using (BeginExecution())
            {
                if (!IsRuntimeAvailable || victim == null || victim.IsAlive || IsUnitRemoving(victim) || !notifiedDeaths.Add(victim.EntityId)) return;
                long generation = RuntimeGeneration;
                processingDeaths.Add(victim.EntityId);
                Ability[] owned = GetAbilitySnapshot(victim);
                Modifier[] snapshot = modifiers.ToArray();
                var errors = new List<Exception>();
                try
                {
                    for (int i = 0; i < owned.Length && generation == RuntimeGeneration; i++) Cleanup(owned[i].OnOwnerDeath, errors);
                    if (generation == RuntimeGeneration && IsRuntimeAvailable) Cleanup(() => DispatchModifierEvent(new ModifierEvent { EventType = ModifierEventType.Death, Engine = this, Source = killer, Target = victim, Ability = ability, Position = victim.Position }), errors);
                    if (generation == RuntimeGeneration && IsRuntimeAvailable) Cleanup(() => RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.UnitDied, Ability = ability, Caster = killer, Target = victim, Position = victim.Position }), errors);
                }
                finally
                {
                    // Death is distinct from despawn: do not remove effects merely sourced by this unit.
                    if (generation == RuntimeGeneration)
                    {
                        for (int i = 0; i < snapshot.Length && generation == RuntimeGeneration; i++)
                        {
                            Modifier modifier = snapshot[i];
                            if (modifier != null && modifier.Parent != null && modifier.Parent.EntityId == victim.EntityId && modifier.Definition.RemoveOnDeath) Cleanup(() => RemoveModifier(modifier), errors);
                        }
                        if (generation == RuntimeGeneration)
                        {
                            processingDeaths.Remove(victim.EntityId);
                            if (deferredRespawns.TryGetValue(victim.EntityId, out IUnit respawned))
                            {
                                deferredRespawns.Remove(victim.EntityId);
                                Cleanup(() => NotifyRespawn(respawned), errors);
                            }
                        }
                    }
                }
                if (errors.Count > 0) throw new AggregateException("Death was recorded and cleanup completed, but lifecycle callbacks failed.", errors);
            }
        }

        public void NotifyRespawn(IUnit unit)
        {
            using (BeginExecution())
            {
                if (!IsRuntimeAvailable || unit == null || !unit.IsAlive || IsUnitRemoving(unit)) return;
                if (processingDeaths.Contains(unit.EntityId)) { deferredRespawns[unit.EntityId] = unit; return; }
                bool wasDead = notifiedDeaths.Remove(unit.EntityId);
                long generation = RuntimeGeneration;
                Ability[] owned = GetAbilitySnapshot(unit);
                var errors = new List<Exception>();
                for (int i = 0; i < owned.Length && generation == RuntimeGeneration; i++) Cleanup(owned[i].RefreshIntrinsicAfterRespawn, errors);
                if (wasDead && generation == RuntimeGeneration && IsRuntimeAvailable)
                {
                    Cleanup(() => DispatchModifierEvent(new ModifierEvent { EventType = ModifierEventType.Respawn, Engine = this, Source = unit, Target = unit, Position = unit.Position }), errors);
                    if (generation == RuntimeGeneration && IsRuntimeAvailable) Cleanup(() => RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.UnitRespawned, Caster = unit, Target = unit, Position = unit.Position }), errors);
                }
                if (errors.Count > 0) throw new AggregateException("Respawn was recorded, but lifecycle callbacks failed.", errors);
            }
        }

        private Ability[] GetAbilitySnapshot(IUnit unit)
        {
            return unit != null && abilitiesByUnit.TryGetValue(unit.EntityId, out List<Ability> owned) ? owned.ToArray() : new Ability[0];
        }

        internal void RemoveCombatUnit(IUnit unit)
        {
            if (unit == null) return;
            notifiedDeaths.Remove(unit.EntityId);
            processingDeaths.Remove(unit.EntityId);
            deferredRespawns.Remove(unit.EntityId);
            var pending = new List<AttackRecord>(pendingAttacks.Values);
            foreach (AttackRecord attack in pending)
            {
                if (attack.Attacker.EntityId == unit.EntityId || attack.Victim.EntityId == unit.EntityId) CancelAttack(attack);
            }
        }

        internal void ResetCombatState()
        {
            notifiedDeaths.Clear();
            processingDeaths.Clear();
            deferredRespawns.Clear();
            foreach (AttackRecord attack in pendingAttacks.Values) attack.State = AttackRecordState.Cancelled;
            pendingAttacks.Clear();
        }
    }
}

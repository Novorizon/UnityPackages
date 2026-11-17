using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    /// <summary>
    /// Runtime ability instance owned by one unit.
    /// It stores level, cooldown, charges, cast point, channel state, and its C# script.
    /// </summary>
    /// <remarks>提供技能的实现。</remarks>
    public sealed class Ability
    {
        // Float partitioning can leave a tiny positive residual at an exact timer boundary.
        /// <summary>根据传入参数取得计时器 Tolerance。</summary>
        private static float TimerTolerance(float duration) => Mathf.Min(0.000001f, Mathf.Max(0f, duration) * 0.000001f);
        public AbilitySystem Engine { get; }
        internal long RuntimeGenerationAtCreation { get; }
        internal bool WasRegistered { get; set; }
        public AbilityDefinition Definition { get; }
        public IUnit Owner { get; }
        public IResourceOwner ResourceOwner { get; }
        public AbilityScript Script { get; }
        public int Level { get; private set; }
        public AbilityPhase Phase { get; private set; }
        public bool Activated { get; private set; } = true;
        public bool ToggleEnabled { get; private set; }
        public float CooldownRemaining { get; private set; }
        /// <summary>The duration captured when this cooldown began; changing modifiers cannot rescale its UI progress.</summary>
        public float CooldownDuration => cooldownDuration;
        public float ChargeRestoreRemaining => Mathf.Max(0f, chargeRestoreRemaining);
        public int Charges { get; private set; }

        private CastOrder pendingOrder;
        private CastContext channelContext;
        private float castPointRemaining;
        private float channelRemaining;
        private float castPointDuration;
        private float channelDuration;
        private float backswingDuration;
        private float cooldownDuration;
        private float chargeRestoreRemaining;
        // Intrinsic modifiers are tied to the ability lifetime and removed with the ability.
        private Modifier intrinsicModifier;
        private bool initialized;
        private bool executing;
        private int castVersion;
        private float backswingRemaining;
        private CastContext activeContext;
        private int cooldownVersion;
        private int chargeTimerVersion;

        // Captured for every ability before any ability callback runs in an Update. Keeping
        // the tokens in the rented frame snapshot also preserves nested Update isolation.
        /// <summary>调度对应的状态。保存状态或定义允许的状态值。</summary>
        internal readonly struct TickState
        {
            internal readonly Ability Ability;
            internal readonly int CastVersion;
            internal readonly int CooldownVersion;
            internal readonly int ChargeTimerVersion;

            /// <summary>使用调用方提供的ability创建 TickState。</summary>
            internal TickState(Ability ability)
            {
                Ability = ability;
                CastVersion = ability.castVersion;
                CooldownVersion = ability.cooldownVersion;
                ChargeTimerVersion = ability.chargeTimerVersion;
            }
        }

        /// <summary>采集调度状态。</summary>
        internal TickState CaptureTickState() => new TickState(this);

        public bool IsRemoved { get; private set; }
        public bool IsBusy => executing || Phase != AbilityPhase.Idle;
        public long CastId { get; private set; }

        /// <summary>使用调用方提供的engine、definition、owner创建 Ability。</summary>
        internal Ability(AbilitySystem engine, AbilityDefinition definition, IUnit owner, IResourceOwner resourceOwner, AbilityScript script, int level, long runtimeGeneration)
        {
            Engine = engine;
            RuntimeGenerationAtCreation = runtimeGeneration;
            Definition = definition;
            Owner = owner;
            ResourceOwner = resourceOwner;
            Script = script ?? new DefaultAbilityScript();
            Level = Mathf.Clamp(level, 0, Mathf.Max(1, definition.MaxLevel));
            Charges = definition.Charges != null && definition.Charges.StartFull ? Mathf.Max(0, definition.Charges.MaxCharges) : Mathf.Max(0, definition.Charges != null ? 0 : -1);
            chargeRestoreRemaining = definition.Charges != null && Charges < definition.Charges.MaxCharges
                ? Mathf.Max(0f, definition.Charges.RestoreTime)
                : 0f;
            Script.Bind(this);
        }

        /// <summary>初始化运行时。</summary>
        internal void InitializeRuntime()
        {
            if (initialized || IsRemoved) return;
            initialized = true;
            Script.OnCreated();
            if (IsOperational) ApplyIntrinsicModifier();
        }

        private bool IsOperational => !IsRemoved && Engine.IsRuntimeAvailable && Engine.ContainsAbility(this) && !Engine.IsUnitRemoving(Owner);

        /// <summary>设置等级。</summary>
        public void SetLevel(int level)
        {
            if (IsRemoved) return;
            int resolvedLevel = Mathf.Clamp(level, 0, Mathf.Max(1, Definition.MaxLevel));
            if (Level == resolvedLevel)
            {
                return;
            }

            Level = resolvedLevel;
            List<Exception> errors = new List<Exception>();
            if (Level == 0)
            {
                CaptureFailure(errors, Interrupt);
                CaptureFailure(errors, RemoveIntrinsicModifier);
            }
            CaptureFailure(errors, Script.OnUpgrade);
            if (IsOperational) CaptureFailure(errors, ApplyIntrinsicModifier);
            ThrowFailures(errors, "Ability level changed, but upgrade callbacks failed.");
        }

        /// <summary>设置 Activated。</summary>
        public void SetActivated(bool activated)
        {
            if (IsRemoved) return;
            Activated = activated;
            if (!activated) Interrupt();
        }

        /// <summary>获取特殊值。</summary>
        public float GetSpecialValue(string name)
        {
            return Definition.GetSpecialValue(name, Level);
        }

        /// <summary>获取冷却。</summary>
        public float GetCooldown()
        {
            float reduction = Engine.GetModifierProperty(Owner, ModifierProperty.CooldownReductionPercent, null);
            return Mathf.Max(0f, Script.GetCooldown() * Mathf.Max(0f, 1f - reduction / 100f));
        }

        /// <summary>获取法力代价。</summary>
        public float GetManaCost()
        {
            return Script.GetManaCost();
        }

        /// <summary>获取施法范围。</summary>
        public float GetCastRange()
        {
            return Script.GetCastRange();
        }

        /// <summary>获取范围效果半径。</summary>
        public float GetAoeRadius()
        {
            return Script.GetAoeRadius();
        }

        /// <summary>启动冷却。</summary>
        public void StartCooldown(float cooldown = -1f)
        {
            if (IsRemoved) return;
            CooldownRemaining = Mathf.Max(0f, cooldown >= 0f ? cooldown : GetCooldown());
            ++cooldownVersion;
            cooldownDuration = CooldownRemaining;
            if (CooldownRemaining > 0f)
            {
                Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.CooldownStarted, Ability = this, Caster = Owner, Value = CooldownRemaining });
            }
        }

        /// <summary>结束冷却。</summary>
        public void EndCooldown()
        {
            if (IsRemoved) return;
            CooldownRemaining = 0f;
            ++cooldownVersion;
            Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.CooldownFinished, Ability = this, Caster = Owner });
        }

        /// <summary>设置充能次数。</summary>
        public void SetCharges(int value)
        {
            if (IsRemoved || Definition.Charges == null)
            {
                return;
            }

            int maxCharges = Mathf.Max(0, Definition.Charges.MaxCharges);
            int previousCharges = Charges;
            Charges = Mathf.Clamp(value, 0, maxCharges);

            if (Charges >= maxCharges)
            {
                chargeRestoreRemaining = 0f;
                ++chargeTimerVersion;
            }
            else if (previousCharges >= maxCharges || chargeRestoreRemaining <= 0f)
            {
                chargeRestoreRemaining = Mathf.Max(0f, Definition.Charges.RestoreTime);
                ++chargeTimerVersion;
            }

            Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.ChargeChanged, Ability = this, Caster = Owner, Value = Charges });
        }

        /// <summary>恢复完整充能次数。</summary>
        public void RestoreFullCharges()
        {
            if (Definition.Charges != null)
            {
                SetCharges(Definition.Charges.MaxCharges);
            }
        }

        /// <summary>All orders pass through the engine's identity, budget and unit arbitration checks.</summary>
        /// <remarks>发出指令。</remarks>
        public CastResult IssueOrder(CastOrder order)
        {
            if (order == null || !IsSameUnit(order.Caster, Owner) || order.AbilityName != Definition.Name)
                return CastResult.Fail(CastFailureReason.InvalidTarget, "Order does not identify this ability and owner.");
            if (!IsOperational) return CastResult.Fail(CastFailureReason.RuntimeUnavailable);
            return Engine.IssueOrder(order);
        }

        /// <summary>开始指令。</summary>
        internal CastResult BeginOrder(CastOrder order, long castId)
        {
            CastResult result = Engine.EvaluateOrderContext(this, order, out CastContext context, castId);
            if (!result.Success) return Reject(order, result, castId);

            CastId = castId;
            int version = ++castVersion;
            activeContext = context;
            if ((Definition.Behavior & AbilityBehavior.Toggle) != 0)
            {
                bool enable = !ToggleEnabled;
                executing = true;
                try
                {
                    if (enable)
                    {
                        CastResult payment = CommitResources(context, version);
                        if (!payment.Success || !IsCurrent(version)) return payment;
                    }
                    ToggleEnabled = enable;
                    Script.OnToggle(ToggleEnabled);
                    if (!IsCurrent(version)) return Accepted(castId);
                    ActionRunner.Execute(ToggleEnabled ? Definition.OnToggleOnActions : Definition.OnToggleOffActions, context);
                    if (!IsCurrent(version)) return Accepted(castId);
                    Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.ToggleChanged, Ability = this, Caster = Owner, CastId = castId, Value = ToggleEnabled ? 1f : 0f });
                }
                catch (Exception error) { throw AbortFailedCallback(error, version); }
                finally { executing = false; }
                if (IsCurrent(version)) CompleteCast(context, castId);
                return Accepted(castId);
            }

            // A private order snapshot survives front-end reuse and modifier callbacks.
            pendingOrder = AbilitySystem.CopyOrder(order);
            Phase = AbilityPhase.Casting;
            bool phaseAccepted;
            try
            {
                castPointRemaining = (Definition.Behavior & AbilityBehavior.Immediate) != 0 ? 0f : Mathf.Max(0f, Script.GetCastPoint());
                castPointDuration = castPointRemaining;
                phaseAccepted = Script.OnAbilityPhaseStart(context);
            }
            catch (Exception error) { throw AbortFailedCallback(error, version); }
            if (!phaseAccepted)
            {
                CancelCastPhase(context, castId, true);
                return Reject(order, CastResult.Fail(CastFailureReason.CustomRejected, "Ability phase rejected."), castId);
            }
            if (!IsCurrent(version) || Phase != AbilityPhase.Casting) return Accepted(castId);
            try { Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.CastStarted, Ability = this, Caster = Owner, Target = context.Target, Position = context.TargetPosition, CastId = castId }); }
            catch (Exception error) { throw AbortFailedCallback(error, version); }
            if (!IsCurrent(version) || Phase != AbilityPhase.Casting) return Accepted(castId);
            if (castPointRemaining <= 0f) return ExecuteCast(context, version);
            return Accepted(castId);
        }

        /// <summary>调用 FinishChannel 完成Interrupt。</summary>
        public void Interrupt()
        {
            CastContext context = activeContext;
            long id = CastId;
            if (Phase == AbilityPhase.Channeling)
            {
                FinishChannel(true);
                return;
            }
            if (Phase == AbilityPhase.Casting)
            {
                CancelCastPhase(context, id, true);
                return;
            }
            if (Phase == AbilityPhase.Backswing || executing)
            {
                ++castVersion;
                Phase = AbilityPhase.Idle;
                backswingRemaining = 0f;
                activeContext = null;
                RaiseTerminal(context, id, true);
            }
        }

        /// <summary>推进技能。</summary>
        internal void Tick(float deltaTime, TickState frame)
        {
            if (!IsOperational || !ReferenceEquals(frame.Ability, this)) return;
            int version = frame.CastVersion;
            if (cooldownVersion == frame.CooldownVersion) TickCooldown(deltaTime);
            if (!IsOperational) return;
            // Observers on this or an earlier ability may create a timer at this frame
            // boundary. Existing timers still advance independently of a new cast.
            if (chargeTimerVersion == frame.ChargeTimerVersion) TickCharges(deltaTime);
            if (!IsOperational) return;
            ApplyIntrinsicModifier();
            // A callback that started a new cast does not inherit the old timer's elapsed frame.
            if (version != castVersion) return;
            if (Phase == AbilityPhase.Casting || Phase == AbilityPhase.Channeling)
            {
                CastResult interruption = ValidateOngoingCaster();
                if (!interruption.Success)
                {
                    CastOrder cancelledOrder = pendingOrder;
                    bool wasCasting = Phase == AbilityPhase.Casting;
                    long id = CastId;
                    Interrupt();
                    if (wasCasting) Reject(cancelledOrder, interruption, id);
                    return;
                }
            }
            if (Phase == AbilityPhase.Casting)
            {
                int activeVersion = castVersion;
                try { TickCasting(deltaTime); }
                catch (Exception error) { throw AbortFailedCallback(error, activeVersion); }
            }
            else if (Phase == AbilityPhase.Channeling) TickChannel(deltaTime);
            else if (Phase == AbilityPhase.Backswing)
            {
                backswingRemaining -= deltaTime;
                if (backswingRemaining <= TimerTolerance(backswingDuration)) CompleteCast(activeContext, CastId);
            }
        }

        /// <summary>移除技能。</summary>
        internal void Remove()
        {
            if (IsRemoved) return;
            IsRemoved = true;
            List<Exception> errors = new List<Exception>();
            CaptureFailure(errors, () => Engine.ClearQueuedOrdersForAbility(this));
            CaptureFailure(errors, Interrupt);
            CaptureFailure(errors, RemoveIntrinsicModifier);
            CaptureFailure(errors, Script.OnRemoved);
            ThrowFailures(errors, "Ability removal callbacks failed after runtime cleanup completed.");
        }

        /// <summary>响应持有方死亡。</summary>
        internal void OnOwnerDeath() { Interrupt(); }

        /// <summary>刷新固有之后复活。</summary>
        internal void RefreshIntrinsicAfterRespawn()
        {
            if (intrinsicModifier != null && intrinsicModifier.IsDestroyed) intrinsicModifier = null;
            ApplyIntrinsicModifier();
        }

        /// <summary>移除固有状态修饰器。</summary>
        private void RemoveIntrinsicModifier()
        {
            Modifier previous = intrinsicModifier;
            intrinsicModifier = null;
            if (previous != null) Engine.RemoveModifier(previous);
        }

        /// <summary>响应单位已移除。</summary>
        internal void OnUnitRemoved(IUnit unit)
        {
            if (unit == null)
            {
                return;
            }

            if (Phase == AbilityPhase.Casting && pendingOrder != null && IsSameUnit(pendingOrder.Target, unit))
            {
                CastOrder cancelledOrder = pendingOrder;
                long id = CastId;
                Interrupt();
                Reject(cancelledOrder, CastResult.Fail(CastFailureReason.InvalidTarget, "Target left the ability world."), id);
                return;
            }

            if (Phase == AbilityPhase.Channeling && channelContext != null && IsSameUnit(channelContext.Target, unit))
            {
                Interrupt();
            }
        }

        /// <summary>检查是否可以执行施法基础。</summary>
        internal CastResult CanCastBasic(bool allowHidden = false, bool ongoingCast = false)
        {
            if (!IsOperational) return CastResult.Fail(CastFailureReason.RuntimeUnavailable);
            if (Level <= 0) return CastResult.Fail(CastFailureReason.NotTrained);
            if (!allowHidden && (Definition.Behavior & AbilityBehavior.Hidden) != 0)
            {
                return CastResult.Fail(CastFailureReason.AbilityHidden);
            }

            if ((Definition.Behavior & AbilityBehavior.Passive) != 0)
            {
                return CastResult.Fail(CastFailureReason.AbilityPassive);
            }

            if (!Activated)
            {
                return CastResult.Fail(CastFailureReason.NotActivated);
            }

            if (Owner == null || !Owner.IsAlive)
            {
                return CastResult.Fail(CastFailureReason.DeadCaster);
            }

            if (Engine.HasState(Owner, UnitState.Stunned) || Engine.HasState(Owner, UnitState.Hexed))
            {
                return CastResult.Fail(CastFailureReason.Stunned);
            }

            if (Engine.HasState(Owner, UnitState.Silenced))
            {
                return CastResult.Fail(CastFailureReason.Silenced);
            }

            if ((Definition.Behavior & AbilityBehavior.RootDisables) != 0 &&
                Engine.HasState(Owner, UnitState.Rooted))
            {
                return CastResult.Fail(CastFailureReason.Rooted);
            }

            if (Engine.HasState(Owner, UnitState.CommandRestricted) || Engine.HasState(Owner, UnitState.OutOfGame))
            {
                return CastResult.Fail(CastFailureReason.CommandRestricted);
            }

            if (!ongoingCast && Phase == AbilityPhase.Casting)
            {
                return CastResult.Fail(CastFailureReason.Casting);
            }

            if (!ongoingCast && Phase == AbilityPhase.Channeling)
            {
                return CastResult.Fail(CastFailureReason.Channeling);
            }

            if (!ongoingCast && (Phase == AbilityPhase.Backswing || executing)) return CastResult.Fail(CastFailureReason.Casting);
            if (Engine.HasConflictingCast(this)) return CastResult.Fail(CastFailureReason.Casting, "Owner is executing another ability.");
            if (!IsOperational) return CastResult.Fail(CastFailureReason.RuntimeUnavailable);
            // Turning a toggle off does not require resources or wait for cooldown.
            if ((Definition.Behavior & AbilityBehavior.Toggle) != 0 && ToggleEnabled) return CastResult.Ok();

            if (CooldownRemaining > 0f)
            {
                return CastResult.Fail(CastFailureReason.Cooldown);
            }

            if (Definition.Charges != null && Charges <= 0)
            {
                return CastResult.Fail(CastFailureReason.NoCharges);
            }

            float manaCost = GetManaCost();
            if (!IsFinite(manaCost) || !IsFinite(GetCooldown()) || !IsFinite(Script.GetCastPoint()) || !IsFinite(Script.GetChannelTime()) || !IsFinite(Script.GetCastBackswing())) return CastResult.Fail(CastFailureReason.CustomRejected, "Ability returned non-finite cast parameters.");
            if (manaCost > 0f && (ResourceOwner == null || !ResourceOwner.HasMana(manaCost)))
            {
                return CastResult.Fail(CastFailureReason.NotEnoughMana);
            }

            return IsOperational ? CastResult.Ok() : CastResult.Fail(CastFailureReason.RuntimeUnavailable);
        }

        /// <summary>执行施法。</summary>
        private CastResult ExecuteCast(CastContext context, int version)
        {
            long id = context.CastId;
            if (!IsCurrent(version) || Phase != AbilityPhase.Casting) return Accepted(id);
            pendingOrder = null;
            executing = true;
            try
            {
                CastResult payment = CommitResources(context, version);
                if (!payment.Success || !IsCurrent(version)) return payment;
                Engine.DispatchModifierEvent(new ModifierEvent { EventType = ModifierEventType.AbilityExecuted, Engine = Engine, Source = Owner, Target = context.Target, Ability = this, Position = context.TargetPosition, CastId = id });
                if (!IsCurrent(version)) return Accepted(id);
                Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.SpellStarted, Ability = this, Caster = Owner, Target = context.Target, Position = context.TargetPosition, CastId = id });
                if (!IsCurrent(version)) return Accepted(id);
                Script.OnSpellStart(context);
                if (!IsCurrent(version)) return Accepted(id);
                Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.AbilityFullyCast, Ability = this, Caster = Owner, Target = context.Target, Position = context.TargetPosition, CastId = id });
                if (!IsCurrent(version)) return Accepted(id);
                Engine.DispatchModifierEvent(new ModifierEvent { EventType = ModifierEventType.AbilityFullyCast, Engine = Engine, Source = Owner, Target = context.Target, Ability = this, Position = context.TargetPosition, CastId = id });
                if (!IsCurrent(version)) return Accepted(id);
                float channelTime = Mathf.Max(0f, Script.GetChannelTime());
                if ((Definition.Behavior & AbilityBehavior.Channelled) != 0 && channelTime > 0f)
                {
                    Phase = AbilityPhase.Channeling;
                    channelRemaining = channelTime;
                    channelDuration = channelTime;
                    channelContext = context;
                    Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.ChannelStarted, Ability = this, Caster = Owner, Target = context.Target, Position = context.TargetPosition, Value = channelTime, CastId = id });
                }
                else BeginBackswing(context, id);
            }
            catch (Exception error) { throw AbortFailedCallback(error, version); }
            finally { executing = false; }
            return Accepted(id);
        }

        /// <summary>提交资源。</summary>
        private CastResult CommitResources(CastContext context, int version)
        {
            long id = context.CastId;
            float manaCost = GetManaCost();
            if (!IsFinite(manaCost))
            {
                Interrupt();
                return Reject(null, CastResult.Fail(CastFailureReason.CustomRejected, "Ability returned a non-finite mana cost."), id);
            }
            if (manaCost > 0f && (ResourceOwner == null || !ResourceOwner.SpendMana(manaCost)))
            {
                Interrupt();
                return Reject(null, CastResult.Fail(CastFailureReason.NotEnoughMana), id);
            }
            if (!IsCurrent(version)) return Accepted(id);
            if (Definition.Charges != null)
            {
                SetCharges(Charges - 1);
                if (!IsCurrent(version)) return Accepted(id);
                if (Definition.Charges.UsesCooldown) StartCooldown();
            }
            else StartCooldown();
            return Accepted(id);
        }

        /// <summary>推进施法。</summary>
        private void TickCasting(float deltaTime)
        {
            float used = Mathf.Min(deltaTime, Mathf.Max(0f, castPointRemaining));
            castPointRemaining -= used;
            if (castPointRemaining > TimerTolerance(castPointDuration)) return;
            CastOrder order = pendingOrder;
            int version = castVersion;
            long id = CastId;
            // The phase belongs to this cast, but ongoing range/resources are checked again.
            CastResult result = CanCastBasic(order != null && order.AllowHidden, true);
            if (!IsCurrent(version) || Phase != AbilityPhase.Casting) return;
            if (!result.Success)
            {
                CancelCastPhase(activeContext, id, true);
                Reject(order, result, id);
                return;
            }
            if (!Targeting.BuildCastContext(Engine, this, order, out CastContext context, out result))
            {
                CancelCastPhase(activeContext, id, true);
                Reject(order, result, id);
                return;
            }
            context.CastId = id;
            context.Level = Level;
            result = Script.CastFilter(context) ?? CastResult.Ok();
            if (!result.Success)
            {
                CancelCastPhase(activeContext, id, true);
                Reject(order, result, id);
                return;
            }
            if (!IsCurrent(version) || Phase != AbilityPhase.Casting) return;
            activeContext = context;
            int previousCooldownVersion = cooldownVersion;
            int previousChargeVersion = chargeTimerVersion;
            ExecuteCast(context, version);
            if (!IsCurrent(version)) return;
            float remainingDelta = Mathf.Max(0f, deltaTime - used);
            if (remainingDelta <= 0f) return;
            int committedChargeVersion = chargeTimerVersion;
            if (cooldownVersion != previousCooldownVersion) TickCooldown(remainingDelta);
            if (!IsCurrent(version)) return;
            // Existing recovery already received this frame's delta in Tick. Only a timer
            // created by this cast receives its post-cast-point fraction here.
            if (committedChargeVersion != previousChargeVersion && chargeTimerVersion == committedChargeVersion) TickCharges(remainingDelta);
            if (!IsCurrent(version)) return;
            if (Phase == AbilityPhase.Channeling) TickChannel(remainingDelta);
            else if (Phase == AbilityPhase.Backswing)
            {
                backswingRemaining -= remainingDelta;
                if (backswingRemaining <= TimerTolerance(backswingDuration)) CompleteCast(activeContext, id);
            }
        }

        /// <summary>推进引导。</summary>
        private void TickChannel(float deltaTime)
        {
            if (Phase != AbilityPhase.Channeling) return;
            int version = castVersion;
            long id = CastId;
            float used = Mathf.Min(deltaTime, Mathf.Max(0f, channelRemaining));
            channelRemaining -= used;
            try { Script.OnChannelThink(used); }
            catch (Exception error) { throw AbortFailedCallback(error, version); }
            if (!IsCurrent(version) || Phase != AbilityPhase.Channeling) return;
            if (channelRemaining <= TimerTolerance(channelDuration))
            {
                FinishChannel(false);
                float leftover = Mathf.Max(0f, deltaTime - used);
                if (IsOperational && CastId == id && Phase == AbilityPhase.Backswing && leftover > 0f)
                {
                    backswingRemaining -= leftover;
                    if (backswingRemaining <= TimerTolerance(backswingDuration)) CompleteCast(activeContext, id);
                }
            }
        }

        /// <summary>结束引导。</summary>
        private void FinishChannel(bool interrupted)
        {
            if (Phase != AbilityPhase.Channeling) return;
            CastContext finishedContext = channelContext;
            long id = CastId;
            int version = ++castVersion;
            Phase = AbilityPhase.Idle;
            channelContext = null;
            activeContext = null;
            channelRemaining = 0f;
            List<Exception> errors = new List<Exception>();
            CaptureFailure(errors, () => Script.OnChannelFinish(interrupted));
            CaptureFailure(errors, () => Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.ChannelFinished, Ability = this, Caster = Owner, Target = finishedContext != null ? finishedContext.Target : null, Position = finishedContext != null ? finishedContext.TargetPosition : Owner.Position, Value = interrupted ? 1f : 0f, CastId = id }));
            CaptureFailure(errors, () => Engine.DispatchModifierEvent(new ModifierEvent { EventType = ModifierEventType.ChannelFinished, Engine = Engine, Source = Owner, Ability = this, Target = finishedContext != null ? finishedContext.Target : null, CastId = id }));
            if (interrupted || errors.Count > 0) CaptureFailure(errors, () => RaiseTerminal(finishedContext, id, true));
            else if (IsCurrent(version) && CastId == id) CaptureFailure(errors, () => BeginBackswing(finishedContext, id));
            else CaptureFailure(errors, () => RaiseTerminal(finishedContext, id, false));
            if (errors.Count > 0) CaptureFailure(errors, () => RaiseTerminal(finishedContext, id, true));
            ThrowFailures(errors, "Channel finish callbacks failed after channel state was cleared.");
        }

        /// <summary>开始后摇。</summary>
        private void BeginBackswing(CastContext context, long id)
        {
            float duration = (Definition.Behavior & AbilityBehavior.Immediate) != 0 ? 0f : Mathf.Max(0f, Script.GetCastBackswing());
            if (duration > 0f)
            {
                activeContext = context;
                Phase = AbilityPhase.Backswing;
                backswingRemaining = duration;
                backswingDuration = duration;
            }
            else CompleteCast(context, id);
        }

        /// <summary>完成施法。</summary>
        private void CompleteCast(CastContext context, long id)
        {
            Phase = AbilityPhase.Idle;
            activeContext = null;
            backswingRemaining = 0f;
            RaiseTerminal(context, id, false);
        }

        /// <summary>取消施法阶段。</summary>
        private void CancelCastPhase(CastContext context, long id, bool invokeHook)
        {
            if (Phase != AbilityPhase.Casting) return;
            ++castVersion;
            Phase = AbilityPhase.Idle;
            pendingOrder = null;
            activeContext = null;
            castPointRemaining = 0f;
            List<Exception> errors = new List<Exception>();
            if (invokeHook) CaptureFailure(errors, Script.OnAbilityPhaseInterrupted);
            CaptureFailure(errors, () => RaiseTerminal(context, id, true));
            ThrowFailures(errors, "Ability phase interruption callbacks failed after cast state was cleared.");
        }

        /// <summary>中止失败回调。</summary>
        private AggregateException AbortFailedCallback(Exception error, int version)
        {
            List<Exception> errors = new List<Exception> { error };
            // A callback may already have ended this cast and started another one.
            if (version == castVersion) CaptureFailure(errors, Interrupt);
            return new AggregateException("Ability callback failed after its cast was interrupted.", errors);
        }

        /// <summary>采集失败原因。</summary>
        private static void CaptureFailure(List<Exception> errors, Action callback)
        {
            try { callback(); }
            catch (Exception error) { errors.Add(error); }
        }

        /// <summary>抛出异常 Failures。</summary>
        private static void ThrowFailures(List<Exception> errors, string message)
        {
            if (errors.Count > 0) throw new AggregateException(message, errors);
        }

        /// <summary>触发 Terminal。</summary>
        private void RaiseTerminal(CastContext context, long id, bool interrupted)
        {
            if (id == 0 || context == null || context.IsTerminated) return;
            context.IsTerminated = true;
            Engine.RaiseEvent(new RuntimeEvent { EventType = interrupted ? RuntimeEventType.CastInterrupted : RuntimeEventType.CastCompleted, Ability = this, Caster = Owner, Target = context != null ? context.Target : null, Position = context != null ? context.TargetPosition : Owner.Position, CastId = id });
        }

        /// <summary>检查数值是否既非无穷大也非 NaN。</summary>
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        /// <summary>检查输入是否仍是当前有效值。</summary>
        private bool IsCurrent(int version) => IsOperational && version == castVersion;

        /// <summary>拒绝技能。</summary>
        private CastResult Reject(CastOrder order, CastResult result, long id)
        {
            result.CastId = id;
            Engine.RaiseCastFailed(this, order, result);
            return result;
        }

        /// <summary>根据传入参数取得Accepted。</summary>
        private static CastResult Accepted(long id)
        {
            CastResult result = CastResult.Ok();
            result.CastId = id;
            return result;
        }

        /// <summary>推进冷却。</summary>
        private void TickCooldown(float deltaTime)
        {
            if (CooldownRemaining <= 0f)
            {
                return;
            }

            CooldownRemaining -= deltaTime;
            if (CooldownRemaining <= TimerTolerance(cooldownDuration))
            {
                CooldownRemaining = 0f;
                Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.CooldownFinished, Ability = this, Caster = Owner });
            }
        }

        /// <summary>推进充能次数。</summary>
        private void TickCharges(float deltaTime)
        {
            if (Definition.Charges == null || Charges >= Definition.Charges.MaxCharges || Definition.Charges.RestoreTime <= 0f)
            {
                return;
            }

            chargeRestoreRemaining -= deltaTime;
            while (chargeRestoreRemaining <= TimerTolerance(Definition.Charges.RestoreTime) && Charges < Definition.Charges.MaxCharges)
            {
                if (!Engine.TryConsumeExecutionBudget("ChargeRestore")) return;
                Charges++;
                chargeRestoreRemaining = Charges >= Definition.Charges.MaxCharges ? 0f : chargeRestoreRemaining + Definition.Charges.RestoreTime;
                if (Charges >= Definition.Charges.MaxCharges) ++chargeTimerVersion;
                int restoredVersion = chargeTimerVersion;
                Engine.RaiseEvent(new RuntimeEvent { EventType = RuntimeEventType.ChargeChanged, Ability = this, Caster = Owner, Value = Charges });
                if (!IsOperational || chargeTimerVersion != restoredVersion || Charges >= Definition.Charges.MaxCharges) return;
            }
        }

        /// <summary>验证 Ongoing 施法者。</summary>
        private CastResult ValidateOngoingCaster()
        {
            if (!Activated)
            {
                return CastResult.Fail(CastFailureReason.NotActivated);
            }

            if (Owner == null || !Owner.IsAlive)
            {
                return CastResult.Fail(CastFailureReason.DeadCaster);
            }

            if (Engine.HasState(Owner, UnitState.Stunned) || Engine.HasState(Owner, UnitState.Hexed))
            {
                return CastResult.Fail(CastFailureReason.Stunned);
            }

            if (Engine.HasState(Owner, UnitState.Silenced))
            {
                return CastResult.Fail(CastFailureReason.Silenced);
            }

            if ((Definition.Behavior & AbilityBehavior.RootDisables) != 0 &&
                Engine.HasState(Owner, UnitState.Rooted))
            {
                return CastResult.Fail(CastFailureReason.Rooted);
            }

            if (Engine.HasState(Owner, UnitState.CommandRestricted) || Engine.HasState(Owner, UnitState.OutOfGame))
            {
                return CastResult.Fail(CastFailureReason.CommandRestricted);
            }

            return CastResult.Ok();
        }

        /// <summary>应用固有状态修饰器。</summary>
        private void ApplyIntrinsicModifier()
        {
            // Intrinsics are permanent modifiers attached to passive or owned abilities.
            string modifierName = Script.GetIntrinsicModifierName();
            if (!IsOperational || Level <= 0 || Owner == null || !Owner.IsAlive) return;
            if (intrinsicModifier != null && intrinsicModifier.IsDestroyed) intrinsicModifier = null;
            if (intrinsicModifier != null && intrinsicModifier.Name != modifierName) RemoveIntrinsicModifier();
            if (string.IsNullOrEmpty(modifierName) || intrinsicModifier != null) return;
            intrinsicModifier = Engine.AddModifier(Owner, Owner, this, modifierName, new ModifierApplyOptions { Duration = -1f });
        }

        /// <summary>判断是否为 Same 单位。</summary>
        private static bool IsSameUnit(IUnit left, IUnit right)
        {
            return left != null && right != null && ReferenceEquals(left, right);
        }
    }
}


using UnityEngine;

namespace Game.Ability
{
    /// <summary>
    /// One modifier on one parent. Periodic work advances in lifetime order, so a refresh
    /// inside a callback takes effect at that callback's time, including at expiry.
    /// </summary>
    public sealed class Modifier
    {
        private const float TimeEpsilon = 0.000001f;
        public AbilitySystem Engine { get; }
        public string Name => Definition.Name;
        public ModifierDefinition Definition { get; }
        public ModifierScript Script { get; }
        public IUnit Caster { get; }
        public IUnit Parent { get; }
        public Ability Ability { get; }
        public int Level => Ability != null ? Ability.Level : sourceLevel;
        public Modifier SourceAura { get; }
        public bool IsAuraApplied { get; }
        public int StackCount { get; private set; }
        public float Duration { get; private set; }
        public float RemainingTime { get; private set; }
        public float ElapsedTime { get; private set; }
        public bool IsDestroyed { get; private set; }

        private float intervalTimer;
        private float auraTimer;
        private int sourceLevel;
        private readonly long runtimeGeneration;
        private bool isRegistered;
        private IPresentationHandle sustainedPresentation;

        private bool HasDuration => Duration > 0f && (Definition.Attributes & ModifierAttribute.Permanent) == 0;
        private bool HasInterval => Definition.Interval > 0f;
        private bool HasAura => !string.IsNullOrEmpty(Definition.AuraModifierName);
        private float AuraInterval => Mathf.Max(0.05f, Definition.AuraThinkInterval);
        internal bool IsRuntimeCurrent => Engine.RuntimeGeneration == runtimeGeneration;
        internal bool RequiresPropertyContext => !(Script is DefaultModifierScript) && !(Script is ConfiguredModifierScript);

        internal Modifier(AbilitySystem engine, ModifierDefinition definition, IUnit caster, IUnit parent, Ability ability, ModifierScript script, ModifierApplyOptions options)
        {
            Engine = engine;
            runtimeGeneration = engine.RuntimeGeneration;
            Definition = definition;
            Caster = caster;
            Parent = parent;
            Ability = ability;
            sourceLevel = Mathf.Max(1, options != null ? options.Level : 1);
            Script = script ?? new DefaultModifierScript();
            SourceAura = options != null ? options.SourceModifier : null;
            IsAuraApplied = options != null && options.IsAura;
            SetStackCount(options != null ? options.StackCount : 1);
            Duration = ResolveDuration(definition, options);
            RemainingTime = Duration;
            intervalTimer = definition.Interval;
            auraTimer = AuraInterval;
            Script.Bind(this);
        }

        // Called only after the engine owns this instance. OnCreated can now remove itself,
        // query its own properties, or recursively apply another modifier safely.
        internal void OnRegistered(ModifierApplyOptions options)
        {
            if (isRegistered || IsDestroyed) return;
            isRegistered = true;
            Engine.RegisterAuraChild(this);
            Script.OnCreated(options);
            if (!CanContinue()) return;

            if (!string.IsNullOrEmpty(Definition.SustainedEffectName) && Engine.Presentation != null)
            {
                IPresentationHandle handle = Engine.Presentation.PlayPersistentEffect(Definition.SustainedEffectName, Parent);
                if (IsDestroyed || !Engine.IsRuntimeAvailable) handle?.Stop();
                else sustainedPresentation = handle;
            }

            if (CanContinue() && HasAura && Engine.TryConsumeExecutionBudget("Aura refresh"))
            {
                Engine.RefreshAura(this);
            }
        }

        public void SetStackCount(int value)
        {
            StackCount = Mathf.Clamp(value, 1, Mathf.Max(1, Definition.MaxStack));
        }

        public void IncrementStack(int count = 1)
        {
            // Saturate before adding to avoid integer overflow for caller-supplied counts.
            long next = (long)StackCount + Mathf.Max(1, count);
            SetStackCount((int)System.Math.Min(int.MaxValue, next));
        }

        public void Refresh(ModifierApplyOptions options)
        {
            if (options != null && float.IsInfinity(options.Duration)) throw new System.ArgumentOutOfRangeException(nameof(options), "Modifier duration must be finite, or NaN to use its definition.");
            if (IsDestroyed) return;
            using (Engine.BeginExecution())
            {
                if (options != null) sourceLevel = Mathf.Max(1, options.Level);
                if ((Definition.Attributes & ModifierAttribute.NoDurationRefresh) == 0)
                {
                    Duration = ResolveDuration(Definition, options);
                    RemainingTime = Duration;
                }

                if (Definition.MaxStack > 1 && (options == null || options.IncrementStacksOnRefresh))
                {
                    IncrementStack(options != null ? options.StackCount : 1);
                }

                if (Engine.TryConsumeExecutionBudget("Modifier refresh")) Script.OnRefresh(options);
            }
        }

        internal void Tick(float deltaTime)
        {
            using (Engine.BeginExecution())
            {
                TickTimeline(deltaTime, Engine.HasAuraLease(this));
            }
        }

        // Aura coverage is sampled before any object ticks. A covered child still runs its
        // own interval/regen timeline, but its duration represents linger after coverage ends.
        internal void RenewAuraLease()
        {
            if (HasDuration) RemainingTime = Duration;
        }

        private void TickTimeline(float deltaTime, bool hasAuraLease)
        {
            if (!CanContinue() || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;

            float unprocessedTime = deltaTime;
            int callbacks = 0;
            while (unprocessedTime > 0f && CanContinue())
            {
                float step = HasDuration && !hasAuraLease ? Mathf.Min(unprocessedTime, Mathf.Max(0f, RemainingTime)) : unprocessedTime;
                if (HasInterval) step = Mathf.Min(step, Mathf.Max(0f, intervalTimer));
                if (HasAura) step = Mathf.Min(step, Mathf.Max(0f, auraTimer));

                AdvanceTime(step, hasAuraLease);
                unprocessedTime = Mathf.Max(0f, unprocessedTime - step);
                if (!CanContinue()) return;

                bool intervalDue = HasInterval && IsDue(intervalTimer, Definition.Interval);
                bool auraDue = HasAura && IsDue(auraTimer, AuraInterval);
                if (intervalDue || auraDue)
                {
                    // Excess historical callbacks are skipped, and their timers still advance.
                    // This bounds long-frame work without accumulating permanent callback debt.
                    if (callbacks >= Mathf.Max(1, Engine.MaxPeriodicCallbacksPerUpdate))
                    {
                        SkipTime(unprocessedTime, hasAuraLease);
                        return;
                    }

                    if (intervalDue)
                    {
                        intervalTimer = Definition.Interval;
                        if (!Engine.TryConsumeExecutionBudget("Modifier interval"))
                        {
                            SkipTime(unprocessedTime, hasAuraLease);
                            return;
                        }

                        callbacks++;
                        Script.OnIntervalThink();
                        if (!CanContinue()) return;
                    }

                    if (auraDue && HasAura)
                    {
                        auraTimer = AuraInterval;
                        if (callbacks >= Mathf.Max(1, Engine.MaxPeriodicCallbacksPerUpdate) || !Engine.TryConsumeExecutionBudget("Aura refresh"))
                        {
                            SkipTime(unprocessedTime, hasAuraLease);
                            return;
                        }

                        callbacks++;
                        Engine.RefreshAura(this);
                        if (!CanContinue()) return;
                    }
                }

                // Boundary callbacks run before expiry. Refresh may have installed a new lifetime.
                if (HasDuration && !hasAuraLease && IsDue(RemainingTime, Duration))
                {
                    Engine.RemoveModifier(this);
                    return;
                }
            }
        }

        internal void Destroy()
        {
            if (IsDestroyed) return;
            IsDestroyed = true;
            Engine.UnregisterAuraChild(this);
            IPresentationHandle handle = sustainedPresentation;
            sustainedPresentation = null;
            try { handle?.Stop(); }
            finally { Script.OnDestroy(); }
        }

        internal float GetProperty(ModifierProperty property, ModifierPropertyContext context)
        {
            return Script.GetProperty(property, context);
        }

        internal bool CheckState(UnitState state)
        {
            return Script.CheckState(state);
        }

        private bool CanContinue()
        {
            if (IsDestroyed || !Engine.IsRuntimeAvailable || !IsRuntimeCurrent) return false;
            if (Parent != null && (Parent.IsAlive || !Definition.RemoveOnDeath)) return true;
            Engine.RemoveModifier(this);
            return false;
        }

        private void AdvanceTime(float deltaTime, bool hasAuraLease)
        {
            if (deltaTime <= 0f) return;
            ElapsedTime += deltaTime;
            if (HasDuration && !hasAuraLease) RemainingTime -= deltaTime;
            if (HasInterval) intervalTimer -= deltaTime;
            if (HasAura) auraTimer -= deltaTime;

            // Regeneration belongs to active time, not a frame extending beyond expiry.
            if (Parent != null && Parent.IsAlive)
            {
                ModifierPropertyContext context = RequiresPropertyContext ? new ModifierPropertyContext { Engine = Engine, Unit = Parent, Ability = Ability } : null;
                float regeneration = GetProperty(ModifierProperty.HealthRegen, context);
                if (regeneration > 0f && !float.IsNaN(regeneration) && !float.IsInfinity(regeneration) && CanContinue())
                {
                    Engine.Heal(new HealInfo { Source = Caster, Target = Parent, Ability = Ability, Amount = regeneration * deltaTime });
                }
            }
        }

        private void SkipTime(float deltaTime, bool hasAuraLease)
        {
            float activeTime = HasDuration && !hasAuraLease ? Mathf.Min(deltaTime, Mathf.Max(0f, RemainingTime)) : deltaTime;
            AdvanceTime(activeTime, hasAuraLease);
            if (!CanContinue()) return;
            if (HasInterval && intervalTimer <= 0f) intervalTimer = NextTimer(intervalTimer, Definition.Interval);
            if (HasAura && auraTimer <= 0f) auraTimer = NextTimer(auraTimer, AuraInterval);
            if (HasDuration && !hasAuraLease && IsDue(RemainingTime, Duration)) Engine.RemoveModifier(this);
        }

        private static bool IsDue(float timer, float period)
        {
            return timer <= Mathf.Min(TimeEpsilon, period * 0.00001f);
        }

        private static float NextTimer(float timer, float interval)
        {
            float remainder = -timer % interval;
            return remainder == 0f ? interval : interval - remainder;
        }

        private static float ResolveDuration(ModifierDefinition definition, ModifierApplyOptions options)
        {
            return options != null && !float.IsNaN(options.Duration) ? options.Duration : definition.Duration;
        }
    }
}

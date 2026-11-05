using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Game.Ability
{
    /// <summary>
    /// Executes simple data-driven actions. Custom AbilityScript/ModifierScript code can still
    /// call the same primitives when a skill needs logic beyond config.
    /// </summary>
    public static class ActionRunner
    {
        public static void Execute(IReadOnlyList<ActionDefinition> actions, CastContext context)
        {
            if (actions == null || context == null)
            {
                return;
            }

            if (context.Engine == null) return;
            using (context.Engine.BeginExecution())
            {
                ActionLifetime lifetime = new ActionLifetime(context);
                for (int i = 0; i < actions.Count && lifetime.IsValid; i++)
                {
                    if (!ExecuteWithBudget(actions[i], context)) break;
                }
            }
        }

        public static void Execute(ActionDefinition action, CastContext context)
        {
            if (action == null || context == null || context.Engine == null || !context.Engine.IsRuntimeAvailable)
            {
                return;
            }

            using (context.Engine.BeginExecution())
            {
                ExecuteWithBudget(action, context);
            }
        }

        private static bool ExecuteWithBudget(ActionDefinition action, CastContext context)
        {
            if (action == null) return true;
            if (!new ActionLifetime(context).IsValid) return false;
            if (!context.Engine.TryConsumeExecutionBudget("Configured action")) return false;
            ExecuteAction(action, context);
            return true;
        }

        private static void ExecuteAction(ActionDefinition action, CastContext context)
        {
            ActionLifetime lifetime = new ActionLifetime(context);
            // Every execution rents its own target list. Nested modifier/projectile callbacks may
            // execute another action immediately, so a shared static scratch list is not reentrant.
            List<IUnit> targets = ListPool<IUnit>.Get();
            try
            {
                ResolveTargets(action.Target, context, targets);

                // ActionType performs the gameplay operation; optional VFX/SFX are also played below.
                switch (action.ActionType)
                {
                    case ActionType.Damage:
                        ExecuteDamage(action, context, targets);
                        break;
                    case ActionType.Heal:
                        ExecuteHeal(action, context, targets);
                        break;
                    case ActionType.AddModifier:
                        ExecuteAddModifier(action, context, targets);
                        break;
                    case ActionType.Purge:
                        ExecutePurge(action, context, targets);
                        break;
                    case ActionType.CreateTrackingProjectile:
                        ExecuteTrackingProjectile(action, context, targets);
                        break;
                    case ActionType.CreateLinearProjectile:
                        ExecuteLinearProjectile(action, context);
                        break;
                    case ActionType.PlayEffect:
                        ExecuteEffect(action, context, targets);
                        break;
                    case ActionType.PlaySound:
                        ExecuteSound(action, context);
                        break;
                }

                if (lifetime.IsValid && action.ActionType != ActionType.PlayEffect)
                {
                    ExecuteEffect(action, context, targets);
                }

                if (lifetime.IsValid && action.ActionType != ActionType.PlaySound)
                {
                    ExecuteSound(action, context);
                }
            }
            finally
            {
                ListPool<IUnit>.Release(targets);
            }
        }

        public static CastContext CreateSingleTargetContext(AbilitySystem engine, Ability ability, IUnit caster, IUnit target, Vector3 position)
        {
            CastContext context = new CastContext
            {
                Engine = engine,
                Ability = ability,
                Level = ability != null ? ability.Level : 1,
                Caster = caster,
                Target = target,
                TargetPosition = target != null ? target.Position : position
            };

            if (target != null)
            {
                context.AddTarget(target);
            }

            return context;
        }

        private static void ExecuteDamage(ActionDefinition action, CastContext context, List<IUnit> targets)
        {
            ActionLifetime lifetime = new ActionLifetime(context);
            float amount = action.ResolveValue(context);
            for (int i = 0; i < targets.Count && lifetime.IsValid; i++)
            {
                context.Engine.ApplyDamage(new DamageInfo
                {
                    Engine = context.Engine,
                    Attacker = context.Caster,
                    Victim = targets[i],
                    Ability = context.Ability,
                    Amount = amount,
                    DamageType = action.DamageType,
                    Flags = action.DamageFlags
                });
            }
        }

        private static void ExecuteHeal(ActionDefinition action, CastContext context, List<IUnit> targets)
        {
            ActionLifetime lifetime = new ActionLifetime(context);
            float amount = action.ResolveValue(context);
            for (int i = 0; i < targets.Count && lifetime.IsValid; i++)
            {
                context.Engine.Heal(new HealInfo
                {
                    Source = context.Caster,
                    Target = targets[i],
                    Ability = context.Ability,
                    Amount = amount
                });
            }
        }

        private static void ExecuteAddModifier(ActionDefinition action, CastContext context, List<IUnit> targets)
        {
            if (string.IsNullOrEmpty(action.ModifierName))
            {
                return;
            }

            float duration = action.ResolveDuration(context);
            ActionLifetime lifetime = new ActionLifetime(context);
            for (int i = 0; i < targets.Count && lifetime.IsValid; i++)
            {
                context.Engine.AddModifier(context.Caster, targets[i], context.Ability, action.ModifierName, new ModifierApplyOptions { Duration = duration, Level = Mathf.Max(1, context.Level) });
            }
        }

        private static void ExecutePurge(ActionDefinition action, CastContext context, List<IUnit> targets)
        {
            ActionLifetime lifetime = new ActionLifetime(context);
            for (int i = 0; i < targets.Count && lifetime.IsValid; i++)
            {
                context.Engine.Purge(targets[i], action.PurgePositiveBuffs, action.PurgeDebuffs, action.PurgeOnlyPurgable);
            }
        }

        private static void ExecuteTrackingProjectile(ActionDefinition action, CastContext context, List<IUnit> targets)
        {
            if (action.Projectile == null)
            {
                return;
            }

            ActionLifetime lifetime = new ActionLifetime(context);
            for (int i = 0; i < targets.Count && lifetime.IsValid; i++)
            {
                context.Engine.CreateTrackingProjectile(context.Ability, context.Caster, targets[i], action.Projectile);
            }
        }

        private static void ExecuteLinearProjectile(ActionDefinition action, CastContext context)
        {
            if (action.Projectile == null)
            {
                return;
            }

            Vector3 direction = context.Direction;
            if (direction.sqrMagnitude <= 0.0001f && context.Caster != null)
            {
                direction = context.TargetPosition - context.Caster.Position;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = Vector3.forward;
            }

            Vector3 origin = context.Caster != null ? context.Caster.Position : context.Modifier != null && context.Modifier.Parent != null ? context.Modifier.Parent.Position : context.TargetPosition;
            context.Engine.CreateLinearProjectile(context.Ability, context.Caster, origin, direction, action.Projectile);
        }

        private static void ExecuteEffect(ActionDefinition action, CastContext context, List<IUnit> targets)
        {
            if (context.Engine.Presentation == null || string.IsNullOrEmpty(action.EffectName))
            {
                return;
            }

            if (targets.Count == 0 || action.Target == ActionTarget.Point)
            {
                context.Engine.Presentation.PlayEffect(action.EffectName, ResolvePoint(action.Target, context));
                return;
            }

            ActionLifetime lifetime = new ActionLifetime(context);
            for (int i = 0; i < targets.Count && lifetime.IsValid; i++)
            {
                context.Engine.Presentation.PlayEffect(action.EffectName, targets[i]);
            }
        }

        private static void ExecuteSound(ActionDefinition action, CastContext context)
        {
            if (context.Engine.Presentation == null || string.IsNullOrEmpty(action.SoundName))
            {
                return;
            }

            context.Engine.Presentation.PlaySound(action.SoundName, ResolvePoint(action.Target, context));
        }

        private static void ResolveTargets(ActionTarget target, CastContext context, List<IUnit> targets)
        {
            // ContextTargets are pre-filtered by Targeting for AOE/no-target casts.
            targets.Clear();
            switch (target)
            {
                case ActionTarget.Caster:
                    AddTarget(context.Caster, targets);
                    break;
                case ActionTarget.Parent:
                    AddTarget(context.Modifier != null ? context.Modifier.Parent : null, targets);
                    break;
                case ActionTarget.EventSource:
                    AddTarget(context.Event != null ? context.Event.Source : null, targets);
                    break;
                case ActionTarget.EventTarget:
                    AddTarget(context.Event != null ? context.Event.Target : null, targets);
                    break;
                case ActionTarget.PrimaryTarget:
                    AddTarget(context.Target, targets);
                    break;
                case ActionTarget.ContextTargets:
                    for (int i = 0; i < context.Targets.Count; i++)
                    {
                        AddTarget(context.Targets[i], targets);
                    }
                    break;
            }
        }

        private static Vector3 ResolvePoint(ActionTarget target, CastContext context)
        {
            switch (target)
            {
                case ActionTarget.Caster:
                    return context.Caster != null ? context.Caster.Position : context.TargetPosition;
                case ActionTarget.Parent:
                    return context.Modifier != null && context.Modifier.Parent != null ? context.Modifier.Parent.Position : context.TargetPosition;
                case ActionTarget.EventSource:
                    return context.Event != null && context.Event.Source != null ? context.Event.Source.Position : context.TargetPosition;
                case ActionTarget.EventTarget:
                    return context.Event != null && context.Event.Target != null ? context.Event.Target.Position : context.TargetPosition;
                case ActionTarget.PrimaryTarget:
                    return context.Target != null ? context.Target.Position : context.TargetPosition;
                default:
                    return context.TargetPosition;
            }
        }

        private static void AddTarget(IUnit target, List<IUnit> targets)
        {
            if (target == null) return;
            for (int i = 0; i < targets.Count; i++)
            {
                if (ReferenceEquals(targets[i], target) || targets[i].EntityId == target.EntityId) return;
            }

            targets.Add(target);
        }

        // Destroy actions intentionally start with a destroyed modifier. Other lifecycle
        // actions stop if their source is removed by an earlier callback in the same list.
        private readonly struct ActionLifetime
        {
            private readonly CastContext context;
            private readonly long generation;
            private readonly Modifier modifier;
            private readonly bool allowDestroyedModifier;

            public ActionLifetime(CastContext context)
            {
                this.context = context;
                generation = context.Engine.RuntimeGeneration;
                modifier = context.Modifier;
                allowDestroyedModifier = modifier != null && modifier.IsDestroyed;
            }

            public bool IsValid => context.Engine.IsRuntimeAvailable && context.Engine.RuntimeGeneration == generation &&
                (context.Projectile != null ? !context.Projectile.Destroyed && context.Engine.ContainsProjectile(context.Projectile) && !context.Engine.IsUnitRemoving(context.Caster) :
                    modifier != null ? modifier.IsRuntimeCurrent && (allowDestroyedModifier || !modifier.IsDestroyed) :
                        (context.Ability == null || !context.Ability.IsRemoved) && !context.Engine.IsUnitRemoving(context.Caster));
        }
    }
}


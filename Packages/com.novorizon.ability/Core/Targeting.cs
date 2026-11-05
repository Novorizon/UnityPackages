using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    /// <summary>
    /// Canonical target filter shared by casts, auras, projectiles, and business helpers.
    /// </summary>
    public sealed class TargetQuery
    {
        public AbilitySystem Engine;
        public IUnit Caster;
        public TargetTeam Team;
        public UnitType Types = UnitType.All;
        public TargetFlags Flags = TargetFlags.None;

        public bool IsValid(IUnit target)
        {
            if (target == null || (Engine != null && (Engine.IsUnitRemoving(target) || Engine.HasTargetState(target, UnitState.OutOfGame))))
            {
                return false;
            }

            if (Caster != null)
            {
                if ((Flags & TargetFlags.ExcludeSelf) != 0 && target.EntityId == Caster.EntityId)
                {
                    return false;
                }

                if ((Flags & TargetFlags.IncludeSelf) == 0 && target.EntityId == Caster.EntityId && Team == TargetTeam.Enemy)
                {
                    return false;
                }
            }

            if (!target.IsAlive && (Flags & TargetFlags.Dead) == 0)
            {
                return false;
            }

            bool magicImmune = Engine != null ? Engine.HasTargetState(target, UnitState.MagicImmune) : target.IsMagicImmune;
            if (magicImmune && (Flags & TargetFlags.MagicImmuneEnemies) == 0 && Caster != null && Caster.TeamId != target.TeamId)
            {
                return false;
            }

            bool invulnerable = Engine != null ? Engine.HasTargetState(target, UnitState.Invulnerable) : target.IsInvulnerable;
            if (invulnerable && (Flags & TargetFlags.Invulnerable) == 0)
            {
                return false;
            }

            bool untargetable = Engine != null && Engine.HasTargetState(target, UnitState.Untargetable);
            if (untargetable && (Flags & TargetFlags.Untargetable) == 0)
            {
                return false;
            }

            if ((target.UnitType & Types) == 0)
            {
                return false;
            }

            if (!IsTeamValid(target))
            {
                return false;
            }

            if ((Flags & TargetFlags.VisibleOnly) != 0 && Caster != null && !target.IsVisibleToTeam(Caster.TeamId))
            {
                return false;
            }

            return true;
        }

        private bool IsTeamValid(IUnit target)
        {
            if (Caster == null)
            {
                return Team != TargetTeam.None;
            }

            bool sameTeam = target.TeamId == Caster.TeamId;
            return (sameTeam && (Team & TargetTeam.Friendly) != 0) || (!sameTeam && (Team & TargetTeam.Enemy) != 0);
        }
    }

    /// <summary>
    /// Builds a cast context from a raw order and validates Dota-like targeting rules.
    /// </summary>
    public static class Targeting
    {
        /// <summary>Copies only the target shape supported by the definition.</summary>
        internal static CastOrder NormalizeOrder(Ability ability, CastOrder source)
        {
            CastOrder order = AbilitySystem.CopyOrder(source);
            AbilityBehavior behavior = ability.Definition.Behavior;
            bool useUnit = (behavior & (AbilityBehavior.UnitTarget | AbilityBehavior.OptionalUnitTarget)) != 0 && order.Target != null;
            bool usePoint = !useUnit && (behavior & AbilityBehavior.PointTarget) != 0 && order.HasTargetPosition;
            if ((behavior & AbilityBehavior.NoTarget) != 0 || (!useUnit && !usePoint))
            {
                order.Target = null;
                order.TargetPosition = order.Caster.Position;
                order.HasTargetPosition = false;
            }
            else if (useUnit)
            {
                order.TargetPosition = order.Target.Position;
                order.HasTargetPosition = false;
            }
            else order.Target = null;
            if ((behavior & AbilityBehavior.Directional) == 0) order.Direction = Vector3.zero;
            else order.Direction = order.Direction.normalized;
            return order;
        }

        public static bool BuildCastContext(AbilitySystem engine, Ability ability, CastOrder order, out CastContext context, out CastResult result)
        {
            context = null;
            result = CastResult.Fail(CastFailureReason.InvalidTarget);
            if (engine == null || ability == null || order == null || !ReferenceEquals(order.Caster, ability.Owner) || order.AbilityName != ability.Definition.Name) return false;
            if (!IsFinite(order.Caster.Position) || !IsFinite(order.Direction) || (order.HasTargetPosition && !IsFinite(order.TargetPosition))) return false;

            AbilityDefinition definition = ability.Definition;
            AbilityBehavior behavior = definition.Behavior;
            bool noTarget = (behavior & AbilityBehavior.NoTarget) != 0;
            bool unitTarget = !noTarget && (behavior & (AbilityBehavior.UnitTarget | AbilityBehavior.OptionalUnitTarget)) != 0 && order.Target != null;
            bool pointTarget = !noTarget && !unitTarget && (behavior & AbilityBehavior.PointTarget) != 0 && order.HasTargetPosition;
            bool optionalUnit = !noTarget && !unitTarget && !pointTarget && (behavior & AbilityBehavior.OptionalUnitTarget) != 0;
            if (!noTarget && !unitTarget && !pointTarget && !optionalUnit) return false;

            context = new CastContext
            {
                Engine = engine,
                Ability = ability,
                Caster = ability.Owner,
                Target = unitTarget ? order.Target : null,
                TargetPosition = unitTarget ? order.Target.Position : pointTarget ? order.TargetPosition : ability.Owner.Position,
                Direction = (behavior & AbilityBehavior.Directional) != 0 ? order.Direction.normalized : Vector3.zero,
                Level = ability.Level
            };
            if (!IsFinite(context.TargetPosition)) return false;
            TargetQuery query = CreateQuery(ability);
            float castRange = Mathf.Max(0f, ability.Script.GetCastRange() + engine.GetModifierProperty(ability.Owner, ModifierProperty.CastRangeBonus, null));
            float aoeRadius = Mathf.Max(0f, ability.Script.GetAoeRadius());
            if (!IsFinite(castRange) || !IsFinite(aoeRadius)) return false;

            if (unitTarget && !query.IsValid(context.Target)) return false;
            if ((unitTarget || pointTarget) && !IsInRange(ability.Owner.Position, context.TargetPosition, castRange))
            {
                result = CastResult.Fail(CastFailureReason.OutOfRange);
                return false;
            }
            if ((unitTarget || pointTarget) && !HasLineOfSight(engine, ability.Owner, context.TargetPosition, definition.TargetFlags))
            {
                result = CastResult.Fail(CastFailureReason.NoVision);
                return false;
            }
            if (unitTarget) context.AddTarget(context.Target);
            if ((noTarget || (behavior & AbilityBehavior.Aoe) != 0) && aoeRadius > 0f) AddAreaTargets(engine, context, query, context.TargetPosition, aoeRadius);
            result = CastResult.Ok();
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        public static TargetQuery CreateQuery(Ability ability)
        {
            AbilityDefinition definition = ability.Definition;
            return new TargetQuery
            {
                Engine = ability.Engine,
                Caster = ability.Owner,
                Team = definition.TargetTeam,
                Types = definition.TargetType,
                Flags = definition.TargetFlags
            };
        }

        private static void AddAreaTargets(AbilitySystem engine, CastContext context, TargetQuery query, Vector3 center, float radius)
        {
            if (engine.World == null || radius <= 0f)
            {
                return;
            }

            List<IUnit> results = new List<IUnit>();
            engine.FindUnits(center, radius, query, results);
            for (int i = 0; i < results.Count; i++)
            {
                context.AddTarget(results[i]);
            }
        }

        private static bool HasLineOfSight(AbilitySystem engine, IUnit caster, Vector3 position, TargetFlags flags)
        {
            return (flags & TargetFlags.IgnoreLineOfSight) != 0 || engine.World == null || engine.World.HasLineOfSight(caster, position);
        }

        private static bool IsInRange(Vector3 from, Vector3 to, float range)
        {
            return range <= 0f || (to - from).sqrMagnitude <= range * range;
        }
    }
}


using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    /// <summary>
    /// Owns movement, swept collision and a bounded lifetime. Hit effects remain script-owned.
    /// Linear projectiles pierce when DeleteOnHit is false; tracking projectiles always terminate
    /// on arrival because they have one destination, not a persistent area.
    /// </summary>
    public sealed class Projectile
    {
        public ProjectileDefinition Definition { get; }
        public Ability Ability { get; }
        public IUnit Caster { get; }
        public IUnit Source { get; }
        public IUnit Target { get; }
        public Vector3 Position { get; private set; }
        public Vector3 Direction { get; }
        public bool Tracking { get; }
        public bool Destroyed { get; private set; }

        private readonly HashSet<int> hitUnitIds = new HashSet<int>();
        private readonly List<IUnit> candidates = new List<IUnit>();
        private readonly List<SweptHit> hits = new List<SweptHit>();
        private readonly float maximumLifetime;
        private float elapsedTime;
        private float traveledDistance;
        private bool ticking;
        private IProjectilePresentationHandle presentationHandle;

        internal Projectile(ProjectileRequest request)
        {
            Definition = request.Definition;
            Ability = request.Ability;
            Caster = request.Caster;
            Source = request.Source;
            Target = request.Target;
            Position = request.Origin;
            Direction = request.Direction.sqrMagnitude > 0.0001f ? AbilityGeometry.Normalize(request.Direction) : Vector3.forward;
            Tracking = request.Tracking;
            maximumLifetime = IsFinite(Definition.MaxLifetime) && Definition.MaxLifetime > 0f ? Definition.MaxLifetime : 10f;
        }

        internal void OnRegistered(IPresentation presentation)
        {
            if (Destroyed || presentation == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(Definition.EffectName))
            {
                if (presentation is IProjectilePresentation projectilePresentation)
                {
                    IProjectilePresentationHandle handle = projectilePresentation.PlayProjectileEffect(Definition.EffectName, this);
                    if (Destroyed)
                    {
                        handle?.Stop();
                        return;
                    }

                    presentationHandle = handle;
                    presentationHandle?.SetPosition(Position);
                }
                else
                {
                    presentation.PlayEffect(Definition.EffectName, Position);
                }
            }

            if (!Destroyed && !string.IsNullOrEmpty(Definition.SoundName))
            {
                presentation.PlaySound(Definition.SoundName, Position);
            }
        }

        internal void Tick(AbilitySystem engine, float deltaTime)
        {
            if (Destroyed || ticking || !IsFinite(deltaTime) || deltaTime <= 0f)
            {
                return;
            }

            ticking = true;
            try
            {
                float activeDelta = Mathf.Min(deltaTime, Mathf.Max(0f, maximumLifetime - elapsedTime));
                if (activeDelta <= 0f)
                {
                    Destroy();
                    return;
                }

                if (Tracking)
                {
                    TickTracking(engine, activeDelta);
                }
                else
                {
                    TickLinear(engine, activeDelta);
                }

                if (!Destroyed)
                {
                    elapsedTime += activeDelta;
                    if (elapsedTime >= maximumLifetime) Destroy();
                }
            }
            finally
            {
                ticking = false;
            }
        }

        internal void Destroy()
        {
            if (Destroyed)
            {
                return;
            }

            Destroyed = true;
            IProjectilePresentationHandle handle = presentationHandle;
            presentationHandle = null;
            try { handle?.Stop(); }
            finally
            {
                candidates.Clear();
                hits.Clear();
            }
        }

        private void TickTracking(AbilitySystem engine, float deltaTime)
        {
            TargetQuery query = Definition.TrackingTargetPolicy == TrackingTargetPolicy.RevalidateTarget ? CreateQuery(engine) : null;
            if (Target == null || !Target.IsAlive ||
                (query != null && !query.IsValid(Target)))
            {
                Destroy();
                return;
            }

            Vector3 targetPosition = Target.Position;
            if (!IsFinite(targetPosition)) { Destroy(); return; }
            double x = (double)targetPosition.x - Position.x;
            double y = (double)targetPosition.y - Position.y;
            double z = (double)targetPosition.z - Position.z;
            double distance = Math.Sqrt(x * x + y * y + z * z);
            float remainingDistance = RemainingDistance();
            float step = Mathf.Min(SafeNonnegative(Definition.Speed) * deltaTime, remainingDistance);
            if (distance <= step)
            {
                traveledDistance += (float)distance;
                SetPosition(targetPosition);
                Hit(engine, Target, query);
                // DeleteOnHit controls piercing linear projectiles. Tracking arrival is terminal.
                Destroy();
                return;
            }

            double fraction = step / distance;
            SetPosition(new Vector3(
                (float)(Position.x + x * fraction),
                (float)(Position.y + y * fraction),
                (float)(Position.z + z * fraction)));
            if (Destroyed) return;
            traveledDistance += step;
            if (RemainingDistance() <= 0f)
            {
                Destroy();
            }
        }

        private void TickLinear(AbilitySystem engine, float deltaTime)
        {
            Vector3 start = Position;
            float step = Mathf.Min(SafeNonnegative(Definition.Speed) * deltaTime, RemainingDistance());
            Vector3 end = start + Direction * step;
            float radius = SafeNonnegative(Definition.Radius);
            TargetQuery query = CreateQuery(engine);

            candidates.Clear();
            hits.Clear();
            // Every point in the swept capsule lies inside this broad sphere. The narrow phase
            // then orders actual intersections by distance, independent of world enumeration order.
            engine.FindUnits(start + Direction * (step * 0.5f), step * 0.5f + radius, query, candidates);
            if (Destroyed) return;
            for (int i = 0; i < candidates.Count; i++)
            {
                IUnit unit = candidates[i];
                if (unit == null || hitUnitIds.Contains(unit.EntityId))
                {
                    continue;
                }

                if (TryIntersect(start, Direction, step, unit.Position, radius, out float distance))
                {
                    hits.Add(new SweptHit { Unit = unit, Distance = distance });
                }
            }

            hits.Sort(CompareHits);
            for (int i = 0; i < hits.Count; i++)
            {
                SweptHit hit = hits[i];
                // Previous hit callbacks may kill, remove or change the next candidate.
                if (!query.IsValid(hit.Unit))
                {
                    continue;
                }

                SetPosition(start + Direction * hit.Distance);
                Hit(engine, hit.Unit, query);
                if (Destroyed)
                {
                    return;
                }
            }

            traveledDistance += step;
            SetPosition(end);
            if (RemainingDistance() <= 0f)
            {
                Destroy();
            }
        }

        private void Hit(AbilitySystem engine, IUnit target, TargetQuery query = null)
        {
            // Moving presentation is an external callback. It may kill or invalidate a
            // candidate after the sweep/arrival check but before gameplay hit callbacks.
            if (Destroyed || target == null || !target.IsAlive || !IsFinite(target.Position) ||
                engine.IsUnitRemoving(target) || !engine.IsAbilitySourceValid(Ability) ||
                (query != null && !query.IsValid(target)) || Destroyed || !hitUnitIds.Add(target.EntityId))
            {
                return;
            }

            bool destroy = Definition.DeleteOnHit;
            if (Ability != null && Ability.Script != null)
            {
                destroy = Ability.Script.OnProjectileHit(this, target, Position) || destroy;
            }

            // Scripts may remove this projectile, its caster or the entire battle. No later hit
            // notifications or gameplay callbacks belong to an already terminated projectile.
            if (Destroyed)
            {
                return;
            }

            engine.OnProjectileHit(this, target, Position);
            if (destroy)
            {
                Destroy();
            }
        }

        private TargetQuery CreateQuery(AbilitySystem engine)
        {
            return new TargetQuery { Engine = engine, Caster = Caster, Team = Definition.TargetTeam, Types = Definition.TargetType, Flags = Definition.TargetFlags };
        }

        private float RemainingDistance()
        {
            return Mathf.Max(0f, SafeNonnegative(Definition.Distance) - traveledDistance);
        }

        private void SetPosition(Vector3 position)
        {
            if (!IsFinite(position)) { Destroy(); return; }
            Position = position;
            if (!Destroyed)
            {
                presentationHandle?.SetPosition(position);
            }
        }

        private static bool TryIntersect(Vector3 origin, Vector3 direction, float length, Vector3 center, float radius, out float distance)
        {
            double x = (double)origin.x - center.x;
            double y = (double)origin.y - center.y;
            double z = (double)origin.z - center.z;
            double radiusSquared = (double)radius * radius;
            if (x * x + y * y + z * z <= radiusSquared)
            {
                distance = 0f;
                return true;
            }

            double directionSquared = (double)direction.x * direction.x + (double)direction.y * direction.y + (double)direction.z * direction.z;
            if (directionSquared == 0)
            {
                distance = 0;
                return false;
            }
            double crossX = y * direction.z - z * direction.y;
            double crossY = z * direction.x - x * direction.z;
            double crossZ = x * direction.y - y * direction.x;
            // Perpendicular distance avoids subtracting two nearly equal squared
            // distances, which can turn a long off-axis miss into a false hit.
            double perpendicularSquared = (crossX * crossX + crossY * crossY + crossZ * crossZ) / directionSquared;
            if (perpendicularSquared > radiusSquared)
            {
                distance = 0f;
                return false;
            }

            double along = -(x * direction.x + y * direction.y + z * direction.z) / directionSquared;
            double hitDistance = along - Math.Sqrt((radiusSquared - perpendicularSquared) / directionSquared);
            distance = (float)hitDistance;
            return hitDistance >= 0 && hitDistance <= length;
        }

        private static int CompareHits(SweptHit left, SweptHit right)
        {
            int distanceOrder = left.Distance.CompareTo(right.Distance);
            return distanceOrder != 0 ? distanceOrder : left.Unit.EntityId.CompareTo(right.Unit.EntityId);
        }

        private static float SafeNonnegative(float value)
        {
            return IsFinite(value) ? Mathf.Max(0f, value) : 0f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private struct SweptHit
        {
            public IUnit Unit;
            public float Distance;
        }
    }
}

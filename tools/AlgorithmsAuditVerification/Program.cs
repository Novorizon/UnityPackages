using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Game.Ability;
using RTree;
using UnityEngine;

internal static class Program
{
    private static int checks;
    private static long sink;

    private static int Main(string[] args)
    {
        try
        {
            if (Array.IndexOf(args, "--measure-only") < 0)
            {
                CastRangeBoundaries();
                ProjectileSweepRejectsOffAxisCandidate();
                TrackingMustNotArriveBeforeItsStep();
                LargeProjectileDirectionsStayNormalized();
                AuraLeaseEndsOutsideALargeFiniteRadius();
                SnapshotPoolAccountsForCapacityAndNestedRentals();
                WarmUpdatesDoNotAllocate(1000);
                WarmUpdatesDoNotAllocate(4096);
                WarmUpdatesDoNotAllocate(4097);
                WarmUpdatesDoNotAllocate(10000);
                Console.WriteLine("PASS " + checks + " algorithm audit regression checks.");
            }
            if (Array.IndexOf(args, "--measure") >= 0 || Array.IndexOf(args, "--measure-only") >= 0)
            {
                MeasureAbilityUpdates(1000);
                MeasureAbilityUpdates(4096);
                MeasureAbilityUpdates(4097);
                MeasureAbilityUpdates(10000);
                MeasureSpatialQueries();
            }
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Require(bool value, string message)
    {
        checks++;
        if (!value)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void CastRangeBoundaries()
    {
        RangeCase(Vector3.zero, new Vector3(1e21f, 0, 0), 1e20f, false, "large finite range");
        RangeCase(Vector3.zero, new Vector3(2e-30f, 0, 0), 1e-30f, false, "tiny finite range");
        RangeCase(new Vector3(-3e38f, 0, 0), new Vector3(3e38f, 0, 0), 3e38f, false, "overflowing float subtraction");
        RangeCase(Vector3.zero, new Vector3(1e20f, 0, 0), 1e20f, true, "large exact boundary");
        RangeCase(Vector3.zero, new Vector3(1e-30f, 0, 0), 1e-30f, true, "tiny exact boundary");
        RangeCase(Vector3.zero, new Vector3(20, 0, 0), 0, true, "zero retains unbounded range policy");
    }

    private static void RangeCase(Vector3 start, Vector3 target, float range, bool expected, string label)
    {
        var engine = new AbilitySystem();
        engine.Initialize(new World());
        var unit = new Unit { EntityId = 1, Position = start };
        var definition = new AbilityDefinition
        {
            Name = "range",
            Behavior = AbilityBehavior.PointTarget,
            CastRange = LevelValue.Constant(range)
        };
        engine.RegisterAbilityDefinition(definition);
        Ability ability = engine.AddAbility(unit, "range");
        var order = new CastOrder { Caster = unit, AbilityName = "range", HasTargetPosition = true, TargetPosition = target };
        bool valid = Targeting.BuildCastContext(engine, ability, order, out _, out CastResult result);
        Require(valid == expected, label + ": invalid range acceptance = " + valid);
        if (!expected)
        {
            Require(result.FailureReason == CastFailureReason.OutOfRange, label + ": rejection must be OutOfRange.");
        }
        engine.ClearRuntime();
    }

    private static void ProjectileSweepRejectsOffAxisCandidate()
    {
        var caster = new Unit { EntityId = 1, TeamId = 1 };
        var outside = new Unit { EntityId = 2, TeamId = 2, Position = new Vector3(1, 0, 100000) };
        var inside = new Unit { EntityId = 3, TeamId = 2, Position = new Vector3(.05f, 0, 99990) };
        var world = new World();
        world.Units.Add(outside);
        world.Units.Add(inside);
        var engine = new AbilitySystem();
        engine.Initialize(world);
        var hits = new List<int>();
        engine.EventRaised += value =>
        {
            if (value.EventType == RuntimeEventType.ProjectileHit)
            {
                hits.Add(value.Target.EntityId);
            }
        };
        var projectile = engine.CreateLinearProjectile(
            null,
            caster,
            Vector3.zero,
            Vector3.forward,
            new ProjectileDefinition { Speed = 100010, Distance = 100010, Radius = .1f, DeleteOnHit = false, TargetTeam = TargetTeam.Enemy });
        Require(projectile != null, "The sweep scenario must create a projectile.");
        engine.Update(1);
        Require(hits.Count == 1 && hits[0] == inside.EntityId, "A far off-axis candidate cannot be hit through discriminant cancellation.");
        engine.ClearRuntime();
    }

    private static void TrackingMustNotArriveBeforeItsStep()
    {
        var caster = new Unit { EntityId = 1, TeamId = 1 };
        var target = new Unit { EntityId = 2, TeamId = 2, Position = new Vector3(0, 0, 1e21f) };
        var engine = new AbilitySystem();
        engine.Initialize(new World());
        int hits = 0;
        engine.EventRaised += value =>
        {
            if (value.EventType == RuntimeEventType.ProjectileHit)
            {
                hits++;
            }
        };
        var projectile = engine.CreateTrackingProjectile(
            null,
            caster,
            target,
            new ProjectileDefinition { Speed = 1e20f, Distance = 2e21f, Radius = 0 });
        engine.Update(1);
        Require(hits == 0 && !projectile.Destroyed, "Tracking must not teleport to a distant target when squared floats overflow.");
        Require(projectile.Position.z > 0 && projectile.Position.z < target.Position.z, "Tracking must make finite progress toward a distant target.");
        engine.ClearRuntime();
    }

    private static void MeasureAbilityUpdates(int count)
    {
        var engine = new AbilitySystem();
        engine.Initialize(new World());
        engine.RegisterAbilityDefinition(new AbilityDefinition { Name = "idle" });
        for (int i = 0; i < count; i++)
        {
            engine.AddAbility(new Unit { EntityId = i + 1 }, "idle");
        }
        Measure("Ability.Update", count, 40, () => engine.Update(1f / 60));
        engine.ClearRuntime();
    }

    private static void SnapshotPoolAccountsForCapacityAndNestedRentals()
    {
        var first = RuntimeSnapshotPool<PoolValue>.Rent();
        var second = RuntimeSnapshotPool<PoolValue>.Rent();
        var third = RuntimeSnapshotPool<PoolValue>.Rent();
        Require(!ReferenceEquals(first, second) && !ReferenceEquals(second, third), "Nested rentals must own different lists.");
        first.Capacity = second.Capacity = third.Capacity = 16384;
        first.Add(new PoolValue());
        RuntimeSnapshotPool<PoolValue>.Release(first);
        bool duplicateRejected = false;
        try
        {
            RuntimeSnapshotPool<PoolValue>.Release(first);
        }
        catch (InvalidOperationException)
        {
            duplicateRejected = true;
        }
        Require(duplicateRejected, "A double return cannot publish the same list to multiple renters.");
        RuntimeSnapshotPool<PoolValue>.Release(second);
        RuntimeSnapshotPool<PoolValue>.Release(third);
        var rentedFirst = RuntimeSnapshotPool<PoolValue>.Rent();
        var rentedSecond = RuntimeSnapshotPool<PoolValue>.Rent();
        var rentedThird = RuntimeSnapshotPool<PoolValue>.Rent();
        Require(ReferenceEquals(rentedFirst, second) && ReferenceEquals(rentedSecond, first), "The pool must retain two empty 16384-capacity lists within its original total budget.");
        Require(rentedFirst.Count == 0 && rentedSecond.Count == 0, "Returning a snapshot must release payload references.");
        Require(!ReferenceEquals(rentedThird, third), "A third large list must be discarded even when Count is zero; capacity consumes the retention budget.");
        rentedThird.Capacity = 32768;
        RuntimeSnapshotPool<PoolValue>.Release(rentedThird);
        RuntimeSnapshotPool<PoolValue>.Release(rentedFirst);
        RuntimeSnapshotPool<PoolValue>.Release(rentedSecond);
        var wholeBudget = RuntimeSnapshotPool<PoolValue>.Rent();
        Require(ReferenceEquals(wholeBudget, rentedThird), "Renting all retained snapshots must reset the retained capacity accounting before later returns.");
        wholeBudget.Capacity = 32769;
        RuntimeSnapshotPool<PoolValue>.Release(wholeBudget);
        Require(!ReferenceEquals(RuntimeSnapshotPool<PoolValue>.Rent(), wholeBudget), "A list larger than the complete pool budget must not be retained.");
    }

    private static void LargeProjectileDirectionsStayNormalized()
    {
        var engine = new AbilitySystem();
        engine.Initialize(new World());
        Projectile projectile = engine.CreateLinearProjectile(
            null,
            null,
            Vector3.zero,
            new Vector3(0, 0, float.MaxValue),
            new ProjectileDefinition { Speed = 1, Distance = 10 });
        engine.Update(1);
        Require(projectile.Direction.z == 1 && projectile.Position.z == 1, "A large finite direction must not normalize to zero and stall the projectile.");
        engine.ClearRuntime();
    }

    private static void AuraLeaseEndsOutsideALargeFiniteRadius()
    {
        var source = new Unit { EntityId = 1, TeamId = 1 };
        var child = new Unit { EntityId = 2, TeamId = 1, Position = new Vector3(0, 0, 1e19f) };
        var world = new World();
        world.Units.Add(child);
        var engine = new AbilitySystem();
        engine.Initialize(world);
        engine.RegisterModifierDefinition(new ModifierDefinition { Name = "child" });
        engine.RegisterModifierDefinition(new ModifierDefinition
        {
            Name = "source",
            AuraModifierName = "child",
            AuraRadius = 1e20f,
            AuraDuration = .5f,
            AuraThinkInterval = 1000
        });
        engine.AddModifier(source, source, "source");
        Require(engine.Modifiers.Count == 2, "The in-range unit must first receive the source aura.");
        Modifier auraChild = engine.Modifiers[1];
        child.Position = new Vector3(0, 0, 1e21f);
        engine.Update(.75f);
        Require(auraChild.IsDestroyed, "A source cannot retain an aura lease outside a large finite radius through float overflow.");
        engine.ClearRuntime();
    }

    private static void WarmUpdatesDoNotAllocate(int count)
    {
        var engine = new AbilitySystem();
        engine.Initialize(new World());
        engine.RegisterAbilityDefinition(new AbilityDefinition { Name = "idle" });
        for (int i = 0; i < count; i++)
        {
            engine.AddAbility(new Unit { EntityId = i + 1 }, "idle");
        }
        for (int i = 0; i < 30; i++)
        {
            engine.Update(1f / 60);
        }
        long started = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++)
        {
            engine.Update(1f / 60);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - started;
        Require(bytes == 0, "Warmed idle updates at population " + count + " allocated " + bytes + " bytes.");
        engine.ClearRuntime();
    }

    private static void MeasureSpatialQueries()
    {
        var entries = new List<KeyValuePair<Bounds3, int>>();
        for (int i = 0; i < 10000; i++)
        {
            int x = i % 100, y = i / 100;
            entries.Add(new KeyValuePair<Bounds3, int>(new Bounds3(x, y, 0, x + .5, y + .5, .5), i));
        }
        var queries = new List<KeyValuePair<string, IBoundsQuery3<int>>>
        {
            new KeyValuePair<string, IBoundsQuery3<int>>("StaticBvh.Query", new StaticBvh<int>(entries)),
            new KeyValuePair<string, IBoundsQuery3<int>>("RTreeIndex.Query", RTreeIndex<int>.BulkLoad(entries, out _))
        };
        var results = new List<int>(10000);
        var context = new QueryContext(true);
        var bounds = new Bounds3(40, 40, -.1, 60, 60, 1);
        foreach (var query in queries)
        {
            Measure(query.Key, entries.Count, 1000, () => sink += query.Value.Query(bounds, results, context));
            Require(results.Count == 441, "The measured spatial query must return all expected grid intersections.");
            Console.WriteLine("COUNTERS " + query.Key + " nodes=" + context.NodesVisited + " entries=" + context.EntriesTested + " results=" + context.ResultCount);
        }
        GC.KeepAlive(sink);
    }

    private static void Measure(string name, int population, int iterations, Action action)
    {
        for (int i = 0; i < 100; i++)
        {
            action();
        }
        var samples = new double[7];
        long bytes = 0;
        for (int sample = 0; sample < samples.Length; sample++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                action();
            }
            samples[sample] = (Stopwatch.GetTimestamp() - started) * 1000000.0 / Stopwatch.Frequency / iterations;
            bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        }
        Array.Sort(samples);
        Console.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "PERF {0} population={1} iterations={2} median_us={3:F3} max_batch_mean_us={4:F3} bytes_per_operation={5:F1}",
            name,
            population,
            iterations * samples.Length,
            samples[samples.Length / 2],
            samples[samples.Length - 1],
            bytes / (double)(iterations * samples.Length)));
    }

    private sealed class Unit : IUnit
    {
        public int EntityId { get; set; }
        public int TeamId { get; set; }
        public UnitType UnitType => UnitType.Basic;
        public bool IsAlive => true;
        public bool IsMagicImmune => false;
        public bool IsInvulnerable => false;
        public Vector3 Position { get; set; }
        public bool IsVisibleToTeam(int teamId) => true;
        public void ApplyDamage(DamageResult result)
        {
        }
        public void Heal(HealInfo info)
        {
        }
    }

    private sealed class PoolValue
    {
    }

    private sealed class World : Game.Ability.IWorld
    {
        public float Time => 0;
        public readonly List<IUnit> Units = new List<IUnit>();
        public void FindUnits(Vector3 center, float radius, TargetQuery query, IList<IUnit> results)
        {
            foreach (IUnit unit in Units)
            {
                results.Add(unit);
            }
        }
        public bool HasLineOfSight(IUnit viewer, Vector3 position) => true;
    }
}

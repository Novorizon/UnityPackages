using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using ECS;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class AlgorithmAuditTests
{
    [Test]
    public void AbilityWarmUpdateBeyondTheOldSnapshotThresholdAllocatesNoManagedMemory()
    {
        var engine = new Game.Ability.AbilitySystem();
        engine.Initialize(null);
        engine.RegisterAbilityDefinition(new Game.Ability.AbilityDefinition { Name = "idle" });
        const int population = 4097;
        for (int i = 0; i < population; i++)
        {
            engine.AddAbility(new AbilityUnit(i + 1), "idle");
        }
        try
        {
            for (int i = 0; i < 30; i++)
            {
                engine.Update(1f / 60);
            }
            const int iterations = 30;
            _ = Stopwatch.GetTimestamp();
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                engine.Update(1f / 60);
            }
            double microseconds = (Stopwatch.GetTimestamp() - started) * 1000000.0 / Stopwatch.Frequency / iterations;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            Assert.That(allocated, Is.Zero, "Stable updates beyond 4096 entries must reuse bounded snapshots.");
            TestContext.Out.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "PERF Ability.Update population={0} iterations={1} mean_us={2:F3} bytes_per_operation={3:F1}",
                population,
                iterations,
                microseconds,
                allocated / (double)iterations));
        }
        finally
        {
            engine.ClearRuntime();
        }
    }

    [Test]
    public void EcsStructuralRemovalWaitsForPostUpdateBeforeRecycling()
    {
        RequireEmptyEcs();
        try
        {
            World world = Ecs.Initialize();
            var entity = EntityManager.Create();
            var counter = EntityManager.Instance.AddComponentData<Counter>(entity);
            counter.Value = 77;
            var system = world.RegisterSystem<RemovingSystem>();
            world.OnUpdate(1f / 60);
            Assert.That(system.SawLiveComponentAfterRemovalRequest, Is.True);
            Assert.That(system.SawLiveComponentDuringPostUpdate, Is.True);
            Assert.That(counter.RecycleCount, Is.EqualTo(1));
            Assert.That(EntityManager.Instance.GetComponentData<Counter>(entity), Is.Null);
            world.OnUpdate(1f / 60);
            Assert.That(system.Calls, Is.EqualTo(1), "The dense component view must detach before reusing pooled data.");
        }
        finally
        {
            DisposeEcs();
        }
    }

    [UnityTest]
    public IEnumerator EcsOwnedAndBorrowedNativeObjectsReleaseIndependently()
    {
        RequireEmptyEcs();
        var borrowed = new GameObject("Borrowed ECS audit object");
        var owned = new GameObject("Owned ECS audit object");
        int releases = 0;
        try
        {
            Ecs.Initialize();
            Entity borrowedEntity = EntityManager.Create(borrowed, GameObjectOwnership.Borrowed);
            Entity ownedEntity = EntityManager.Create(GameObjectLease.Owned(owned, value =>
            {
                releases++;
                Object.Destroy(value);
            }));
            EntityManager.Instance.DestroyEntity(ownedEntity);
            EntityManager.Instance.DestroyEntity(borrowedEntity);
            EntityManager.Instance.DestroyEntity(ownedEntity);
            Assert.That(releases, Is.EqualTo(1));
            yield return null;
            Assert.That(owned == null, Is.True, "The provider's real deferred native Destroy must complete.");
            Assert.That(borrowed != null, Is.True, "Destroying the entity cannot destroy its caller's borrowed GameObject.");
        }
        finally
        {
            DisposeEcs();
            Object.Destroy(borrowed);
            if (owned != null)
            {
                Object.Destroy(owned);
            }
        }
    }

    [Test]
    public void EcsWarmDenseWorldUpdateReportsTimeAndAllocatesNoManagedMemory()
    {
        RequireEmptyEcs();
        try
        {
            World world = Ecs.Initialize();
            var system = world.RegisterSystem<CountingSystem>();
            const int population = 1024;
            for (int i = 0; i < population; i++)
            {
                var entity = EntityManager.Create();
                EntityManager.Instance.AddComponentData<Counter>(entity);
            }
            for (int i = 0; i < 100; i++)
            {
                world.OnUpdate(1f / 60);
            }
            const int iterations = 100;
            _ = Stopwatch.GetTimestamp();
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; i++)
            {
                world.OnUpdate(1f / 60);
            }
            double microseconds = (Stopwatch.GetTimestamp() - started) * 1000000.0 / Stopwatch.Frequency / iterations;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            Assert.That(system.Calls, Is.EqualTo(population * 200));
            Assert.That(allocated, Is.Zero, "The warmed stable ECS world must not allocate managed memory on each tick.");
            TestContext.Out.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "PERF ECS.World.Update population={0} iterations={1} mean_us={2:F3} bytes_per_operation={3:F1}",
                population,
                iterations,
                microseconds,
                allocated / (double)iterations));
        }
        finally
        {
            DisposeEcs();
        }
    }

    private static void RequireEmptyEcs()
    {
        Assert.That(WorldManager.TryGetInstance(out _), Is.False);
        Assert.That(EntityManager.TryGetInstance(out _), Is.False);
        Assert.That(ReferencePool.TryGetInstance(out _), Is.False);
    }

    private static void DisposeEcs()
    {
        if (WorldManager.TryGetInstance(out var worlds))
        {
            Object.DestroyImmediate(worlds.gameObject);
        }
        if (EntityManager.TryGetInstance(out var entities))
        {
            Object.DestroyImmediate(entities.gameObject);
        }
        if (ReferencePool.TryGetInstance(out var pool))
        {
            Object.DestroyImmediate(pool.gameObject);
        }
    }

    public sealed class Counter : IComponent, IRecycleable
    {
        public int Value;
        public int RecycleCount;

        public void OnRecycle()
        {
            RecycleCount++;
        }
    }

    public sealed class RemovingSystem : ComponentSystem<Counter>
    {
        private Counter pending;
        public int Calls;
        public bool SawLiveComponentAfterRemovalRequest;
        public bool SawLiveComponentDuringPostUpdate;

        protected override void OnUpdate(int index, Entity entity, Counter counter)
        {
            Calls++;
            pending = counter;
            EntityManager.Instance.RemoveComponentData<Counter>(entity);
            SawLiveComponentAfterRemovalRequest = counter.RecycleCount == 0 && counter.Value == 77;
        }

        public override void PostUpdate()
        {
            if (pending != null)
            {
                SawLiveComponentDuringPostUpdate = pending.RecycleCount == 0 && pending.Value == 77;
            }
        }
    }

    public sealed class CountingSystem : ComponentSystem<Counter>
    {
        public int Calls;

        protected override void OnUpdate(int index, Entity entity, Counter counter)
        {
            Calls++;
            counter.Value++;
        }
    }

    private sealed class AbilityUnit : Game.Ability.IUnit
    {
        public AbilityUnit(int id)
        {
            EntityId = id;
        }

        public int EntityId { get; }
        public int TeamId => 1;
        public Game.Ability.UnitType UnitType => Game.Ability.UnitType.Basic;
        public bool IsAlive => true;
        public bool IsMagicImmune => false;
        public bool IsInvulnerable => false;
        public Vector3 Position => Vector3.zero;
        public bool IsVisibleToTeam(int teamId) => true;
        public void ApplyDamage(Game.Ability.DamageResult result)
        {
        }
        public void Heal(Game.Ability.HealInfo info)
        {
        }
    }
}

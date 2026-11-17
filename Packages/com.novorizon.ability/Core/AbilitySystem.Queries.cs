using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    /// <summary>提供技能系统的实现。</summary>
    public sealed partial class AbilitySystem
    {
        /// <summary>查找单位核心。</summary>
        private void FindUnitsCore(Vector3 center, float radius, TargetQuery query, IList<IUnit> results)
        {
            using (BeginExecution())
            {
                if (results == null || World == null || !IsRuntimeAvailable || !IsFinite(center) || !CombatSnapshots.IsFinite(radius) || radius < 0f ||
                    query != null && query.Engine != null && !ReferenceEquals(query.Engine, this) || !TryConsumeExecutionBudget("FindUnits")) return;
                long generation = RuntimeGeneration;
                IWorld world = World;
                List<IUnit> candidates = RuntimeSnapshotPool<IUnit>.Rent();
                List<IUnit> accepted = RuntimeSnapshotPool<IUnit>.Rent();
                HashSet<int> identities = QueryIdentityPool.Rent();
                try
                {
                    for (int i = 0; i < results.Count; i++)
                        if (results[i] != null) identities.Add(results[i].EntityId);
                    world.FindUnits(center, radius, query, candidates);
                    if (generation != RuntimeGeneration || !IsRuntimeAvailable || executionAborted || query != null && IsUnitRemoving(query.Caster)) return;
                    double radiusSquared = (double)radius * radius;
                    foreach (IUnit unit in candidates)
                    {
                        if (generation != RuntimeGeneration || !IsRuntimeAvailable || executionAborted) return;
                        if (unit == null || IsUnitRemoving(unit) || identities.Contains(unit.EntityId)) continue;
                        Vector3 position = unit.Position;
                        if (!IsFinite(position)) continue;
                        double x = (double)position.x - center.x, y = (double)position.y - center.y, z = (double)position.z - center.z;
                        if (x * x + y * y + z * z > radiusSquared || query != null && !query.IsValid(unit)) continue;
                        if (generation != RuntimeGeneration || !IsRuntimeAvailable || executionAborted || query != null && IsUnitRemoving(query.Caster)) return;
                        if (!IsUnitRemoving(unit) && identities.Add(unit.EntityId)) accepted.Add(unit);
                    }
                    if (generation != RuntimeGeneration || !IsRuntimeAvailable || executionAborted) return;
                    // Commit only after all external query/filter callbacks complete. Existing
                    // caller entries are preserved; duplicate collider wrappers collapse by ID.
                    foreach (IUnit unit in accepted)
                        if (!IsUnitRemoving(unit)) results.Add(unit);
                }
                finally
                {
                    QueryIdentityPool.Release(identities);
                    RuntimeSnapshotPool<IUnit>.Release(accepted);
                    RuntimeSnapshotPool<IUnit>.Release(candidates);
                }
            }
        }

        /// <summary>表示技能系统中的Query 身份对象池。</summary>
        private static class QueryIdentityPool
        {
            [ThreadStatic] private static Stack<HashSet<int>> available;
            /// <summary>租用 Query 身份对象池。</summary>
            internal static HashSet<int> Rent() => available != null && available.Count > 0 ? available.Pop() : new HashSet<int>();
            /// <summary>结束Query 身份对象池对相关资源的持有，并执行该类型的清理流程。</summary>
            internal static void Release(HashSet<int> values)
            {
                bool retain = values.Count <= 4096;
                values.Clear();
                if (!retain) return;
                if (available == null) available = new Stack<HashSet<int>>();
                if (available.Count < 8) available.Push(values);
            }
        }
    }
}

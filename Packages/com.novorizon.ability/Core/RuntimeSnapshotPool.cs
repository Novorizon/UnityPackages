using System;
using System.Collections.Generic;

namespace Game.Ability
{
    /// <summary>Each nested operation owns a separate snapshot until its finally block returns it.</summary>
    /// <remarks>提供运行时快照对象池的实现。</remarks>
    internal static class RuntimeSnapshotPool<T>
    {
        private const int MaximumRetainedLists = 8;
        // Same aggregate element ceiling as eight 4096-element lists, but one
        // larger active workload can reuse that budget instead of allocating each tick.
        private const int MaximumRetainedElements = MaximumRetainedLists * 4096;
        [ThreadStatic] private static Stack<List<T>> available;
        [ThreadStatic] private static int retainedCapacity;

        /// <summary>租用运行时快照对象池。</summary>
        internal static List<T> Rent(List<T> source = null)
        {
            List<T> snapshot;
            if (available != null && available.Count > 0)
            {
                snapshot = available.Pop();
                retainedCapacity -= snapshot.Capacity;
            }
            else
            {
                snapshot = new List<T>();
            }
            if (source != null)
            {
                snapshot.AddRange(source);
            }
            return snapshot;
        }

        /// <summary>结束运行时快照对象池对相关资源的持有，并执行该类型的清理流程。</summary>
        internal static void Release(List<T> snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }
            if (available != null && available.Contains(snapshot))
            {
                throw new InvalidOperationException("A runtime snapshot cannot be returned to its pool twice.");
            }
            snapshot.Clear();
            if (snapshot.Capacity > MaximumRetainedElements - retainedCapacity)
            {
                return;
            }
            if (available == null)
            {
                available = new Stack<List<T>>();
                retainedCapacity = 0;
            }
            if (available.Count < MaximumRetainedLists)
            {
                available.Push(snapshot);
                retainedCapacity += snapshot.Capacity;
            }
        }
    }
}

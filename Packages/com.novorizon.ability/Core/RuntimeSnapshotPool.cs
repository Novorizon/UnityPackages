using System;
using System.Collections.Generic;

namespace Game.Ability
{
    /// <summary>Each nested operation owns a separate snapshot until its finally block returns it.</summary>
    internal static class RuntimeSnapshotPool<T>
    {
        private const int MaximumRetainedLists = 8;
        private const int MaximumRetainedCapacity = 4096;
        [ThreadStatic] private static Stack<List<T>> available;

        internal static List<T> Rent(List<T> source = null)
        {
            List<T> snapshot = available != null && available.Count > 0 ? available.Pop() : new List<T>();
            if (source != null) snapshot.AddRange(source);
            return snapshot;
        }

        internal static void Release(List<T> snapshot)
        {
            snapshot.Clear();
            if (snapshot.Capacity > MaximumRetainedCapacity) return;
            if (available == null) available = new Stack<List<T>>();
            if (available.Count < MaximumRetainedLists) available.Push(snapshot);
        }
    }
}

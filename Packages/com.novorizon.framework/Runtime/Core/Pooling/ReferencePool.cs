using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Game.Framework.Pooling
{
    public interface IReferencePoolable { void Reset(); }

    public static class ReferencePool
    {
        private static readonly object gate = new object();
        private static readonly List<Action> sharedResets = new List<Action>();
        internal static ReferencePool<T> Register<T>(ReferencePool<T> pool) where T : class, IReferencePoolable, new()
        { lock (gate) sharedResets.Add(pool.Clear); return pool; }
        public static void ClearAll()
        {
            Action[] snapshot; lock (gate) snapshot = sharedResets.ToArray();
            foreach (var reset in snapshot) reset();
        }
    }

    // Identity-based ownership checks, bounded retention, and no strong roots for forgotten rentals.
    public sealed class ReferencePool<T> where T : class, IReferencePoolable, new()
    {
        private enum State { Rented, Returning, Cached, Retired }
        private sealed class Entry { internal T Value; internal State State; internal long Generation; }
        private static readonly Lazy<ReferencePool<T>> shared = new Lazy<ReferencePool<T>>(() => ReferencePool.Register(new ReferencePool<T>()));
        private readonly object gate = new object();
        private readonly Stack<Entry> cached = new Stack<Entry>();
        private readonly ConditionalWeakTable<T, Entry> ownership = new ConditionalWeakTable<T, Entry>();
        private long generation;
        public static ReferencePool<T> Shared => shared.Value;
        public int MaximumRetained { get; }
        public int CountInactive { get { lock (gate) return cached.Count; } }
        public ReferencePool(int maximumRetained = 256)
        {
            if (maximumRetained < 0) throw new ArgumentOutOfRangeException(nameof(maximumRetained));
            MaximumRetained = maximumRetained;
        }
        public T Rent()
        {
            lock (gate)
            {
                if (cached.Count != 0)
                {
                    var entry = cached.Pop(); entry.State = State.Rented; entry.Generation = generation;
                    return entry.Value;
                }
            }
            // User constructors and reset callbacks never run under the pool lock.
            var created = new Entry { Value = new T(), State = State.Rented };
            lock (gate) { created.Generation = generation; ownership.Add(created.Value, created); }
            return created.Value;
        }
        public void Return(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            Entry entry;
            lock (gate)
            {
                if (!ownership.TryGetValue(value, out entry) || entry.State != State.Rented)
                    throw new InvalidOperationException("Return each rental once, to the pool that created it.");
                entry.State = State.Returning;
            }
            try { value.Reset(); }
            catch { lock (gate) { entry.State = State.Retired; ownership.Remove(value); } throw; }
            lock (gate)
            {
                if (entry.Generation == generation && cached.Count < MaximumRetained)
                { entry.State = State.Cached; cached.Push(entry); }
                else { entry.State = State.Retired; ownership.Remove(value); }
            }
        }
        public void Prewarm(int count)
        {
            if (count < 0 || count > MaximumRetained) throw new ArgumentOutOfRangeException(nameof(count));
            while (true)
            {
                long started;
                lock (gate) { if (cached.Count >= count) return; started = generation; }
                var entry = new Entry { Value = new T(), State = State.Cached };
                lock (gate)
                {
                    if (started != generation || cached.Count >= count) return;
                    entry.Generation = generation; ownership.Add(entry.Value, entry); cached.Push(entry);
                }
            }
        }
        public void Trim(int retainedCount = 0)
        {
            if (retainedCount < 0) throw new ArgumentOutOfRangeException(nameof(retainedCount));
            lock (gate)
            {
                while (cached.Count > retainedCount)
                { var entry = cached.Pop(); entry.State = State.Retired; ownership.Remove(entry.Value); }
                cached.TrimExcess();
            }
        }
        public void Clear()
        {
            lock (gate)
            {
                generation++; // Outstanding rentals from the old generation are reset and discarded on return.
                while (cached.Count != 0)
                { var entry = cached.Pop(); entry.State = State.Retired; ownership.Remove(entry.Value); }
                cached.TrimExcess();
            }
        }
    }
}

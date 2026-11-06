using System;
using System.Collections.Generic;

namespace Game.Framework.Timing
{
    public enum TimerRepeatMode
    {
        FixedRate,
        FixedDelay
    }

    public readonly struct TimerHandle : IDisposable
    {
        private readonly TimerManager owner;
        private readonly long id;
        internal TimerHandle(TimerManager owner, long id)
        {
            this.owner = owner;
            this.id = id;
        }

        public bool IsActive => owner != null && owner.Contains(id);

        public bool Cancel() => owner != null && owner.Cancel(id);
        public void Dispose()
        {
            Cancel();
        }
    }

    // Two indexed min-heaps: scaled and unscaled clocks. All operations run on the host main thread.
    public sealed class TimerManager : Singleton<TimerManager>, IDisposable
    {
        public void Dispose() => ResetForPlay();
        private sealed class Entry
        {
            internal long Id;
            internal Action Callback;
            internal double Due, Interval, Origin, Ordinal;
            internal TimerRepeatMode Mode;
            internal bool Unscaled;
            internal int Index = -1;
        }

        private sealed class Heap
        {
            private readonly List<Entry> values = new List<Entry>();
            internal Entry First => values.Count == 0 ? null : values[0];

            private static bool Before(Entry a, Entry b) => a.Due < b.Due || (a.Due == b.Due && a.Id < b.Id);
            private void Set(int index, Entry entry)
            {
                values[index] = entry;
                entry.Index = index;
            }

            internal void Add(Entry entry)
            {
                int index = values.Count;
                values.Add(entry);
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Before(entry, values[parent]))
                    {
                        break;
                    }

                    Set(index, values[parent]);
                    index = parent;
                }

                Set(index, entry);
            }

            internal void Remove(Entry entry)
            {
                int index = entry.Index, lastIndex = values.Count - 1;
                var last = values[lastIndex];
                values.RemoveAt(lastIndex);
                entry.Index = -1;
                if (index == lastIndex)
                {
                    return;
                }

                Set(index, last);
                if (index > 0 && Before(last, values[(index - 1) / 2]))
                {
                    while (index > 0)
                    {
                        int parent = (index - 1) / 2;
                        if (!Before(last, values[parent]))
                        {
                            break;
                        }

                        Set(index, values[parent]);
                        index = parent;
                    }
                }
                else
                {
                    while (index * 2 + 1 < values.Count)
                    {
                        int child = index * 2 + 1;
                        if (child + 1 < values.Count && Before(values[child + 1], values[child]))
                        {
                            child++;
                        }

                        if (!Before(values[child], last))
                        {
                            break;
                        }

                        Set(index, values[child]);
                        index = child;
                    }
                }

                Set(index, last);
            }

            internal void Clear(bool trim)
            {
                values.Clear();
                if (trim)
                {
                    values.TrimExcess();
                }
            }
        }

        public const int MaximumTimers = 65536;
        public const int MaximumCachedEntries = 1024;
        private readonly Heap scaled = new Heap(), unscaled = new Heap();
        private readonly Dictionary<long, Entry> active = new Dictionary<long, Entry>();
        private readonly Stack<Entry> cached = new Stack<Entry>();
        private readonly List<Entry> deferred = new List<Entry>();
        private long nextId;
        private double scaledTime, unscaledTime;
        private double lastScaledSample, lastUnscaledSample;
        private Func<double> scaledClock, unscaledClock;
        private bool updating, preferUnscaled;
        private int callbackBudget = 256;
        public int PendingCount
        {
            get
            {
                MainThread();
                return active.Count;
            }
        }

        public int CallbackBudget
        {
            get => callbackBudget;
            set
            {
                MainThread();
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                callbackBudget = value;
            }
        }

        private static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use TimerManager on the initialized main thread; post worker requests through UnityThread.");
            }
        }

        private static void Duration(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }

        // Advanced hosts bind clocks once, before scheduling. Unity installs its clocks automatically.
        public void UseClocks(Func<double> scaledTimeProvider, Func<double> unscaledTimeProvider)
        {
            MainThread();
            if (updating || active.Count != 0)
            {
                throw new InvalidOperationException("Bind clocks before scheduling timers.");
            }

            if (scaledTimeProvider == null)
            {
                throw new ArgumentNullException(nameof(scaledTimeProvider));
            }

            if (unscaledTimeProvider == null)
            {
                throw new ArgumentNullException(nameof(unscaledTimeProvider));
            }

            double game = scaledTimeProvider(), real = unscaledTimeProvider();
            Duration(game, nameof(scaledTimeProvider));
            Duration(real, nameof(unscaledTimeProvider));
            scaledClock = scaledTimeProvider;
            unscaledClock = unscaledTimeProvider;
            scaledTime = lastScaledSample = game;
            unscaledTime = lastUnscaledSample = real;
        }

        public TimerHandle Schedule(double seconds, Action callback, bool useUnscaledTime = false) => Add(seconds, 0, callback, useUnscaledTime, default);
        public TimerHandle Repeat(double intervalSeconds, Action callback, bool useUnscaledTime = false, TimerRepeatMode mode = TimerRepeatMode.FixedRate)
        {
            if (intervalSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            }

            if (mode != TimerRepeatMode.FixedRate && mode != TimerRepeatMode.FixedDelay)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            return Add(intervalSeconds, intervalSeconds, callback, useUnscaledTime, mode);
        }

        private TimerHandle Add(double delay, double interval, Action callback, bool realTime, TimerRepeatMode mode)
        {
            MainThread();
            Duration(delay, nameof(delay));
            if (callback == null)
            {
                throw new ArgumentNullException(nameof(callback));
            }

            if (active.Count >= MaximumTimers || nextId == long.MaxValue)
            {
                throw new InvalidOperationException("Timer capacity exhausted.");
            }

            double now = ReadClock(realTime), due = now + delay;
            if (delay > 0 && due <= now)
            {
                due = NextRepresentable(now);
            }

            Duration(due, nameof(delay));
            var entry = cached.Count == 0 ? new Entry() : cached.Pop();
            entry.Id = ++nextId;
            entry.Callback = callback;
            entry.Due = entry.Origin = due;
            entry.Ordinal = 0;
            entry.Interval = interval;
            entry.Unscaled = realTime;
            entry.Mode = mode;
            active.Add(entry.Id, entry);
            (realTime ? unscaled : scaled).Add(entry);
            return new TimerHandle(this, entry.Id);
        }

        internal bool Contains(long id)
        {
            if (!UnityThread.IsInitialized)
            {
                return false;
            }

            MainThread();
            return active.ContainsKey(id);
        }

        internal bool Cancel(long id)
        {
            if (!UnityThread.IsInitialized)
            {
                return false;
            }

            MainThread();
            if (!active.TryGetValue(id, out var entry))
            {
                return false;
            }

            active.Remove(id);
            entry.Callback = null;
            if (entry.Index >= 0)
            {
                (entry.Unscaled ? unscaled : scaled).Remove(entry);
                Recycle(entry);
            }

            return true;
        }

        private void Recycle(Entry entry)
        {
            entry.Callback = null;
            entry.Index = -1;
            if (cached.Count < MaximumCachedEntries)
            {
                cached.Push(entry);
            }
        }

        public int Update(double scaledDeltaSeconds, double unscaledDeltaSeconds)
        {
            MainThread();
            Duration(scaledDeltaSeconds, nameof(scaledDeltaSeconds));
            Duration(unscaledDeltaSeconds, nameof(unscaledDeltaSeconds));
            if (scaledClock != null)
            {
                throw new InvalidOperationException("Bound clocks use Update() without delta arguments.");
            }

            if (updating)
            {
                throw new InvalidOperationException("Timer updates cannot be nested.");
            }

            double nextScaled = scaledTime + scaledDeltaSeconds, nextUnscaled = unscaledTime + unscaledDeltaSeconds;
            Duration(nextScaled, nameof(scaledDeltaSeconds));
            Duration(nextUnscaled, nameof(unscaledDeltaSeconds));
            scaledTime = nextScaled;
            unscaledTime = nextUnscaled;
            return Dispatch();
        }

        public int Update()
        {
            MainThread();
            if (scaledClock == null)
            {
                throw new InvalidOperationException("Bind clocks with UseClocks, or use Update(delta, delta) for a manual host.");
            }

            if (updating)
            {
                throw new InvalidOperationException("Timer updates cannot be nested.");
            }

            double game = scaledClock(), real = unscaledClock();
            ValidateSample(game, lastScaledSample);
            ValidateSample(real, lastUnscaledSample);
            // Validate both samples before advancing either clock.
            scaledTime = lastScaledSample = game;
            unscaledTime = lastUnscaledSample = real;
            return Dispatch();
        }

        private static void ValidateSample(double now, double previous)
        {
            Duration(now, nameof(now));
            if (now < previous)
            {
                throw new InvalidOperationException("Timer clocks must be monotonic. Use elapsed time, not a wall clock.");
            }
        }

        private double ReadClock(bool realTime)
        {
            if (scaledClock == null)
            {
                return realTime ? unscaledTime : scaledTime;
            }

            double now = realTime ? unscaledClock() : scaledClock();
            ValidateSample(now, realTime ? lastUnscaledSample : lastScaledSample);
            if (realTime)
            {
                lastUnscaledSample = now;
            }
            else
            {
                lastScaledSample = now;
            }

            return now;
        }

        private static double NextRepresentable(double now) => BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(now) + 1);
        private void SetNextDeadline(Entry entry)
        {
            double now = ReadClock(entry.Unscaled);
            if (entry.Mode == TimerRepeatMode.FixedDelay)
            {
                entry.Due = now + entry.Interval;
            }
            else
            {
                // Anchor every tick to the original phase, rather than accumulating execution lateness.
                entry.Ordinal++;
                entry.Due = entry.Ordinal >= 9007199254740992d ? NextRepresentable(now) : entry.Origin + entry.Ordinal * entry.Interval;
                double resolution = NextRepresentable(now) - now;
                double tolerance = entry.Interval > resolution * 8 ? resolution * 4 : 0;
                // On-time ticks take the fast path; division is only needed to skip missed phases.
                if (entry.Due - now <= tolerance)
                {
                    entry.Ordinal = Math.Max(entry.Ordinal, Math.Floor((now - entry.Origin) / entry.Interval) + 1);
                    entry.Due = entry.Ordinal >= 9007199254740992d ? NextRepresentable(now) : entry.Origin + entry.Ordinal * entry.Interval;
                }

                // Decimal periods (e.g. 0.1 at 0.3) can land a few ULPs after now.
                // Treat that phase as elapsed, without rounding away genuinely tiny intervals.
                if (tolerance > 0 && entry.Due - now <= tolerance)
                {
                    entry.Ordinal++;
                    entry.Due = entry.Origin + entry.Ordinal * entry.Interval;
                }
            }

            // A huge clock jump or a sub-resolution interval must never reschedule at/before now.
            if (!(entry.Due > now))
            {
                entry.Due = NextRepresentable(now);
            }

            if (double.IsInfinity(entry.Due))
            {
                throw new InvalidOperationException("Timer clock exhausted its representable range.");
            }
        }

        private int Dispatch()
        {
            long cutoff = nextId;
            int invoked = 0, budget = callbackBudget;
            updating = true;
            try
            {
                while (invoked < budget)
                {
                    var first = preferUnscaled ? unscaled : scaled;
                    var second = preferUnscaled ? scaled : unscaled;
                    var heap = Ready(first, cutoff) ? first : Ready(second, cutoff) ? second : null;
                    if (heap == null)
                    {
                        break;
                    }

                    preferUnscaled = !preferUnscaled;
                    var entry = heap.First;
                    heap.Remove(entry);
                    var callback = entry.Callback;
                    bool repeat = entry.Interval > 0;
                    if (repeat)
                    {
                        deferred.Add(entry);
                    }
                    else
                    {
                        active.Remove(entry.Id);
                    }

                    try
                    {
                        callback();
                    }
                    catch (Exception error)
                    {
                        Logger.Exception(error);
                    }
                    finally
                    {
                        if (!repeat)
                        {
                            Recycle(entry);
                        }
                        else if (active.ContainsKey(entry.Id))
                        {
                            try
                            {
                                SetNextDeadline(entry);
                            }
                            catch
                            {
                                active.Remove(entry.Id);
                                entry.Callback = null;
                                throw;
                            }
                        }
                    }

                    invoked++;
                }

                return invoked;
            }
            finally
            {
                foreach (var entry in deferred)
                {
                    if (active.TryGetValue(entry.Id, out var current) && ReferenceEquals(current, entry))
                    {
                        (entry.Unscaled ? unscaled : scaled).Add(entry);
                    }
                    else
                    {
                        Recycle(entry);
                    }
                }

                deferred.Clear();
                updating = false;
            }
        }

        private bool Ready(Heap heap, long cutoff)
        {
            var entry = heap.First;
            return entry != null && entry.Id <= cutoff && entry.Due <= (entry.Unscaled ? unscaledTime : scaledTime);
        }

        public void Clear(bool trimExcess = false)
        {
            MainThread();
            ClearCore(trimExcess);
        }

        private void ClearCore(bool trim)
        {
            foreach (var entry in active.Values)
            {
                entry.Callback = null;
                if (entry.Index >= 0)
                {
                    Recycle(entry); // In-flight repeat entries are recycled by Update's finally.
                }
            }

            active.Clear();
            scaled.Clear(trim);
            unscaled.Clear(trim);
            if (trim)
            {
                cached.Clear();
                cached.TrimExcess();
                active.TrimExcess();
                if (!updating)
                {
                    deferred.TrimExcess();
                }
            }
        }

        internal void ResetForPlay()
        {
            ClearCore(true);
            scaledTime = unscaledTime = lastScaledSample = lastUnscaledSample = 0;
            scaledClock = unscaledClock = null;
            callbackBudget = 256;
            preferUnscaled = false;
        }
    }
}

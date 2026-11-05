#nullable enable
using System;

namespace Game.Framework.Samples.Demo
{
    /// <summary>A demo of elapsed-period settlement, independent of timer callback frequency.</summary>
    public sealed class DemoProductionLedger
    {
        readonly double start, period;
        readonly long reward;
        public long Units { get; private set; }
        public long Cycles { get; private set; }

        public DemoProductionLedger(double start, double period, long rewardPerPeriod)
        {
            if (!Finite(start) || start < 0) throw new ArgumentOutOfRangeException(nameof(start));
            if (!Finite(period) || period <= 0) throw new ArgumentOutOfRangeException(nameof(period));
            if (rewardPerPeriod < 0) throw new ArgumentOutOfRangeException(nameof(rewardPerPeriod));
            this.start = start; this.period = period; reward = rewardPerPeriod;
        }

        public long Settle(double now)
        {
            if (!Finite(now) || now < start) throw new ArgumentOutOfRangeException(nameof(now));
            double due = Math.Floor((now - start) / period);
            if (due >= long.MaxValue) throw new OverflowException("The demo period counter exceeded Int64.");
            // Decimal periods such as .1 can put the third deadline a few ULPs after .3.
            // Use the timer's limited boundary tolerance; do not round genuinely small intervals.
            double resolution = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(now) + 1) - now;
            double tolerance = period > resolution * 8 ? resolution * 4 : 0;
            double nextDeadline = start + (due + 1) * period;
            if (tolerance > 0 && nextDeadline > now && nextDeadline - now <= tolerance) due++;
            if (due >= long.MaxValue) throw new OverflowException("The demo period counter exceeded Int64.");
            long cycles = (long)due;
            if (cycles <= Cycles) return 0;
            long amount = checked((cycles - Cycles) * reward);
            long total = checked(Units + amount);
            Cycles = cycles; Units = total;
            return amount;
        }

        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}

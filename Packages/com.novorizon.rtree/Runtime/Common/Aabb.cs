using System;
using System.Runtime.CompilerServices;

namespace RTree
{
    // A box is six inline doubles in the tree's entry pool. Public Rectangle
    // objects are converted only at the API boundary, not while traversing.
    /// <summary>保存轴对齐包围盒所需的数据与配套操作。</summary>
    internal struct Aabb
    {
        internal double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

        /// <summary>使用调用方提供的minX、minY、minZ创建 Aabb。</summary>
        internal Aabb(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            MinX = minX; MinY = minY; MinZ = minZ;
            MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        }

        /// <summary>按提供的矩形构建返回值。</summary>
        internal static Aabb FromRectangle(Rectangle rectangle)
        {
            if (rectangle == null) throw new ArgumentNullException("rectangle");
            return new Aabb(rectangle.min[0], rectangle.min[1], rectangle.min[2],
                            rectangle.max[0], rectangle.max[1], rectangle.max[2]);
        }

        /// <summary>将当前值转换为矩形。</summary>
        internal Rectangle ToRectangle()
        {
            return new Rectangle(MinX, MinY, MaxX, MaxY, MinZ, MaxZ);
        }

        /// <summary>判断是否相交轴对齐包围盒。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Intersects(Aabb other)
        {
            return MinX <= other.MaxX && MaxX >= other.MinX &&
                   MinY <= other.MaxY && MaxY >= other.MinY &&
                   MinZ <= other.MaxZ && MaxZ >= other.MinZ;
        }

        /// <summary>检查是否包含轴对齐包围盒。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Contains(Aabb other)
        {
            return MinX <= other.MinX && MaxX >= other.MaxX &&
                   MinY <= other.MinY && MaxY >= other.MaxY &&
                   MinZ <= other.MinZ && MaxZ >= other.MaxZ;
        }

        /// <summary>返回Equals 包围盒的判断结果。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool EqualsBounds(Aabb other)
        {
            return MinX == other.MinX && MaxX == other.MaxX &&
                   MinY == other.MinY && MaxY == other.MaxY &&
                   MinZ == other.MinZ && MaxZ == other.MaxZ;
        }

        /// <summary>根据传入参数取得并集。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Aabb Union(Aabb left, Aabb right)
        {
            return new Aabb(Math.Min(left.MinX, right.MinX), Math.Min(left.MinY, right.MinY), Math.Min(left.MinZ, right.MinZ),
                            Math.Max(left.MaxX, right.MaxX), Math.Max(left.MaxY, right.MaxY), Math.Max(left.MaxZ, right.MaxZ));
        }

        /// <summary>根据传入参数取得Min。</summary>
        internal double Min(int axis)
        {
            return axis == 0 ? MinX : axis == 1 ? MinY : MinZ;
        }

        /// <summary>根据传入参数取得上限。</summary>
        internal double Max(int axis)
        {
            return axis == 0 ? MaxX : axis == 1 ? MaxY : MaxZ;
        }

        /// <summary>根据传入参数取得中心点。</summary>
        internal double Center(int axis)
        {
            double minimum = Min(axis), maximum = Max(axis);
            // The equality shortcut also preserves single-point subnormals.
            return minimum == maximum ? minimum : minimum * 0.5 + maximum * 0.5;
        }

        /// <summary>取得体积。</summary>
        internal double Volume()
        {
            return Product(MaxX - MinX, MaxY - MinY, MaxZ - MinZ);
        }

        /// <summary>根据传入参数取得增长。</summary>
        internal double Growth(Aabb other)
        {
            double x = MaxX - MinX, y = MaxY - MinY, z = MaxZ - MinZ;
            double gx = AxisGrowth(MinX, MaxX, other.MinX, other.MaxX);
            double gy = AxisGrowth(MinY, MaxY, other.MinY, other.MaxY);
            double gz = AxisGrowth(MinZ, MaxZ, other.MinZ, other.MaxZ);
            // Nonnegative expansion terms avoid infinity - infinity when both
            // original and expanded volumes exceed the double range.
            return Product(gx, y + gy, z + gz) + Product(x, gy, z + gz) + Product(x, y, gz);
        }

        /// <summary>取得表面。</summary>
        internal double Surface()
        {
            double x = MaxX - MinX, y = MaxY - MinY, z = MaxZ - MinZ;
            return Product(x, y) + Product(x, z) + Product(y, z);
        }

        /// <summary>根据传入参数取得表面增长。</summary>
        internal double SurfaceGrowth(Aabb other)
        {
            double x = MaxX - MinX, y = MaxY - MinY, z = MaxZ - MinZ;
            double gx = AxisGrowth(MinX, MaxX, other.MinX, other.MaxX);
            double gy = AxisGrowth(MinY, MaxY, other.MinY, other.MaxY);
            double gz = AxisGrowth(MinZ, MaxZ, other.MinZ, other.MaxZ);
            return Product(gx, y + gy) + Product(x, gy) +
                   Product(gx, z + gz) + Product(x, gz) +
                   Product(gy, z + gz) + Product(y, gz);
        }

        /// <summary>取得边距。</summary>
        internal double Margin()
        {
            return (MaxX - MinX) + (MaxY - MinY) + (MaxZ - MinZ);
        }

        /// <summary>根据传入参数取得边距增长。</summary>
        internal double MarginGrowth(Aabb other)
        {
            return AxisGrowth(MinX, MaxX, other.MinX, other.MaxX) +
                   AxisGrowth(MinY, MaxY, other.MinY, other.MaxY) +
                   AxisGrowth(MinZ, MaxZ, other.MinZ, other.MaxZ);
        }

        /// <summary>根据传入参数取得重叠体积。</summary>
        internal double OverlapVolume(Aabb other)
        {
            double x, y, z;
            return OverlapWidths(other, out x, out y, out z) ? Product(x, y, z) : 0;
        }

        /// <summary>根据传入参数取得重叠表面。</summary>
        internal double OverlapSurface(Aabb other)
        {
            double x, y, z;
            return OverlapWidths(other, out x, out y, out z) ? Product(x, y) + Product(x, z) + Product(y, z) : 0;
        }

        /// <summary>根据传入参数取得重叠边距。</summary>
        internal double OverlapMargin(Aabb other)
        {
            double x, y, z;
            return OverlapWidths(other, out x, out y, out z) ? x + y + z : 0;
        }

        /// <summary>返回重叠 Widths的判断结果。</summary>
        private bool OverlapWidths(Aabb other, out double x, out double y, out double z)
        {
            double minX = Math.Max(MinX, other.MinX), maxX = Math.Min(MaxX, other.MaxX);
            double minY = Math.Max(MinY, other.MinY), maxY = Math.Min(MaxY, other.MaxY);
            double minZ = Math.Max(MinZ, other.MinZ), maxZ = Math.Min(MaxZ, other.MaxZ);
            x = maxX - minX; y = maxY - minY; z = maxZ - minZ;
            return maxX >= minX && maxY >= minY && maxZ >= minZ;
        }

        /// <summary>根据传入参数取得轴向增长。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double AxisGrowth(double minimum, double maximum, double otherMinimum, double otherMaximum)
        {
            return Math.Max(0, minimum - otherMinimum) + Math.Max(0, otherMaximum - maximum);
        }

        /// <summary>根据传入参数取得商品。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Product(double x, double y)
        {
            return x == 0 || y == 0 ? 0 : x * y;
        }

        /// <summary>根据传入参数取得商品。</summary>
        private static double Product(double x, double y, double z)
        {
            double product = (x * y) * z;
            if (product > 0 && !double.IsInfinity(product)) return product;
            if (x == 0 || y == 0 || z == 0) return 0;
            if (x > y) { double swap = x; x = y; y = swap; }
            if (y > z) { double swap = y; y = z; z = swap; }
            if (x > y) { double swap = x; x = y; y = swap; }
            return (x * z) * y;
        }

        /// <summary>根据传入参数取得距离。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal DistanceKey Distance(double px, double py, double pz)
        {
            double x = Gap(MinX, MaxX, px), y = Gap(MinY, MaxY, py), z = Gap(MinZ, MaxZ, pz);
            double squared = x * x + y * y + z * z;
            if (squared >= DistanceKey.MinimumNormal && !double.IsInfinity(squared))
                return new DistanceKey(squared, 1);
            if (x == 0 && y == 0 && z == 0) return new DistanceKey(0, 0);
            if (double.IsInfinity(squared))
            {
                double distance = Norm(x, y, z);
                if (!double.IsInfinity(distance))
                {
                    // An overflowing squared sum can still round to a norm
                    // on the ordinary side of the category boundary.
                    if (distance <= DistanceKey.MaximumOrdinaryDistance)
                        return new DistanceKey(distance * distance, 1);
                    return new DistanceKey(distance * DistanceKey.ScaleDown, 2);
                }
                return new DistanceKey(ScaledDistance(px, py, pz, DistanceKey.ScaleDown), 2);
            }
            double smallDistance = Norm(x, y, z);
            if (smallDistance >= DistanceKey.MinimumOrdinaryDistance)
                return new DistanceKey(smallDistance * smallDistance, 1);
            if (smallDistance >= DistanceKey.MinimumNormal)
                return new DistanceKey(smallDistance * DistanceKey.ScaleUp, 0);
            // Scale before the final norm is rounded when the distance itself
            // is subnormal, retaining Epsilon axis/diagonal distinctions.
            return new DistanceKey(ScaledDistance(px, py, pz, DistanceKey.ScaleUp), 0);
        }

        /// <summary>根据传入参数取得已缩放距离。</summary>
        private double ScaledDistance(double px, double py, double pz, double scale)
        {
            return Norm(ScaledGap(MinX, MaxX, px, scale),
                        ScaledGap(MinY, MaxY, py, scale),
                        ScaledGap(MinZ, MaxZ, pz, scale));
        }

        /// <summary>根据传入参数取得间距。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double Gap(double minimum, double maximum, double point)
        {
            if (point < minimum) return minimum - point;
            if (point > maximum) return point - maximum;
            return 0;
        }

        /// <summary>根据传入参数取得已缩放间距。</summary>
        private static double ScaledGap(double minimum, double maximum, double point, double scale)
        {
            // Test containment before scaling so a zero-gap axis cannot turn
            // into infinity - infinity when tiny distances are scaled up.
            if (point < minimum) return minimum * scale - point * scale;
            if (point > maximum) return point * scale - maximum * scale;
            return 0;
        }

        /// <summary>根据传入参数取得范数。</summary>
        private static double Norm(double x, double y, double z)
        {
            double squared = x * x + y * y + z * z;
            if (squared >= DistanceKey.MinimumNormal && !double.IsInfinity(squared)) return Math.Sqrt(squared);
            double scale = Math.Max(x, Math.Max(y, z));
            if (scale == 0 || double.IsInfinity(scale)) return scale;
            x /= scale; y /= scale; z /= scale;
            return scale * Math.Sqrt(x * x + y * y + z * z);
        }
    }

    // Ordinary comparisons stay squared; extreme norms and nearby rounding
    // ties use a norm fallback. An unbounded limit ranks last.
    /// <summary>保存距离键所需的数据与配套操作。</summary>
    internal struct DistanceKey
    {
        internal const double MinimumNormal = 2.2250738585072014e-308;
        internal const double ScaleUp = 1.3407807929942597e154; // 2^512
        internal const double ScaleDown = 7.4583407312002067e-155; // 2^-512
        internal const double MinimumOrdinaryDistance = 1.4916681462400413e-154; // sqrt(MinimumNormal)
        internal const double MaximumOrdinaryDistance = 1.3407807929942596e154; // sqrt(double.MaxValue)
        internal readonly double value;
        internal readonly int rank;

        /// <summary>使用调用方提供的value、rank创建 DistanceKey。</summary>
        internal DistanceKey(double value, int rank)
        {
            this.value = value; this.rank = rank;
        }

        /// <summary>按此类型定义的比较规则确定两个值的先后顺序。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int CompareTo(DistanceKey other)
        {
            if (rank != other.rank) return rank.CompareTo(other.rank);
            if (value == other.value) return 0;
            if (rank == 1)
            {
                // Distinct nearby squares can have the same rounded norm.
                // Preserve the public nearest-tie semantics without taking a
                // square root for well-separated or exactly equal candidates.
                long difference = BitConverter.DoubleToInt64Bits(value) - BitConverter.DoubleToInt64Bits(other.value);
                if (difference >= -4 && difference <= 4)
                    return Math.Sqrt(value).CompareTo(Math.Sqrt(other.value));
            }
            return value.CompareTo(other.value);
        }

        /// <summary>根据传入参数取得上限。</summary>
        internal static DistanceKey Limit(double radius)
        {
            if (double.IsNaN(radius) || radius < 0) throw new ArgumentOutOfRangeException("radius");
            if (double.IsPositiveInfinity(radius)) return new DistanceKey(0, 3);
            double squared = radius * radius;
            if (double.IsInfinity(squared)) return new DistanceKey(radius * ScaleDown, 2);
            if (squared < MinimumNormal) return new DistanceKey(radius * ScaleUp, 0);

            // Squaring a rounded radius can round inward: sqrt(3)^2 is the
            // predecessor of 3. Choose the last squared value whose rounded
            // square root still satisfies the inclusive limit. This work is
            // done once per query; ordinary candidate distances stay squared.
            while (Math.Sqrt(squared) > radius)
                squared = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(squared) - 1);
            while (squared < double.MaxValue)
            {
                double next = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(squared) + 1);
                if (Math.Sqrt(next) > radius) break;
                squared = next;
            }
            return new DistanceKey(squared, 1);
        }
    }
}

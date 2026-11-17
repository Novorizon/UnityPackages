using System;

namespace RTree
{
    /// <summary>保存三维向量 d所需的数据与配套操作。</summary>
    public struct Vector3d : IEquatable<Vector3d>
    {
        public readonly double X, Y, Z;
        /// <summary>使用调用方提供的x、y、z创建 Vector3d。</summary>
        public Vector3d(double x, double y, double z)
        {
            Rectangle.ValidateCoordinate(x, "x"); Rectangle.ValidateCoordinate(y, "y"); Rectangle.ValidateCoordinate(z, "z");
            X = x; Y = y; Z = z;
        }
        /// <summary>比较当前值和参数是否相等。</summary>
        public bool Equals(Vector3d other) { return X == other.X && Y == other.Y && Z == other.Z; }
        /// <summary>比较当前值和参数是否相等。</summary>
        public override bool Equals(object other) { return other is Vector3d && Equals((Vector3d)other); }
        /// <summary>根据参与相等比较的字段生成哈希值。</summary>
        public override int GetHashCode()
        { unchecked { return (X.GetHashCode() * 397 ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode(); } }
        /// <summary>定义 三维向量 d 对 + 运算符的处理结果。</summary>
        public static Vector3d operator +(Vector3d a, Vector3d b) { return new Vector3d(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        /// <summary>定义 三维向量 d 对 - 运算符的处理结果。</summary>
        public static Vector3d operator -(Vector3d a, Vector3d b) { return new Vector3d(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        /// <summary>定义 三维向量 d 对 * 运算符的处理结果。</summary>
        public static Vector3d operator *(Vector3d a, double scale) { return new Vector3d(a.X * scale, a.Y * scale, a.Z * scale); }
        /// <summary>根据传入参数取得轴向。</summary>
        internal double Axis(int axis) { return axis == 0 ? X : axis == 1 ? Y : Z; }
    }

    /// <summary>Finite axis-aligned bounds. Scalar order is minXYZ, maxXYZ.</summary>
    /// <remarks>保存三维包围盒所需的数据与配套操作。</remarks>
    public struct Bounds3 : IEquatable<Bounds3>
    {
        public readonly double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        /// <summary>使用调用方提供的minimum、maximum创建 Bounds3。</summary>
        public Bounds3(Vector3d minimum, Vector3d maximum)
            : this(minimum.X, minimum.Y, minimum.Z, maximum.X, maximum.Y, maximum.Z) { }
        /// <summary>使用调用方提供的minX、minY、minZ创建 Bounds3。</summary>
        public Bounds3(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            Rectangle.ValidateCoordinate(minX, "minX"); Rectangle.ValidateCoordinate(minY, "minY"); Rectangle.ValidateCoordinate(minZ, "minZ");
            Rectangle.ValidateCoordinate(maxX, "maxX"); Rectangle.ValidateCoordinate(maxY, "maxY"); Rectangle.ValidateCoordinate(maxZ, "maxZ");
            if (minX > maxX || minY > maxY || minZ > maxZ)
                throw new ArgumentException("Each minimum must be less than or equal to its maximum.");
            MinX = minX; MinY = minY; MinZ = minZ; MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
        }
        public Vector3d Minimum { get { return new Vector3d(MinX, MinY, MinZ); } }
        public Vector3d Maximum { get { return new Vector3d(MaxX, MaxY, MaxZ); } }
        public Vector3d Center { get { Aabb b = Box; return new Vector3d(b.Center(0), b.Center(1), b.Center(2)); } }
        internal Aabb Box { get { return new Aabb(MinX, MinY, MinZ, MaxX, MaxY, MaxZ); } }
        /// <summary>使用调用方提供的box创建 Bounds3。</summary>
        private Bounds3(Aabb box)
        { MinX = box.MinX; MinY = box.MinY; MinZ = box.MinZ; MaxX = box.MaxX; MaxY = box.MaxY; MaxZ = box.MaxZ; }
        /// <summary>按提供的轴对齐包围盒构建返回值。</summary>
        internal static Bounds3 FromAabb(Aabb box) { return new Bounds3(box); }
        /// <summary>按提供的矩形构建返回值。</summary>
        public static Bounds3 FromRectangle(Rectangle rectangle) { return FromAabb(Aabb.FromRectangle(rectangle)); }
        /// <summary>将当前值转换为矩形。</summary>
        public Rectangle ToRectangle() { return Box.ToRectangle(); }
        /// <summary>判断是否相交三维包围盒。</summary>
        public bool Intersects(Bounds3 other) { return Box.Intersects(other.Box); }
        /// <summary>检查是否包含三维包围盒。</summary>
        public bool Contains(Bounds3 other) { return Box.Contains(other.Box); }
        /// <summary>检查是否包含三维包围盒。</summary>
        public bool Contains(Vector3d point)
        { return point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY && point.Z >= MinZ && point.Z <= MaxZ; }
        /// <summary>根据传入参数取得并集。</summary>
        public static Bounds3 Union(Bounds3 left, Bounds3 right) { return FromAabb(Aabb.Union(left.Box, right.Box)); }
        /// <summary>根据传入参数取得展开后的。</summary>
        public Bounds3 Expanded(double margin) { return Expanded(margin, default(Vector3d), 0); }
        /// <summary>根据传入参数取得展开后的。</summary>
        public Bounds3 Expanded(double margin, Vector3d displacement, double multiplier)
        {
            if (double.IsNaN(margin) || double.IsInfinity(margin) || margin < 0) throw new ArgumentOutOfRangeException("margin");
            if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier < 0) throw new ArgumentOutOfRangeException("multiplier");
            double dx = displacement.X * multiplier, dy = displacement.Y * multiplier, dz = displacement.Z * multiplier;
            return new Bounds3(SpatialMath.ClampFinite(MinX - margin + Math.Min(0, dx)),
                SpatialMath.ClampFinite(MinY - margin + Math.Min(0, dy)), SpatialMath.ClampFinite(MinZ - margin + Math.Min(0, dz)),
                SpatialMath.ClampFinite(MaxX + margin + Math.Max(0, dx)), SpatialMath.ClampFinite(MaxY + margin + Math.Max(0, dy)),
                SpatialMath.ClampFinite(MaxZ + margin + Math.Max(0, dz)));
        }
        /// <summary>比较当前值和参数是否相等。</summary>
        public bool Equals(Bounds3 other) { return Box.EqualsBounds(other.Box); }
        /// <summary>比较当前值和参数是否相等。</summary>
        public override bool Equals(object other) { return other is Bounds3 && Equals((Bounds3)other); }
        /// <summary>根据参与相等比较的字段生成哈希值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MinX.GetHashCode(); hash = hash * 397 ^ MinY.GetHashCode(); hash = hash * 397 ^ MinZ.GetHashCode();
                hash = hash * 397 ^ MaxX.GetHashCode(); hash = hash * 397 ^ MaxY.GetHashCode(); return hash * 397 ^ MaxZ.GetHashCode();
            }
        }
    }

    /// <summary>保存球体3所需的数据与配套操作。</summary>
    public struct Sphere3
    {
        public readonly Vector3d Center;
        public readonly double Radius;
        internal readonly DistanceKey Limit;
        /// <summary>使用调用方提供的center、radius创建 Sphere3。</summary>
        public Sphere3(Vector3d center, double radius)
        {
            if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 0) throw new ArgumentOutOfRangeException("radius");
            Center = center; Radius = radius; Limit = DistanceKey.Limit(radius);
        }
    }

    /// <summary>The inside half-space is normal dot position + offset >= 0.</summary>
    /// <remarks>保存平面3所需的数据与配套操作。</remarks>
    public struct Plane3
    {
        public readonly Vector3d Normal;
        public readonly double Offset;
        /// <summary>使用调用方提供的normal、offset创建 Plane3。</summary>
        public Plane3(Vector3d normal, double offset)
        {
            Rectangle.ValidateCoordinate(offset, "offset");
            if (normal.X == 0 && normal.Y == 0 && normal.Z == 0) throw new ArgumentException("A plane needs a nonzero normal.", "normal");
            Normal = normal; Offset = offset;
        }
        /// <summary>验证平面3。</summary>
        internal void Validate()
        {
            if (Normal.X == 0 && Normal.Y == 0 && Normal.Z == 0) throw new ArgumentException("A plane needs a nonzero normal.");
        }
        /// <summary>返回Outside的判断结果。</summary>
        internal bool Outside(Bounds3 bounds)
        {
            double x = Normal.X >= 0 ? bounds.MaxX : bounds.MinX;
            double y = Normal.Y >= 0 ? bounds.MaxY : bounds.MinY;
            double z = Normal.Z >= 0 ? bounds.MaxZ : bounds.MinZ;
            double ax = Normal.X * x, by = Normal.Y * y, cz = Normal.Z * z;
            double magnitude = Math.Abs(ax) + Math.Abs(by) + Math.Abs(cz) + Math.Abs(Offset);
            // Ordinary coordinates need no normalization. The error allowance
            // covers cancellation; extreme sums keep the scaled robust path.
            if (magnitude >= 1e-280 && magnitude <= 1e280)
                return ax + by + cz + Offset < -3.552713678800501e-15 * magnitude;
            double coordinates = Math.Max(1, Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))));
            double coefficients = Math.Max(Math.Abs(Offset), Math.Max(Math.Abs(Normal.X), Math.Max(Math.Abs(Normal.Y), Math.Abs(Normal.Z))));
            double a = (Normal.X / coefficients) * (x / coordinates);
            double b = (Normal.Y / coefficients) * (y / coordinates);
            double c = (Normal.Z / coefficients) * (z / coordinates);
            double d = (Offset / coefficients) / coordinates;
            double error = 3.552713678800501e-15 * (Math.Abs(a) + Math.Abs(b) + Math.Abs(c) + Math.Abs(d));
            return a + b + c + d < -error;
        }
    }

    /// <summary>Immutable six-plane conservative box culling volume.</summary>
    /// <remarks>提供视锥体3的实现。</remarks>
    public sealed class Frustum3
    {
        private readonly Plane3[] planes;
        /// <summary>使用调用方提供的left、right、bottom创建 Frustum3。</summary>
        public Frustum3(Plane3 left, Plane3 right, Plane3 bottom, Plane3 top, Plane3 near, Plane3 far)
        {
            planes = new[] { left, right, bottom, top, near, far };
            for (int i = 0; i < planes.Length; i++) planes[i].Validate();
        }
        /// <summary>获取平面。</summary>
        public Plane3 GetPlane(int index)
        { if (index < 0 || index >= 6) throw new ArgumentOutOfRangeException("index"); return planes[index]; }
        /// <summary>判断是否相交视锥体3。</summary>
        public bool Intersects(Bounds3 bounds)
        {
            for (int i = 0; i < planes.Length; i++) if (planes[i].Outside(bounds)) return false;
            return true;
        }
    }

    /// <summary>A forward ray, with an inclusive parameter interval and an unnormalized direction.</summary>
    /// <remarks>保存射线3所需的数据与配套操作。</remarks>
    public struct Ray3
    {
        public readonly Vector3d Origin, Direction;
        public readonly double TMin, TMax;
        /// <summary>用默认配置创建 Ray3，其余初始化交给完整构造函数。</summary>
        public Ray3(Vector3d origin, Vector3d direction) : this(origin, direction, 0, double.PositiveInfinity) { }
        /// <summary>使用调用方提供的origin、direction、tMin创建 Ray3。</summary>
        public Ray3(Vector3d origin, Vector3d direction, double tMin, double tMax)
        {
            Origin = origin; Direction = direction; TMin = tMin; TMax = tMax; Validate();
        }
        /// <summary>验证射线3。</summary>
        internal void Validate()
        {
            if (Direction.X == 0 && Direction.Y == 0 && Direction.Z == 0) throw new ArgumentException("A ray needs a nonzero direction.", "direction");
            if (double.IsNaN(TMin) || double.IsInfinity(TMin) || TMin < 0) throw new ArgumentOutOfRangeException("tMin");
            if (double.IsNaN(TMax) || TMax < TMin) throw new ArgumentOutOfRangeException("tMax");
        }
        /// <summary>按位置取得射线3。</summary>
        public Vector3d At(double parameter)
        {
            Rectangle.ValidateCoordinate(parameter, "parameter");
            return new Vector3d(SpatialMath.MultiplyAdd(Direction.X, parameter, Origin.X),
                SpatialMath.MultiplyAdd(Direction.Y, parameter, Origin.Y), SpatialMath.MultiplyAdd(Direction.Z, parameter, Origin.Z));
        }
        /// <summary>判断是否相交射线3。</summary>
        public bool Intersects(Bounds3 bounds, out double near, out double far)
        {
            Validate();
            return IntersectsUnchecked(bounds.Box, out near, out far);
        }
        // Tree entry points validate once. Internal bounds were validated when
        // inserted, so traversal can retain the same robust slabs without
        // repeating public argument validation for every visited bound.
        /// <summary>判断是否相交 Unchecked。</summary>
        internal bool IntersectsUnchecked(Bounds3 bounds, out double near, out double far)
        {
            return IntersectsUnchecked(bounds.Box, out near, out far);
        }
        /// <summary>判断是否相交 Unchecked。</summary>
        internal bool IntersectsUnchecked(Aabb bounds, out double near, out double far)
        {
            near = TMin; far = TMax;
            if (!Slab(bounds.MinX, bounds.MaxX, Origin.X, Direction.X, ref near, ref far)) return false;
            if (!Slab(bounds.MinY, bounds.MaxY, Origin.Y, Direction.Y, ref near, ref far)) return false;
            if (!Slab(bounds.MinZ, bounds.MaxZ, Origin.Z, Direction.Z, ref near, ref far)) return false;
            // Public hit parameters are representable doubles. An unbounded
            // upper interval does not make an infinite entry parameter a hit.
            return !double.IsInfinity(near) && near <= far;
        }
        /// <summary>返回Slab的判断结果。</summary>
        private static bool Slab(double minimum, double maximum, double origin, double direction, ref double near, ref double far)
        {
            if (direction == 0) return origin >= minimum && origin <= maximum;
            double a = SpatialMath.DifferenceRatio(minimum, origin, direction);
            double b = SpatialMath.DifferenceRatio(maximum, origin, direction);
            if (a > b) { double swap = a; a = b; b = swap; }
            near = Math.Max(near, a); far = Math.Min(far, b);
            return near <= far;
        }
    }

    /// <summary>保存三角形3所需的数据与配套操作。</summary>
    public struct Triangle3
    {
        public readonly Vector3d A, B, C;
        internal readonly bool HasArea;
        internal readonly int ConstantAxesMask;
        /// <summary>使用调用方提供的a、b、c创建 Triangle3。</summary>
        public Triangle3(Vector3d a, Vector3d b, Vector3d c)
        {
            A = a; B = b; C = c;
            ConstantAxesMask = (a.X == b.X && a.X == c.X ? 1 : 0) |
                (a.Y == b.Y && a.Y == c.Y ? 2 : 0) | (a.Z == b.Z && a.Z == c.Z ? 4 : 0);
            HasArea = TriangleIntersection.HasArea(a, b, c);
        }
        public Bounds3 Bounds
        {
            get { return new Bounds3(Math.Min(A.X, Math.Min(B.X, C.X)), Math.Min(A.Y, Math.Min(B.Y, C.Y)), Math.Min(A.Z, Math.Min(B.Z, C.Z)),
                Math.Max(A.X, Math.Max(B.X, C.X)), Math.Max(A.Y, Math.Max(B.Y, C.Y)), Math.Max(A.Z, Math.Max(B.Z, C.Z))); }
        }
        /// <summary>Two-sided inclusive edge intersection; coplanar and degenerate triangles miss.</summary>
        /// <remarks>判断是否相交三角形3。</remarks>
        public bool Intersects(Ray3 ray, out double parameter)
        {
            return TriangleIntersection.Intersects(this, ray, out parameter);
        }
    }

    /// <summary>提供空间索引 Math的实现。</summary>
    internal static class SpatialMath
    {
        /// <summary>限制 Finite。</summary>
        internal static double ClampFinite(double value)
        {
            if (double.IsPositiveInfinity(value)) return double.MaxValue;
            if (double.IsNegativeInfinity(value)) return -double.MaxValue;
            return value;
        }
        /// <summary>判断是否相交空间索引 Math。</summary>
        internal static bool Intersects(Bounds3 bounds, Sphere3 sphere)
        { return bounds.Box.Distance(sphere.Center.X, sphere.Center.Y, sphere.Center.Z).CompareTo(sphere.Limit) <= 0; }
        /// <summary>根据传入参数取得距离。</summary>
        internal static double Distance(Vector3d a, Vector3d b)
        { return DistanceFromKey(new Aabb(a.X, a.Y, a.Z, a.X, a.Y, a.Z).Distance(b.X, b.Y, b.Z)); }
        /// <summary>根据传入参数取得距离 From 键。</summary>
        internal static double DistanceFromKey(DistanceKey key)
        {
            if (key.rank == 0) return key.value / DistanceKey.ScaleUp;
            if (key.rank == 1) return Math.Sqrt(key.value);
            if (key.rank == 2) return key.value / DistanceKey.ScaleDown;
            return double.PositiveInfinity;
        }
        /// <summary>根据传入参数取得差异比例。</summary>
        internal static double DifferenceRatio(double a, double b, double divisor)
        {
            double difference = a - b;
            return double.IsInfinity(difference) ? a / divisor - b / divisor : difference / divisor;
        }
        /// <summary>根据传入参数取得MultiplyAdd。</summary>
        internal static double MultiplyAdd(double a, double b, double c)
        {
            double value = a * b + c;
            if (double.IsInfinity(value)) value = (a * 0.5 * b + c * 0.5) * 2;
            return value;
        }
        /// <summary>根据传入参数取得商品比例。</summary>
        internal static double ProductRatio(double a, double b, double c, double d)
        {
            if (a == 0) return 0;
            double product = a * b, divisor = c * d, result = product / divisor;
            if (product != 0 && divisor != 0 && !double.IsInfinity(product) && !double.IsInfinity(divisor)) return result;
            bool negative = (a < 0) != (c < 0);
            int ea, eb, ec, ed;
            double mantissa = (Fraction(Math.Abs(a), out ea) * Fraction(b, out eb)) /
                (Fraction(Math.Abs(c), out ec) * Fraction(d, out ed));
            int exponent = ea + eb - ec - ed;
            while (exponent > 512) { mantissa *= 1.3407807929942597e154; exponent -= 512; }
            while (exponent < -512) { mantissa *= 7.4583407312002067e-155; exponent += 512; }
            result = mantissa * BitConverter.Int64BitsToDouble((long)(exponent + 1023) << 52);
            return negative ? -result : result;
        }
        /// <summary>根据传入参数取得Fraction。</summary>
        private static double Fraction(double value, out int exponent)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int adjustment = 0;
            if ((bits & 0x7ff0000000000000L) == 0)
            { value *= 18014398509481984.0; bits = BitConverter.DoubleToInt64Bits(value); adjustment = -54; }
            exponent = (int)((bits >> 52) & 2047) - 1023 + adjustment;
            return BitConverter.Int64BitsToDouble((bits & 0x000fffffffffffffL) | 0x3ff0000000000000L);
        }
    }
}

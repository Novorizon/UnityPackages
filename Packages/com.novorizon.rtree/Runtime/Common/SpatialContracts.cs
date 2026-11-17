using System;
using System.Collections.Generic;
using System.Threading;

namespace RTree
{
    /// <summary>Queries of real object bounds; payloads may occur more than once.</summary>
    /// <remarks>定义包围盒 Query3的调用契约。</remarks>
    public interface IBoundsQuery3<T>
    {
        int Count { get; }
        /// <summary>查询包围盒 Query3。</summary>
        int Query(Bounds3 bounds, List<T> results, QueryContext context);
        /// <summary>按球体查询匹配结果。</summary>
        int QuerySphere(Sphere3 sphere, List<T> results, QueryContext context);
        /// <summary>按视锥体查询匹配结果。</summary>
        int QueryFrustum(Frustum3 frustum, List<T> results, QueryContext context);
    }

    /// <summary>Mutable object bounds identified by index-owned, generation-checked handles.</summary>
    /// <remarks>定义可变包围盒索引3的调用契约。</remarks>
    public interface IMutableBoundsIndex3<T> : IBoundsQuery3<T>
    {
        /// <summary>插入可变包围盒索引3。</summary>
        ProxyHandle Insert(Bounds3 bounds, T item);
        /// <summary>Updates bounds; true means structural reinsertion, not success.</summary>
        /// <remarks>移动可变包围盒索引3。</remarks>
        bool Move(ProxyHandle handle, Bounds3 bounds);
        /// <summary>移除可变包围盒索引3。</summary>
        bool Remove(ProxyHandle handle);
        /// <summary>尝试取得包围盒，返回是否成功。</summary>
        bool TryGetBounds(ProxyHandle handle, out Bounds3 bounds);
    }

    /// <summary>Unique unordered pairs whose current real AABBs overlap, before narrow phase.</summary>
    /// <remarks>定义IBroad 阶段3的调用契约。</remarks>
    public interface IBroadPhase3<T> : IMutableBoundsIndex3<T>
    {
        /// <summary>按配对查询匹配结果。</summary>
        int QueryPairs(List<ProxyPair<T>> results, QueryContext context);
    }

    /// <summary>
    /// Ray parameters use the supplied direction, without normalization.
    /// Box indexes return box hits; TriangleBvh returns exact triangle hits.
    /// </summary>
    /// <remarks>定义射线 Query3的调用契约。</remarks>
    public interface IRayQuery3<T>
    {
        /// <summary>执行射线检测，找到任一满足条件的命中即返回。</summary>
        bool RaycastAny(Ray3 ray, out RayHit<T> hit, QueryContext context);
        /// <summary>执行射线检测并查找最近的命中。</summary>
        bool RaycastClosest(Ray3 ray, out RayHit<T> hit, QueryContext context);
        /// <summary>执行射线检测并收集有效命中。</summary>
        int RaycastAll(Ray3 ray, List<RayHit<T>> results, QueryContext context);
    }

    /// <summary>定义射线相交检测器的调用契约。</summary>
    public interface IRayIntersector<T>
    {
        /// <summary>检测相交射线相交检测器。</summary>
        bool Intersect(T item, Ray3 ray, out double parameter);
    }

    /// <summary>定义点 Query3的调用契约。</summary>
    public interface IPointQuery3<T>
    {
        int Count { get; }
        /// <summary>根据传入参数取得半径。</summary>
        int Radius(Vector3d point, double maximumDistance, List<Neighbor<T>> results, QueryContext context);
        /// <summary>根据传入参数取得K 最近。</summary>
        int KNearest(Vector3d point, int k, double maximumDistance, List<Neighbor<T>> results, QueryContext context);
    }

    /// <summary>保存射线命中所需的数据与配套操作。</summary>
    public struct RayHit<T>
    {
        public readonly T Item;
        public readonly double Parameter;
        /// <summary>使用调用方提供的item、parameter创建 RayHit。</summary>
        public RayHit(T item, double parameter) { Item = item; Parameter = parameter; }
    }

    /// <summary>保存Neighbor所需的数据与配套操作。</summary>
    public struct Neighbor<T>
    {
        public readonly T Item;
        public readonly double Distance;
        /// <summary>使用调用方提供的item、distance创建 Neighbor。</summary>
        public Neighbor(T item, double distance) { Item = item; Distance = distance; }
    }

    /// <summary>A proxy identity, scoped to its index and invalidated on removal.</summary>
    /// <remarks>代理对应的句柄。保存访问本次操作或对象所需的标识。</remarks>
    public struct ProxyHandle : IEquatable<ProxyHandle>
    {
        public readonly int Slot;
        public readonly long Generation;
        public readonly long Owner;
        /// <summary>使用调用方提供的slot、generation、owner创建 ProxyHandle。</summary>
        internal ProxyHandle(int slot, long generation, long owner)
        { Slot = slot; Generation = generation; Owner = owner; }
        /// <summary>Checks handle format only. Use TryGetBounds to check whether it is still live.</summary>
        public bool IsValid { get { return Owner > 0 && Slot >= 0 && Generation > 0; } }
        /// <summary>比较当前值和参数是否相等。</summary>
        public bool Equals(ProxyHandle other)
        { return Slot == other.Slot && Generation == other.Generation && Owner == other.Owner; }
        /// <summary>比较当前值和参数是否相等。</summary>
        public override bool Equals(object value) { return value is ProxyHandle && Equals((ProxyHandle)value); }
        /// <summary>根据参与相等比较的字段生成哈希值。</summary>
        public override int GetHashCode()
        { unchecked { return (Slot * 397 ^ Generation.GetHashCode()) * 397 ^ Owner.GetHashCode(); } }
        /// <summary>定义 代理句柄 对 == 运算符的处理结果。</summary>
        public static bool operator ==(ProxyHandle left, ProxyHandle right) { return left.Equals(right); }
        /// <summary>定义 代理句柄 对 != 运算符的处理结果。</summary>
        public static bool operator !=(ProxyHandle left, ProxyHandle right) { return !left.Equals(right); }
    }

    /// <summary>保存代理对所需的数据与配套操作。</summary>
    public struct ProxyPair<T>
    {
        public readonly ProxyHandle HandleA, HandleB;
        public readonly T ItemA, ItemB;
        /// <summary>使用调用方提供的handleA、handleB、itemA创建 ProxyPair。</summary>
        public ProxyPair(ProxyHandle handleA, ProxyHandle handleB, T itemA, T itemB)
        { HandleA = handleA; HandleB = handleB; ItemA = itemA; ItemB = itemB; }
    }

    /// <summary>提供空间索引 QueryWorkspaces的实现。</summary>
    internal static class SpatialQueryWorkspaces
    {
        private static readonly ThreadLocal<QueryContext> workspaces =
            new ThreadLocal<QueryContext>(delegate { return new QueryContext(); });
        /// <summary>获取空间索引 QueryWorkspaces。</summary>
        internal static QueryContext Get() { return workspaces.Value; }
        /// <summary>验证结果。</summary>
        internal static void ValidateResults<T>(List<T> results, QueryContext context)
        {
            if (results == null) throw new ArgumentNullException("results");
            if (context == null) throw new ArgumentNullException("context");
        }
    }

    /// <summary>提供代理 Owners的实现。</summary>
    internal static class ProxyOwners
    {
        private static long next;
        /// <summary>分配代理 Owners。</summary>
        internal static long Allocate()
        {
            long value = Interlocked.Increment(ref next);
            if (value <= 0) throw new InvalidOperationException("Proxy owner identifiers are exhausted.");
            return value;
        }
    }

    // The owning index provides synchronization. Query loops index payloads
    // directly; only writes can grow these arrays or recycle a generation.
    /// <summary>提供代理商店的实现。</summary>
    internal sealed class ProxyStore<T>
    {
        internal T[] Items = new T[16];
        internal Bounds3[] Bounds = new Bounds3[16];
        internal bool[] Live = new bool[16];
        internal long[] Generations = new long[16];
        internal int Next, Count;
        private readonly long owner = ProxyOwners.Allocate();
        private readonly Stack<int> free = new Stack<int>();

        /// <summary>分配代理商店。</summary>
        internal int Allocate(Bounds3 bounds, T item)
        {
            if (ReferenceEquals(item, null)) throw new ArgumentNullException("item");
            int slot;
            if (free.Count != 0)
            {
                slot = free.Peek();
                if (Generations[slot] == long.MaxValue)
                    throw new InvalidOperationException("Proxy generation identifiers are exhausted.");
                free.Pop();
            }
            else
            {
                if (Next == Items.Length) Grow();
                slot = Next++;
            }
            Generations[slot]++;
            Items[slot] = item; Bounds[slot] = bounds; Live[slot] = true; Count++;
            return slot;
        }

        /// <summary>扩大代理商店。</summary>
        private void Grow()
        {
            int size = checked(Items.Length * 2);
            var items = new T[size]; var bounds = new Bounds3[size];
            var live = new bool[size]; var generations = new long[size];
            Array.Copy(Items, items, Next); Array.Copy(Bounds, bounds, Next);
            Array.Copy(Live, live, Next); Array.Copy(Generations, generations, Next);
            Items = items; Bounds = bounds; Live = live; Generations = generations;
        }

        /// <summary>结束代理商店对相关资源的持有，并执行该类型的清理流程。</summary>
        internal void Release(int slot)
        {
            Items[slot] = default(T); Bounds[slot] = default(Bounds3);
            Live[slot] = false; Count--; free.Push(slot);
        }

        /// <summary>尝试解析代理商店，返回是否成功。</summary>
        internal bool TryResolve(ProxyHandle handle, out int slot)
        {
            slot = handle.Slot;
            return handle.Owner == owner && slot >= 0 && slot < Next && Live[slot]
                && Generations[slot] == handle.Generation;
        }

        /// <summary>处理传入的 slot。</summary>
        internal ProxyHandle Handle(int slot) { return new ProxyHandle(slot, Generations[slot], owner); }
    }
}

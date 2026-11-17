using System;
using System.Collections.Generic;

namespace RTree
{
    /// <summary>Convenience overloads for interface-typed indexes. Explicit contexts remain available.</summary>
    /// <remarks>空间索引 Query对应的扩展方法。为相关类型提供扩展方法。</remarks>
    public static class SpatialQueryExtensions
    {
        /// <summary>查询空间索引 Query 扩展方法。</summary>
        public static int Query<T>(this IBoundsQuery3<T> index, Bounds3 bounds, List<T> results)
        { Require(index); return index.Query(bounds, results, SpatialQueryWorkspaces.Get()); }

        /// <summary>按球体查询匹配结果。</summary>
        public static int QuerySphere<T>(this IBoundsQuery3<T> index, Sphere3 sphere, List<T> results)
        { Require(index); return index.QuerySphere(sphere, results, SpatialQueryWorkspaces.Get()); }

        /// <summary>按视锥体查询匹配结果。</summary>
        public static int QueryFrustum<T>(this IBoundsQuery3<T> index, Frustum3 frustum, List<T> results)
        { Require(index); return index.QueryFrustum(frustum, results, SpatialQueryWorkspaces.Get()); }

        /// <summary>执行射线检测，找到任一满足条件的命中即返回。</summary>
        public static bool RaycastAny<T>(this IRayQuery3<T> index, Ray3 ray, out RayHit<T> hit)
        { Require(index); return index.RaycastAny(ray, out hit, SpatialQueryWorkspaces.Get()); }

        /// <summary>执行射线检测并查找最近的命中。</summary>
        public static bool RaycastClosest<T>(this IRayQuery3<T> index, Ray3 ray, out RayHit<T> hit)
        { Require(index); return index.RaycastClosest(ray, out hit, SpatialQueryWorkspaces.Get()); }

        /// <summary>执行射线检测并收集有效命中。</summary>
        public static int RaycastAll<T>(this IRayQuery3<T> index, Ray3 ray, List<RayHit<T>> results)
        { Require(index); return index.RaycastAll(ray, results, SpatialQueryWorkspaces.Get()); }

        /// <summary>根据传入参数取得半径。</summary>
        public static int Radius<T>(this IPointQuery3<T> index, Vector3d point, double maximumDistance, List<Neighbor<T>> results)
        { Require(index); return index.Radius(point, maximumDistance, results, SpatialQueryWorkspaces.Get()); }

        /// <summary>根据传入参数取得K 最近。</summary>
        public static int KNearest<T>(this IPointQuery3<T> index, Vector3d point, int k, double maximumDistance, List<Neighbor<T>> results)
        { Require(index); return index.KNearest(point, k, maximumDistance, results, SpatialQueryWorkspaces.Get()); }

        /// <summary>根据传入参数取得K 最近。</summary>
        public static int KNearest<T>(this IPointQuery3<T> index, Vector3d point, int k, List<Neighbor<T>> results)
        { Require(index); return index.KNearest(point, k, double.PositiveInfinity, results, SpatialQueryWorkspaces.Get()); }

        /// <summary>根据传入参数取得K 最近。</summary>
        public static int KNearest<T>(this IPointQuery3<T> index, Vector3d point, int k, List<Neighbor<T>> results, QueryContext context)
        { Require(index); return index.KNearest(point, k, double.PositiveInfinity, results, context); }

        /// <summary>按配对查询匹配结果。</summary>
        public static int QueryPairs<T>(this IBroadPhase3<T> index, List<ProxyPair<T>> results)
        { Require(index); return index.QueryPairs(results, SpatialQueryWorkspaces.Get()); }

        /// <summary>检查空间索引 Query 扩展方法是否满足前置条件。</summary>
        private static void Require(object index)
        { if (index == null) throw new ArgumentNullException("index"); }
    }
}

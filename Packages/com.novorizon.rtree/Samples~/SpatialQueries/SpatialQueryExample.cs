using System.Collections.Generic;
using UnityEngine;
using RTree;

/// <summary>提供空间索引 Query 示例的实现。</summary>
public sealed class SpatialQueryExample : MonoBehaviour
{
    /// <summary>运行当前操作。</summary>
    [ContextMenu("Run Spatial Query Example")]
    public void Run()
    {
        IMutableBoundsIndex3<int> index = new RTreeIndex<int>();
        var results = new List<int>(8);
        var context = new QueryContext();
        var handle = index.Insert(new Bounds3(-1, -1, -1, 1, 1, 1), 42);
        index.QuerySphere(new Sphere3(new Vector3d(0, 0, 0), 3), results, context);
        Debug.Log("Initial sphere hits: " + results.Count);
        index.Move(handle, new Bounds3(10, 0, 10, 11, 1, 11));
        index.QuerySphere(new Sphere3(new Vector3d(0, 0, 0), 3), results, context);
        Debug.Log("Hits after move: " + results.Count);
        index.Remove(handle);
        Debug.Log("Removed the indexed object.");
    }
}

#nullable enable
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>Unity main-thread loader. Each successful load retains one lease until ReleasePrefab.</summary>
    /// <remarks>
    /// A shared prefab may be returned by several calls. Reference-count each successful acquisition;
    /// releasing one lease must not invalidate other views or consumers. Implementations retain ownership
    /// of their loader; UIRoot only releases the individual leases it acquired.
    /// </remarks>
    public interface IUIAssetLoader
    {
        Task<GameObject?> LoadPrefabAsync(string prefabPath);
        void ReleasePrefab(GameObject prefab);
    }
}

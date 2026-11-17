#nullable enable
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>Unity main-thread loader. Each successful load retains one lease until ReleasePrefab.</summary>
    /// <remarks>
    /// A shared prefab may be returned by several calls. Reference-count each successful acquisition;
    /// releasing one lease must not invalidate other views or consumers. Implementations retain ownership
    /// of their loader; UIManager only releases the individual leases it acquired.
    /// </remarks>
    /// <remarks>定义IUI 资源加载器的调用契约。</remarks>
    public interface IUIAssetLoader
    {
        /// <summary>异步加载预制件。</summary>
        Task<GameObject?> LoadPrefabAsync(string prefabPath);
        /// <summary>释放预制件。</summary>
        void ReleasePrefab(GameObject prefab);
    }
}
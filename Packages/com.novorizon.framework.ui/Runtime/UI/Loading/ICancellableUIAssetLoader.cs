#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>可选的逐调用取消接口；每次成功获取仍对应一次 ReleasePrefab。</summary>
    public interface ICancellableUIAssetLoader : IUIAssetLoader
    {
        /// <summary>异步加载预制件。</summary>
        Task<GameObject?> LoadPrefabAsync(string prefabPath, CancellationToken cancellationToken);
    }
}

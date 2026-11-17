#nullable enable
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>提供资源资源加载器的实现。</summary>
    public sealed class ResourcesAssetLoader : IUIAssetLoader
    {
        /// <summary>异步加载预制件。</summary>
        public Task<GameObject?> LoadPrefabAsync(string prefabPath)
        {
            ResourceRequest request = Resources.LoadAsync<GameObject>(prefabPath);
            if (request.isDone) return Task.FromResult(request.asset as GameObject);
            var completion = new TaskCompletionSource<GameObject?>();
            request.completed += _ => completion.TrySetResult(request.asset as GameObject);
            return completion.Task;
        }

        /// <summary>释放预制件。</summary>
        public void ReleasePrefab(GameObject prefab)
        {
            // Resources owns its shared asset. Unloading it here could invalidate other users.
        }
    }
}

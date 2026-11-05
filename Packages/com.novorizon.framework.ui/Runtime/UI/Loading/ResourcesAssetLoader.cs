#nullable enable
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    public sealed class ResourcesAssetLoader : IUIAssetLoader
    {
        public Task<GameObject?> LoadPrefabAsync(string prefabPath)
        {
            ResourceRequest request = Resources.LoadAsync<GameObject>(prefabPath);
            if (request.isDone) return Task.FromResult(request.asset as GameObject);
            var completion = new TaskCompletionSource<GameObject?>();
            request.completed += _ => completion.TrySetResult(request.asset as GameObject);
            return completion.Task;
        }

        public void ReleasePrefab(GameObject prefab)
        {
            // Resources owns its shared asset. Unloading it here could invalidate other users.
        }
    }
}

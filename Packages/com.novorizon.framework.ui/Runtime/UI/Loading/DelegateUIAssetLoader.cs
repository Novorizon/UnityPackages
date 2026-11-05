#nullable enable
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>Connect an application's prefab acquisition/release functions without a UI dependency on its resource library.</summary>
    public sealed class DelegateUIAssetLoader : IUIAssetLoader
    {
        readonly Func<string, Task<GameObject?>> load;
        readonly Action<GameObject> release;

        public DelegateUIAssetLoader(Func<string, Task<GameObject?>> load, Action<GameObject> release)
        {
            this.load = load ?? throw new ArgumentNullException(nameof(load));
            this.release = release ?? throw new ArgumentNullException(nameof(release));
        }

        public Task<GameObject?> LoadPrefabAsync(string prefabPath) => load(prefabPath);

        public void ReleasePrefab(GameObject prefab) => release(prefab);
    }
}

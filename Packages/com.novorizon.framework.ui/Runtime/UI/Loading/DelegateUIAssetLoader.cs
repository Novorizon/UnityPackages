#nullable enable
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace UI
{
    /// <summary>Connect an application's prefab acquisition/release functions without a UI dependency on its resource library.</summary>
    /// <remarks>提供委托界面资源加载器的实现。</remarks>
    public sealed class DelegateUIAssetLoader : IUIAssetLoader
    {
        readonly Func<string, Task<GameObject?>> load;
        readonly Action<GameObject> release;

        /// <summary>使用调用方提供的load、release创建 DelegateUIAssetLoader。</summary>
        public DelegateUIAssetLoader(Func<string, Task<GameObject?>> load, Action<GameObject> release)
        {
            this.load = load ?? throw new ArgumentNullException(nameof(load));
            this.release = release ?? throw new ArgumentNullException(nameof(release));
        }

        /// <summary>异步加载预制件。</summary>
        public Task<GameObject?> LoadPrefabAsync(string prefabPath) => load(prefabPath);

        /// <summary>释放预制件。</summary>
        public void ReleasePrefab(GameObject prefab) => release(prefab);
    }
}

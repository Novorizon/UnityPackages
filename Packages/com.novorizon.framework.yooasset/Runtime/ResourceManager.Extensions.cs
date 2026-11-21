///------------------------------------
/// Author：guanjinbiao
/// Mail：novogooglor@gmail.com
/// Date：2025-12-10
/// Description：资源管理器语法糖
///------------------------------------

using System;
using Game.Framework;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace Game.Resource
{
    /// <summary>资源对应的管理器。管理对应模块的状态和操作入口。</summary>
    public partial class ResourceManager
    {
        /// <summary>自动绑定图集时，管理器持有引用直到统一清理。</summary>
        private void RequestAtlas(string atlasName, Action<SpriteAtlas> callback)
        {
            if (callback != null)
            {
                RequestAtlasAsync(atlasName, callback).Forget();
            }
        }

        private async Task RequestAtlasAsync(string atlasName, Action<SpriteAtlas> callback)
        {
            string location = "Assets/GameData/SpriteAtlas/" + atlasName + ".spriteatlas";
            var atlas = await LoadAssetAsync<SpriteAtlas, SpriteAtlas>(location, value => value, AssetLifetime.AtlasBinding, CancellationToken.None);
            if (atlas != null)
            {
                callback(atlas);
            }
        }

        // GameObject
        /// <summary>异步加载 GameObject。</summary>
        public Task<GameObject> LoadGameObjectAsync(string location) => LoadAssetAsync<GameObject>(location);

        /// <summary>异步加载 GameObject。</summary>
        public Task LoadGameObjectAsync(string location, Action<GameObject> callback) => LoadAssetAsync<GameObject>(location, callback);

        // GameObject
        /// <summary>加载 GameObject。</summary>
        public GameObject LoadGameObject(string location) => LoadAsset<GameObject>(location);

        /// <summary>加载 GameObject。</summary>
        public void LoadGameObject(string location, Action<GameObject> callback) => LoadAsset<GameObject>(location, callback);

        // TextAsset
        /// <summary>异步加载文本资源。</summary>
        public Task<TextAsset> LoadTextAssetAsync(string location) => LoadAssetAsync<TextAsset>(location);

        /// <summary>异步加载文本资源。</summary>
        public Task LoadTextAssetAsync(string location, Action<TextAsset> callback) => LoadAssetAsync<TextAsset>(location, callback);
        /// <summary>加载文本资源。</summary>
        public TextAsset LoadTextAsset(string location) => LoadAsset<TextAsset>(location);

        /// <summary>加载文本资源。</summary>
        public void LoadTextAsset(string location, Action<TextAsset> callback) => LoadAsset<TextAsset>(location, callback);


        // SpriteAtlas
        /// <summary>异步加载精灵图图集。</summary>
        public Task<SpriteAtlas> LoadSpriteAtlasAsync(string location) => LoadAssetAsync<SpriteAtlas>(location);

        /// <summary>异步加载精灵图图集。</summary>
        public Task LoadSpriteAtlasAsync(string location, Action<SpriteAtlas> callback) => LoadAssetAsync<SpriteAtlas>(location, callback);

        /// <summary>加载精灵图图集。</summary>
        public SpriteAtlas LoadSpriteAtlas(string location) => LoadAsset<SpriteAtlas>(location);

        /// <summary>加载精灵图图集。</summary>
        public void LoadSpriteAtlas(string location, Action<SpriteAtlas> callback) => LoadAsset<SpriteAtlas>(location, callback);

        /// <summary>从图集取得 Sprite，返回的 Sprite 持有本次图集引用。使用结束后调用 Release(sprite)。</summary>
        public Task<Sprite> LoadSpriteAsync(string spriteAtlas, string location) =>
            LoadSpriteAsync(spriteAtlas, location, CancellationToken.None);

        /// <summary>异步取得图集中的 Sprite。取消、图集加载失败或找不到 Sprite 时返回 null。</summary>
        public Task<Sprite> LoadSpriteAsync(string spriteAtlas, string location, CancellationToken token) =>
            LoadAssetAsync<SpriteAtlas, Sprite>(spriteAtlas, atlas => atlas.GetSprite(location), AssetLifetime.SpriteClone, token);

        /// <summary>非空回调接收 Sprite 及本次图集引用。</summary>
        public async Task LoadSpriteAsync(string spriteAtlas, string location, Action<Sprite> callback)
        {
            if (callback != null && CheckMainThread(nameof(LoadSpriteAsync)))
            {
                callback(await LoadSpriteAsync(spriteAtlas, location));
            }
        }

        /// <summary>同步取得图集中的 Sprite。每次成功加载都要配对 Release(sprite)。</summary>
        public Sprite LoadSprite(string spriteAtlas, string location) =>
            LoadAsset<SpriteAtlas, Sprite>(spriteAtlas, atlas => atlas.GetSprite(location), AssetLifetime.SpriteClone);

        /// <summary>非空回调接收同步加载的 Sprite 及本次图集引用。</summary>
        public void LoadSprite(string spriteAtlas, string location, Action<Sprite> callback)
        {
            if (callback != null && CheckMainThread(nameof(LoadSprite)))
            {
                callback(LoadSprite(spriteAtlas, location));
            }
        }

        /// <summary>加载并实例化 Prefab。实例销毁后用 Release(instance) 归还其 Prefab 引用。</summary>
        public Task<GameObject> LoadInstantiateAsync(string location) =>
            LoadInstantiateAsync(location, CancellationToken.None);

        /// <summary>异步取得由独立 Prefab 引用支持的实例。取消或加载失败返回 null。</summary>
        public Task<GameObject> LoadInstantiateAsync(string location, CancellationToken token) =>
            LoadAssetAsync<GameObject, GameObject>(location, prefab => Object.Instantiate(prefab), AssetLifetime.PrefabInstance, token);

        /// <summary>非空回调接收本次实例及其 Prefab 引用。</summary>
        public async Task LoadInstantiateAsync(string location, Action<GameObject> callback)
        {
            if (callback != null && CheckMainThread(nameof(LoadInstantiateAsync)))
            {
                callback(await LoadInstantiateAsync(location));
            }
        }

        /// <summary>同步加载并实例化 Prefab。实例销毁后用 Release(instance) 归还其 Prefab 引用。</summary>
        public GameObject LoadInstantiate(string location) =>
            LoadAsset<GameObject, GameObject>(location, prefab => Object.Instantiate(prefab), AssetLifetime.PrefabInstance);

        /// <summary>非空回调接收同步创建的实例及其 Prefab 引用。</summary>
        public void LoadInstantiate(string location, Action<GameObject> callback)
        {
            if (callback != null && CheckMainThread(nameof(LoadInstantiate)))
            {
                callback(LoadInstantiate(location));
            }
        }

        /// <summary>检查当前值是否满足此类型的有效条件。</summary>
        public bool IsValid(string location)
        {
            if (string.IsNullOrEmpty(location))
                return false;

            if (package == null)
                return false;

            // 1. 是否存在于构建结果中
            if (!package.CheckLocationValid(location))
                return false;

            // 2. 是否已在本地（StreamingAssets / Cache）
            if (package.IsNeedDownloadFromRemote(location))
                return false;

            return true;
        }

    }
    /// <summary>提供UnityObjExt的实现。</summary>
    public static class UnityObjExt
    {
        /// <summary>根据传入参数取得Null。</summary>
        public static T Null<T>(this T obj) where T : UnityEngine.Object => obj ? obj : null;
    }


}

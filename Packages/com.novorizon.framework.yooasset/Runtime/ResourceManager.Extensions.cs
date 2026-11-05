///------------------------------------
/// Author：guanjinbiao
/// Mail：novogooglor@gmail.com
/// Date：2025-12-10
/// Description：资源管理器语法糖
///------------------------------------

using System;
using Game.Framework;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.U2D;
using YooAsset;
using Object = UnityEngine.Object;

namespace Game.Resource
{
    public partial class ResourceManager
    {
        private void RequestAtlas(string atlasName, Action<SpriteAtlas> callback)
        {
            RequestAtlasAsync(atlasName, callback).Forget();
        }
        private async Task RequestAtlasAsync(string atlasName, Action<SpriteAtlas> callback)
        {
            var atlas = await LoadAssetAsync<SpriteAtlas>("Assets/GameData/SpriteAtlas/" + atlasName + ".spriteatlas");
            callback?.Invoke(atlas);
        }

        // GameObject
        public Task<GameObject> LoadGameObjectAsync(string location) => LoadAssetAsync<GameObject>(location);

        public Task LoadGameObjectAsync(string location, Action<GameObject> callback) => LoadAssetAsync<GameObject>(location, callback);

        // GameObject
        public GameObject LoadGameObject(string location) => LoadAsset<GameObject>(location);

        public void LoadGameObject(string location, Action<GameObject> callback) => LoadAsset<GameObject>(location, callback);

        // TextAsset
        public Task<TextAsset> LoadTextAssetAsync(string location) => LoadAssetAsync<TextAsset>(location);

        public Task LoadTextAssetAsync(string location, Action<TextAsset> callback) => LoadAssetAsync<TextAsset>(location, callback);
        public TextAsset LoadTextAsset(string location) => LoadAsset<TextAsset>(location);

        public void LoadTextAsset(string location, Action<TextAsset> callback) => LoadAsset<TextAsset>(location, callback);


        // SpriteAtlas
        public Task<SpriteAtlas> LoadSpriteAtlasAsync(string location) => LoadAssetAsync<SpriteAtlas>(location);

        public Task LoadSpriteAtlasAsync(string location, Action<SpriteAtlas> callback) => LoadAssetAsync<SpriteAtlas>(location, callback);

        public SpriteAtlas LoadSpriteAtlas(string location) => LoadAsset<SpriteAtlas>(location);

        public void LoadSpriteAtlas(string location, Action<SpriteAtlas> callback) => LoadAsset<SpriteAtlas>(location, callback);

        // Sprite
        public async Task<Sprite> LoadSpriteAsync(string spriteAtlas, string location)
        {
            SpriteAtlas atlas = await LoadAssetAsync<SpriteAtlas>(spriteAtlas);
            Sprite sprite = atlas?.GetSprite(location);
            return sprite;
        }

        public async Task LoadSpriteAsync(string spriteAtlas, string location, Action<Sprite> callback)
        {
            SpriteAtlas atlas = await LoadAssetAsync<SpriteAtlas>(spriteAtlas);
            Sprite sprite = atlas?.GetSprite(location);
            callback?.Invoke(sprite);
        }

        public Sprite LoadSprite(string spriteAtlas, string location)
        {
            SpriteAtlas atlas = LoadAsset<SpriteAtlas>(spriteAtlas);
            Sprite sprite = atlas?.GetSprite(location);
            return sprite;
        }

        public void LoadSprite(string spriteAtlas, string location, Action<Sprite> callback)
        {
            SpriteAtlas atlas = LoadAsset<SpriteAtlas>(spriteAtlas);
            Sprite sprite = atlas?.GetSprite(location);
            callback?.Invoke(sprite);
        }

        /// 加载 prefab 并实例化
        public async Task<GameObject> LoadInstantiateAsync(string location)
        {
            GameObject prefab = await LoadGameObjectAsync(location);
            if (prefab == null)
            {
                return null;
            }

            GameObject instance = Object.Instantiate(prefab);
            return instance;
        }

        /// 加载 prefab 并实例化
        public async Task LoadInstantiateAsync(string location, Action<GameObject> callback)
        {
            GameObject instance = await LoadInstantiateAsync(location);
            callback?.Invoke(instance);
        }

        /// 加载 prefab 并实例化
        public GameObject LoadInstantiate(string location)
        {
            GameObject prefab = LoadGameObject(location);
            if (prefab == null)
            {
                return null;
            }

            GameObject instance = Object.Instantiate(prefab);
            return instance;
        }

        /// 加载 prefab 并实例化
        public void LoadInstantiate(string location, Action<GameObject> callback)
        {
            GameObject instance = LoadInstantiate(location);
            callback?.Invoke(instance);
        }

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

        //逻辑存在 不管下载
        //public bool IsAssetDefined(string location)
        //{
        //    return package.CheckLocationValid(location);
        //}

        //是否能立刻加载
        //public bool IsExist(string location)
        //{
        //    var info = package.GetAssetInfo(location);

        //    if (info.IsInvalid)
        //        return false;

        //    //return info.IsCached;
        //}


        //        private string NormalizeLocation(string location, Type type)
        //        {
        //            if (string.IsNullOrEmpty(location))
        //                return null;

        //            if(type == typeof(SpriteAtlas))
        //            {
        //                if (!location.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        //                {
        //                    return string.Format("Assets/GameData/SpriteAtlas/{0}.spriteatlas", location);
        //                }
        //            }
        //            if (!location.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        //            {
        //                location = "Assets/" + location;
        //            }

        //            if (Path.HasExtension(location))
        //            {
        //                return location;
        //            }

        //            if (TypeSuffixMap1.TryGetValue(type, out var suffix))
        //            {
        //                location += suffix;
        //            }
        //            else
        //            {
        //#if UNITY_EDITOR
        //                Debug.LogWarning($"NormalizeAssetLocation: No suffix rule for type {type.Name}, location = {location}");
        //#endif
        //            }

        //            return location;
        //        }


        //        private static readonly Dictionary<Type, string> TypeSuffixMap = new Dictionary<Type, string>
        //        {
        //            { typeof(Scene), ".unity" },
        //            { typeof(SpriteAtlas), ".spriteatlas" },
        //            { typeof(GameObject), ".prefab" },
        //            { typeof(Shader), ".shader" },
        //            { typeof(Texture), ".png" },
        //            { typeof(Material), ".material" },
        //        };
    }
    public static class UnityObjExt
    {
        public static T Null<T>(this T obj) where T : UnityEngine.Object => obj ? obj : null;
    }


}

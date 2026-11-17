using System;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Game.Framework.Scenes
{
    /// <summary>返回已完成加载的原生场景。实现必须等待原生操作结束后再完成 Task。</summary>
    public interface ISceneLoader
    {
        /// <summary>异步加载场景加载器。</summary>
        Task<Scene> LoadAsync(string location, LoadSceneMode mode, IProgress<float> progress);
        /// <summary>异步卸载场景加载器。</summary>
        Task UnloadAsync(Scene scene);
    }
}

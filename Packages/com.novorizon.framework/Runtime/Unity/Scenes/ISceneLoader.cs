using System;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace Game.Framework.Scenes
{
    /// <summary>返回已完成加载的原生场景。实现必须等待原生操作结束后再完成 Task。</summary>
    public interface ISceneLoader
    {
        /// <summary>加载场景。返回默认 Scene 表示失败，Task 完成前须等待已启动的原生操作。</summary>
        Task<Scene> LoadAsync(string location, LoadSceneMode mode, IProgress<float> progress);
        /// <summary>卸载场景。失败后若原生场景仍在加载状态，场景管理器保留记录。</summary>
        Task UnloadAsync(Scene scene);
    }
}

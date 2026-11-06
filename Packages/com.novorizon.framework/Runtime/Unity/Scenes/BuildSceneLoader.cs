using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using NativeSceneManager = UnityEngine.SceneManagement.SceneManager;

namespace Game.Framework.Scenes
{
    /// <summary>加载 Build Settings 中的场景。资源包场景通过 ISceneLoader 接入。</summary>
    public sealed class BuildSceneLoader : ISceneLoader
    {
        public async Task<Scene> LoadAsync(string location, LoadSceneMode mode, IProgress<float> progress)
        {
            var previous = new HashSet<int>();
            for (int i = 0; i < NativeSceneManager.sceneCount; i++)
            {
                previous.Add(NativeSceneManager.GetSceneAt(i).handle);
            }

            var operation = NativeSceneManager.LoadSceneAsync(location, mode);
            if (operation == null)
            {
                throw new InvalidOperationException("Cannot load scene: " + location);
            }

            await WaitAsync(operation, progress);
            string expectedName = Path.GetFileNameWithoutExtension(location);
            for (int i = 0; i < NativeSceneManager.sceneCount; i++)
            {
                var scene = NativeSceneManager.GetSceneAt(i);
                if (!previous.Contains(scene.handle) && scene.isLoaded && (scene.path == location || scene.name == location || scene.name == expectedName))
                {
                    return scene;
                }
            }

            throw new InvalidOperationException("Loaded scene was not found: " + location);
        }

        public async Task UnloadAsync(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            var operation = NativeSceneManager.UnloadSceneAsync(scene);
            if (operation == null)
            {
                throw new InvalidOperationException("Cannot unload scene: " + scene.name);
            }

            await WaitAsync(operation, null);
        }

        private static async Task WaitAsync(AsyncOperation operation, IProgress<float> progress)
        {
            while (!operation.isDone)
            {
                Report(progress, operation.progress);
                await Task.Yield();
            }

            Report(progress, 1);
        }

        internal static void Report(IProgress<float> progress, float value)
        {
            try
            {
                progress?.Report(value);
            }
            catch (Exception error)
            {
                // UI 的进度回调出错不能提前结束原生场景加载。
                Logger.Exception(error);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
using NativeSceneManager = UnityEngine.SceneManagement.SceneManager;

namespace Game.Framework.Scenes
{
    /// <summary>串行协调场景操作。取消调用方的等待不会撤销已经开始的原生加载。</summary>
    public sealed partial class SceneManager : Singleton<SceneManager>, IDisposable
    {
        // 重置管理器也不能让新请求越过旧实例仍在执行的原生加载。
        private static readonly SemaphoreSlim operations = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Dictionary<int, Scene> loaded = new Dictionary<int, Scene>();
        private ISceneLoader loader;
        private bool disposed;
        public int LoadedCount => loaded.Count;
        public bool Initialized => loader != null && !disposed;

        public event Action<Scene> Loaded;
        public event Action<Scene> Unloaded;
        public static void Reset(bool stopping = false)
        {
            ResetInstance(value => value.Dispose(), stopping);
        }

        public override void Initialize()
        {
            Initialize((ISceneLoader)null);
        }

        /// <summary>加载器交由管理器持有；实现 IDisposable 时随管理器一起释放。</summary>
        public void Initialize(ISceneLoader sceneLoader = null)
        {
            MainThread();
            if (loader != null)
            {
                if (sceneLoader != null && !ReferenceEquals(loader, sceneLoader))
                {
                    throw new InvalidOperationException("Scene loader is already configured.");
                }

                return;
            }

            loader = sceneLoader ?? new BuildSceneLoader();
            NativeSceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        public Task<Scene> LoadAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, bool setActive = true, IProgress<float> progress = null, CancellationToken token = default)
        {
            Ready();
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Scene location is required.", nameof(location));
            }

            if (mode != LoadSceneMode.Single && mode != LoadSceneMode.Additive)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            token.ThrowIfCancellationRequested();
            return WaitForLoadAsync(location, mode, setActive, progress, token);
        }

        private async Task<Scene> WaitForLoadAsync(string location, LoadSceneMode mode, bool setActive, IProgress<float> progress, CancellationToken token)
        {
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                return await TaskUtil.WaitAsync(LoadCoreAsync(location, mode, setActive, progress, cancellation.Token), cancellation.Token);
            }
        }

        private async Task<Scene> LoadCoreAsync(string location, LoadSceneMode mode, bool setActive, IProgress<float> progress, CancellationToken token)
        {
            await operations.WaitAsync(token);
            try
            {
                Ready();
                token.ThrowIfCancellationRequested();
                // 操作开始以后不再取消底层 Task，避免下一次加载与未完成的加载交错。
                var scene = await loader.LoadAsync(location, mode, progress);
                if (disposed)
                {
                    throw new OperationCanceledException("Scene manager was disposed during loading.");
                }

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    throw new InvalidOperationException("Scene loader returned an unloaded scene: " + location);
                }

                if (mode == LoadSceneMode.Single)
                {
                    loaded.Clear();
                }

                loaded[scene.handle] = scene;
                if (setActive && !NativeSceneManager.SetActiveScene(scene))
                {
                    throw new InvalidOperationException("Cannot activate scene: " + scene.name);
                }

                Notify(Loaded, scene);
                return scene;
            }
            finally
            {
                operations.Release();
            }
        }

        public Task UnloadAsync(Scene scene, CancellationToken token = default)
        {
            Ready();
            token.ThrowIfCancellationRequested();
            return WaitForUnloadAsync(scene, token);
        }

        private async Task WaitForUnloadAsync(Scene scene, CancellationToken token)
        {
            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token))
            {
                await TaskUtil.WaitAsync(UnloadCoreAsync(scene, cancellation.Token), cancellation.Token);
            }
        }

        private async Task UnloadCoreAsync(Scene scene, CancellationToken token)
        {
            await operations.WaitAsync(token);
            try
            {
                Ready();
                token.ThrowIfCancellationRequested();
                if (!loaded.ContainsKey(scene.handle))
                {
                    throw new InvalidOperationException("Scene is not owned by this manager.");
                }

                await loader.UnloadAsync(scene);
                if (!disposed)
                {
                    // Unity 的 sceneUnloaded 通常先到达；自定义加载器也可以只完成 Task。
                    OnSceneUnloaded(scene);
                }
            }
            finally
            {
                operations.Release();
            }
        }

        private void OnSceneUnloaded(Scene scene)
        {
            if (loaded.Remove(scene.handle))
            {
                Notify(Unloaded, scene);
            }
        }

        private static void Notify(Action<Scene> listeners, Scene scene)
        {
            if (listeners == null)
            {
                return;
            }

            foreach (Action<Scene> listener in listeners.GetInvocationList())
            {
                try
                {
                    listener(scene);
                }
                catch (Exception error)
                {
                    Logger.Exception(error);
                }
            }
        }

        private void MainThread()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(SceneManager));
            }

            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("SceneManager must run on Unity's initialized main thread.");
            }
        }

        private void Ready()
        {
            MainThread();
            if (loader == null)
            {
                throw new InvalidOperationException("Initialize SceneManager first.");
            }
        }

        /// <summary>停止接收请求和发送通知；不擅自卸载当前游戏场景，也不强行取消 Unity 的原生操作。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            MainThread();
            disposed = true;
            NativeSceneManager.sceneUnloaded -= OnSceneUnloaded;
            loaded.Clear();
            Loaded = null;
            Unloaded = null;
            try
            {
                lifetime.Cancel();
                (loader as IDisposable)?.Dispose();
            }
            finally
            {
                lifetime.Dispose();
                ReleaseInstance(this);
            }
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using FirebaseApp = global::Firebase.FirebaseApp;

namespace Game.Framework.Firebase.Unity
{
    /// <summary>Explicit dependency setup. Firebase App remains project-owned; disposing a wrapper never deletes it.</summary>
    /// <remarks>检查 Firebase 原生依赖，准备各产品适配器共用的默认 App。</remarks>
    public sealed class FirebaseManager
    {
        private Task<FirebaseApp> initialization;
        public bool IsInitialized => initialization != null && initialization.Status == TaskStatus.RanToCompletion;
        public FirebaseApp App => IsInitialized ? initialization.Result : throw new InvalidOperationException("Await Firebase initialization first.");

        /// <summary>复用进行中或已成功的初始化任务，失败或取消后允许下次调用重试。</summary>
        public Task<FirebaseApp> InitializeAsync(CancellationToken token = default)
        {
            MainThread();
            token.ThrowIfCancellationRequested();
            if (initialization == null || initialization.IsFaulted || initialization.IsCanceled)
            {
                initialization = InitializeCore();
            }
            return TaskUtil.WaitAsync(initialization, token);
        }

        private static async Task<FirebaseApp> InitializeCore()
        {
            var status = await FirebaseApp.CheckAndFixDependenciesAsync();
            // 依赖检查完成的线程不固定，取得默认 App 前先回到 Unity 主线程。
            await UnityThread.SwitchToMainThread();
            if (status != global::Firebase.DependencyStatus.Available)
            {
                throw new InvalidOperationException("Firebase native dependencies are unavailable: " + status);
            }
            return FirebaseApp.DefaultInstance;
        }

        public static void MainThread()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Use Firebase adapters on the initialized Unity main thread.");
            }
        }
    }
}

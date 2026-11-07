using System;
using System.Threading;
using System.Threading.Tasks;
using FirebaseApp = global::Firebase.FirebaseApp;

namespace Game.Framework.Firebase.Unity
{
    /// <summary>Explicit dependency setup. Firebase App remains project-owned; disposing a wrapper never deletes it.</summary>
    public class FirebaseRuntime
    {
        private Task<FirebaseApp> initialization;
        public bool IsInitialized => initialization != null && initialization.Status == TaskStatus.RanToCompletion;
        public FirebaseApp App => IsInitialized ? initialization.Result : throw new InvalidOperationException("Await Firebase initialization first.");

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

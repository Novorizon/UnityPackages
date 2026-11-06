using System;
using Game.Framework.Pooling;
using Game.Framework.Timing;
using UnityEngine;

namespace Game.Framework
{
    internal static class UnityFrameworkBootstrap
    {
        private sealed class UnityLogger : ILogger
        {
            public void Info(string message)
            {
                Debug.Log(message);
            }

            public void Warn(string message)
            {
                Debug.LogWarning(message);
            }

            public void Error(string message)
            {
                Debug.LogError(message);
            }

            public void Exception(Exception error)
            {
                Debug.LogException(error);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            ResetGameServices(false);
            FrameworkUpdateDriver.Stop();
            GameObjectPool.DisposeAll();
            TimerManager.Instance.ResetForPlay();
            ReferencePool.ClearAll();
            UnityThread.Reset();
            MonoSingletonLifecycle.Reset();
            Event.EventManager.Instance.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            UnityThread.Initialize();
            Logger.Sink = new UnityLogger();
            FrameworkUpdateDriver.Install();
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        private static void OnQuitting()
        {
            MonoSingletonLifecycle.Quit();
            FrameworkUpdateDriver.Stop();
            GameObjectPool.DisposeAll();
            TimerManager.Instance.ResetForPlay();
            ReferencePool.ClearAll();
            ResetGameServices(true);
        }

        private static void ResetGameServices(bool stopping)
        {
            try
            {
                Scenes.SceneManager.Reset(stopping);
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }

            try
            {
                Data.DataManager.Reset(stopping);
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }

            Storage.LocalStorageManager.Reset(stopping);
        }
    }
}

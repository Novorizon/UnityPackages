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
            SingletonLifecycle.Reset();
            FrameworkUpdateDriver.Stop();
            GameObjectPool.DisposeAll();
            ReferencePool.ClearAll();
            UnityThread.Reset();
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
            SingletonLifecycle.Shutdown();
            FrameworkUpdateDriver.Stop();
            GameObjectPool.DisposeAll();
            ReferencePool.ClearAll();
        }
    }
}

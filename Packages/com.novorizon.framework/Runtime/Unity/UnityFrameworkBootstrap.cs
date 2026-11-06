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
            SingletonRuntimeState.IsQuitting = false;
            System.Threading.Interlocked.Increment(ref SingletonRuntimeState.Epoch);
#if UNITY_EDITOR
            // Domain Reload 开启时由 CLR 初始化字段，避免重复执行声明中的初始化表达式。
            if (UnityEditor.EditorSettings.enterPlayModeOptionsEnabled &&
                (UnityEditor.EditorSettings.enterPlayModeOptions & UnityEditor.EnterPlayModeOptions.DisableDomainReload) != 0)
            {
                EditorStaticReset.RestoreDeclaredValues();
            }
#endif
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
            SingletonRuntimeState.IsQuitting = true;
            FrameworkUpdateDriver.Stop();
            GameObjectPool.DisposeAll();
            ReferencePool.ClearAll();
        }
    }
}

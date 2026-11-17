using System;
using Game.Framework.Pooling;
using Game.Framework.Timing;
using UnityEngine;

namespace Game.Framework
{
    /// <summary>提供Unity 框架启动入口的实现。</summary>
    internal static class UnityFrameworkBootstrap
    {
        /// <summary>表示Unity 框架启动入口中的Unity 日志接口。</summary>
        private sealed class UnityLogger : ILogger
        {
            /// <summary>调用 Debug.Log 完成信息。</summary>
            public void Info(string message)
            {
                Debug.Log(message);
            }

            /// <summary>调用 Debug.LogWarning 完成Warn。</summary>
            public void Warn(string message)
            {
                Debug.LogWarning(message);
            }

            /// <summary>调用 Debug.LogError 完成错误。</summary>
            public void Error(string message)
            {
                Debug.LogError(message);
            }

            /// <summary>调用 Debug.LogException 完成异常。</summary>
            public void Exception(Exception error)
            {
                Debug.LogException(error);
            }
        }

        /// <summary>恢复该类型约定的初始状态。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
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

        /// <summary>初始化 Unity 框架启动入口。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            UnityThread.Initialize();
            Logger.Sink = new UnityLogger();
            FrameworkUpdateDriver.Install();
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        /// <summary>响应正在退出。</summary>
        private static void OnQuitting()
        {
            FrameworkUpdateDriver.Stop();
            GameObjectPool.DisposeAll();
            ReferencePool.ClearAll();
        }
    }
}

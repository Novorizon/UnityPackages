#if UNITY_2022_3_OR_NEWER
using System;
using System.Threading;
using Game.Framework.Network.Dispatching;
using UnityEngine;

namespace Game.Framework.Network.Unity
{
    // Unity executes SynchronizationContext.Post callbacks in its main loop.
    // At most one drain is posted per dispatcher, even while the application is paused.
    /// <summary>提供Unity 网络调度器的实现。</summary>
    internal sealed class UnityNetworkDispatcher : IAutomaticNetworkDispatcher
    {
        private readonly QueuedNetworkDispatcher queue = new QueuedNetworkDispatcher();
        private readonly Action drain;
        private int scheduled, disposed;
        /// <summary>创建 UnityNetworkDispatcher 并采用默认配置。</summary>
        internal UnityNetworkDispatcher()
        {
            drain = Drain;
        }

        /// <summary>尝试处理 Post，返回是否成功。</summary>
        public bool TryPost(Action callback, int retainedPayloadBytes)
        {
            if (Volatile.Read(ref disposed) != 0 || !queue.TryPost(callback, retainedPayloadBytes))
            {
                return false;
            }

            Schedule();
            return true;
        }

        /// <summary>安排 Unity 网络调度器。</summary>
        private void Schedule()
        {
            if (Volatile.Read(ref disposed) != 0 || Interlocked.CompareExchange(ref scheduled, 1, 0) != 0)
            {
                return;
            }

            try
            {
                UnityThread.Post(drain);
            }
            catch
            {
                Volatile.Write(ref scheduled, 0);
                throw;
            }
        }

        /// <summary>取出并处理 Unity 网络调度器。</summary>
        private void Drain()
        {
            try
            {
                if (Volatile.Read(ref disposed) == 0)
                {
                    queue.Pump(64);
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
            finally
            {
                Volatile.Write(ref scheduled, 0);
                if (queue.PendingCount > 0)
                {
                    Schedule();
                }
            }
        }

        /// <summary>结束Unity 网络调度器对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                queue.Dispose();
            }
        }
    }

    /// <summary>提供Unity 网络 Integration的实现。</summary>
    internal static class UnityNetworkIntegration
    {
        /// <summary>恢复该类型约定的初始状态。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            NetworkDispatcherProvider.CreateAutomatic = null;
            NetworkDefaults.ConfigurationProvider = null;
        }

        /// <summary>加载配置。</summary>
        private static NetworkConfiguration LoadConfiguration()
        {
            if (!UnityThread.IsMainThread)
            {
                throw new InvalidOperationException("Create NetworkManager on the Unity main thread.");
            }

            var settings = Resources.Load<NetworkSettings>("NetworkSettings");
            if (settings == null)
            {
                throw new InvalidOperationException("Create a Resources/NetworkSettings asset before using the default NetworkManager constructor.");
            }

            return settings.CreateConfiguration();
        }

        /// <summary>初始化 Unity 网络 Integration。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            UnityThread.Initialize();
            NetworkDefaults.ConfigurationProvider = LoadConfiguration;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            NetworkDispatcherProvider.CreateAutomatic = () =>
            {
                if (Thread.CurrentThread.ManagedThreadId != mainThread)
                {
                    throw new InvalidOperationException("Create NetworkManager on the Unity main thread, or provide a custom dispatcher.");
                }

                return new UnityNetworkDispatcher();
            };
        }
    }
}
#endif

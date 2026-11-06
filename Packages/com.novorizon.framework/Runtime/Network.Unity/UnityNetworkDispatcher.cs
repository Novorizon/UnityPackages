#if UNITY_2022_3_OR_NEWER
using System;
using System.Threading;
using Game.Framework.Network.Dispatching;
using UnityEngine;

namespace Game.Framework.Network.Unity
{
    // Unity executes SynchronizationContext.Post callbacks in its main loop.
    // At most one drain is posted per dispatcher, even while the application is paused.
    internal sealed class UnityNetworkDispatcher : IAutomaticNetworkDispatcher
    {
        private readonly QueuedNetworkDispatcher queue = new QueuedNetworkDispatcher();
        private readonly Action drain;
        private int scheduled, disposed;
        internal UnityNetworkDispatcher()
        {
            drain = Drain;
        }

        public bool TryPost(Action callback, int retainedPayloadBytes)
        {
            if (Volatile.Read(ref disposed) != 0 || !queue.TryPost(callback, retainedPayloadBytes))
            {
                return false;
            }

            Schedule();
            return true;
        }

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

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
            {
                queue.Dispose();
            }
        }
    }

    internal static class UnityNetworkIntegration
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            NetworkDispatcherProvider.CreateAutomatic = null;
            NetworkDefaults.ConfigurationProvider = null;
        }

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

using System;
using Game.Framework;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace ECS
{
    /// <summary>Owns ECS startup without loading MVC or scanning user assemblies.</summary>
    public static class Ecs
    {
        static Ecs()
        {
            // World -> Entity -> Pool 的顺序仍由 ECS 自己维护。
            SingletonLifecycle.Register(OnSubsystemRegistration, OnSubsystemRegistration, order: -100);
        }

        internal static void EnsureLifecycleRegistration()
        {
        }

        public static bool IsQuitting
        {
            get
            {
                return SingletonLifecycle.IsQuitting || SingletonRuntimeState.IsQuitting;
            }
        }

        public static World Initialize()
        {
            if (IsQuitting)
            {
                throw new InvalidOperationException("ECS cannot initialize while the application is quitting.");
            }

            if (ReferencePool.Instance == null)
            {
                throw new InvalidOperationException("The ECS pool is shutting down.");
            }

            var manager = WorldManager.Instance;
            if (manager == null)
            {
                throw new InvalidOperationException("The ECS world manager is shutting down.");
            }

            return manager.DefaultWorld;
        }

        private static void OnSubsystemRegistration()
        {
            SingletonRuntimeState.IsQuitting = true;
            unchecked
            {
                SingletonRuntimeState.SessionEpoch++;
            }

            Exception firstError = null;
            try
            {
                // Drop system caches before EntityManager releases component/native leases.
                // Cleanup works through existing instances and never creates shutdown managers.
                try
                {
                    WorldManager.ResetForNewSession();
                }
                catch (Exception error)
                {
                    firstError = error;
                }

                try
                {
                    EntityManager.ResetForNewSession();
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }

                try
                {
                    ReferencePool.ResetForNewSession();
                }
                catch (Exception error)
                {
                    if (firstError == null)
                    {
                        firstError = error;
                    }
                }
            }
            finally
            {
                SingletonRuntimeState.IsQuitting = false;
            }

            // ComponentType IDs stay stable alongside generic system caches when domain reload is disabled.
            if (firstError != null)
            {
                ExceptionDispatchInfo.Capture(firstError).Throw();
            }
        }
    }
}

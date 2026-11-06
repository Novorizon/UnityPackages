using System;
using Game.Framework;
using UnityEngine;

namespace ECS
{
    internal static class SingletonRuntimeState
    {
        internal static bool IsQuitting;
        internal static int SessionEpoch = 0;
    }

    /// <summary>ECS initialization and update policy on Framework's Unity singleton implementation.</summary>
    public partial class SingletonBehaviour<T> : MonoSingleton<T> where T : SingletonBehaviour<T>
    {
        private static T lifetimeOwner;
        private static int sessionEpoch;
        public static bool applicationIsQuitting;
        private bool initialized;
        private bool initializing;
        private bool initializationAttempted;
        private bool deleted;
        private Exception initializationError;
        public new static T Instance
        {
            get
            {
                if (sessionEpoch != SingletonRuntimeState.SessionEpoch)
                {
                    ResetForNewSession();
                }

                if (applicationIsQuitting || SingletonRuntimeState.IsQuitting)
                {
                    return null;
                }

                return MonoSingleton<T>.Instance;
            }
        }

        public new static bool TryGetInstance(out T instance)
        {
            instance = null;
            return sessionEpoch == SingletonRuntimeState.SessionEpoch && MonoSingleton<T>.TryGetInstance(out instance);
        }

        protected override void Awake()
        {
            if (sessionEpoch != SingletonRuntimeState.SessionEpoch)
            {
                ResetForNewSession();
            }

            base.Awake();
        }

        protected override void OnStart()
        {
            lifetimeOwner = (T)this;
            EnsureInitialized();
        }

        protected override void OnDuplicateInstance(T existing)
        {
            throw new InvalidOperationException("Multiple ECS singletons: " + typeof(T).FullName);
        }

        private void EnsureInitialized()
        {
            if (IsDestroying)
            {
                throw new ObjectDisposedException(typeof(T).FullName);
            }

            if (initialized || initializing)
            {
                return;
            }

            if (initializationError != null)
            {
                throw new InvalidOperationException("ECS singleton initialization previously failed: " + typeof(T).FullName, initializationError);
            }

            initializing = true;
            initializationAttempted = true;
            try
            {
                OnInitialized();
                if (IsDestroying || deleted || applicationIsQuitting || SingletonRuntimeState.IsQuitting)
                {
                    throw new ObjectDisposedException(typeof(T).FullName, "The singleton stopped during initialization.");
                }

                initialized = true;
            }
            catch (Exception error)
            {
                initializationError = error;
                throw;
            }
            finally
            {
                initializing = false;
            }
        }

        protected override void OnApplicationQuit()
        {
            applicationIsQuitting = true;
            SingletonRuntimeState.IsQuitting = true;
            base.OnApplicationQuit();
        }

        protected override void OnExit()
        {
            try
            {
                DeleteOnce();
            }
            finally
            {
                if (IsDestroying && ReferenceEquals(lifetimeOwner, this))
                {
                    lifetimeOwner = null;
                }
            }
        }

        private void DeleteOnce()
        {
            if (deleted || !initializationAttempted)
            {
                return;
            }

            deleted = true;
            OnDelete();
        }

        // Ordered cleanup belongs to ECS and is independent of Framework subsystem-hook ordering.
        internal static void ResetForNewSession()
        {
            sessionEpoch = SingletonRuntimeState.SessionEpoch;
            var old = lifetimeOwner;
            applicationIsQuitting = true;
            bool cleanupSucceeded = false;
            try
            {
                if (old != null)
                {
                    old.DeleteOnce();
                }

                cleanupSucceeded = true;
            }
            finally
            {
                if (old != null)
                {
                    old.initialized = false;
                    old.initializing = false;
                    old.initializationAttempted = false;
                    old.initializationError = null;
                    if (cleanupSucceeded)
                    {
                        old.deleted = false;
                    }
                    else
                    {
                        old.RetireSingleton();
                    }
                }

                lifetimeOwner = null;
                ResetSingletonReference();
                applicationIsQuitting = false;
            }
        }

        public enum UpdateMode
        {
            FIXED_UPDATE,
            UPDATE,
            LATE_UPDATE
        }

        public UpdateMode updateMode = UpdateMode.UPDATE;
        private void Update()
        {
            if (initialized && !IsDestroying && updateMode == UpdateMode.UPDATE)
            {
                OnUpdate(Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (initialized && !IsDestroying && updateMode == UpdateMode.FIXED_UPDATE)
            {
                OnUpdate(Time.fixedDeltaTime);
            }
        }

        private void LateUpdate()
        {
            if (initialized && !IsDestroying && updateMode == UpdateMode.LATE_UPDATE)
            {
                OnUpdate(Time.deltaTime);
            }
        }

        protected virtual void OnInitialized()
        {
        }

        protected virtual void OnDelete()
        {
        }

        protected virtual void OnUpdate(float delta)
        {
        }
    }
}

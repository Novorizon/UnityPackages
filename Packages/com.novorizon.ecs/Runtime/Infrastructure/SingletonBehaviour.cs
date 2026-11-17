using System;
using Game.Framework;
using UnityEngine;

namespace ECS
{
    /// <summary>单例运行时对应的状态。保存状态或定义允许的状态值。</summary>
    internal static class SingletonRuntimeState
    {
        internal static bool IsQuitting;
        internal static int SessionEpoch = 0;
    }

    /// <summary>ECS initialization and update policy on Framework's Unity singleton implementation.</summary>
    /// <remarks>提供单例 Behaviour的实现。</remarks>
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

        /// <summary>尝试取得实例，返回是否成功。</summary>
        public new static bool TryGetInstance(out T instance)
        {
            instance = null;
            return sessionEpoch == SingletonRuntimeState.SessionEpoch && MonoSingleton<T>.TryGetInstance(out instance);
        }

        /// <summary>Unity 创建组件时执行本组件的初始化逻辑。</summary>
        protected override void Awake()
        {
            if (sessionEpoch != SingletonRuntimeState.SessionEpoch)
            {
                ResetForNewSession();
            }

            base.Awake();
        }

        /// <summary>响应 Start。</summary>
        protected override void OnStart()
        {
            lifetimeOwner = (T)this;
            EnsureInitialized();
        }

        /// <summary>响应重复项实例。</summary>
        protected override void OnDuplicateInstance(T existing)
        {
            throw new InvalidOperationException("Multiple ECS singletons: " + typeof(T).FullName);
        }

        /// <summary>检查并确保已初始化可用。</summary>
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

        /// <summary>响应 Unity 应用退出通知。</summary>
        protected override void OnApplicationQuit()
        {
            applicationIsQuitting = true;
            SingletonRuntimeState.IsQuitting = true;
            base.OnApplicationQuit();
        }

        /// <summary>响应退出。</summary>
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

        /// <summary>删除 Once。</summary>
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
        /// <summary>重置 For 新会话。</summary>
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

        /// <summary>定义更新模式可选的枚举值。</summary>
        public enum UpdateMode
        {
            FIXED_UPDATE,
            UPDATE,
            LATE_UPDATE
        }

        public UpdateMode updateMode = UpdateMode.UPDATE;
        /// <summary>推进该对象的一次更新。</summary>
        private void Update()
        {
            if (initialized && !IsDestroying && updateMode == UpdateMode.UPDATE)
            {
                OnUpdate(Time.deltaTime);
            }
        }

        /// <summary>通过 Unity 的固定时间步推进本组件逻辑。</summary>
        private void FixedUpdate()
        {
            if (initialized && !IsDestroying && updateMode == UpdateMode.FIXED_UPDATE)
            {
                OnUpdate(Time.fixedDeltaTime);
            }
        }

        /// <summary>在常规帧更新以后执行本组件的处理。</summary>
        private void LateUpdate()
        {
            if (initialized && !IsDestroying && updateMode == UpdateMode.LATE_UPDATE)
            {
                OnUpdate(Time.deltaTime);
            }
        }

        /// <summary>响应已初始化。</summary>
        protected virtual void OnInitialized()
        {
        }

        /// <summary>响应 Delete。</summary>
        protected virtual void OnDelete()
        {
        }

        /// <summary>响应更新。</summary>
        protected virtual void OnUpdate(float delta)
        {
        }
    }
}

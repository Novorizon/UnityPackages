#nullable disable
using System;
using UnityEngine;

namespace Game.Framework
{
    /// <summary>为 Unity 组件提供共享入口，组件自己的启动和退出逻辑在 OnStart、OnExit 中实现。</summary>
    public abstract class MonoSingleton<T> : MonoBehaviour, IDisposable where T : MonoSingleton<T>
    {
        /// <summary>统一的无参初始化入口。实例由 Instance 准备；有初始化工作的服务重写本方法。</summary>
        public virtual void Initialize()
        {
        }

        private static T instance;
        private static bool ending;
        private bool retired;
        private bool lifetimeStarted;
        private bool lifetimeEnded;
        /// <summary>为 MonoSingleton 准备类型首次使用时所需的静态状态。</summary>
        static MonoSingleton()
        {
#if UNITY_EDITOR
            EditorStaticReset.Register(ResetForPlay);
#endif
        }

        /// <summary>结束现有生命周期。</summary>
        private static void EndExistingLifetime()
        {
            if (instance != null)
            {
                instance.EndLifetime();
            }
        }

        // 关闭 Domain Reload 后，结束上一轮的运行状态并清掉静态引用，保留场景组件可供下次使用。
        private static void ResetForPlay()
        {
            try
            {
                EndExistingLifetime();
            }
            finally
            {
                ResetSingletonReference();
                ending = false;
            }
        }

        public virtual string RootName => typeof(T).Name;
        protected bool IsDestroying => retired;

        public static T Instance
        {
            get
            {
                if (ending)
                {
                    return null;
                }

                if (!UnityThread.IsMainThread)
                {
                    throw new InvalidOperationException("MonoSingleton instances must be accessed on the initialized Unity main thread.");
                }

                if (TryGetInstance(out var current))
                {
                    return Prepare(current);
                }

                T found = null;
                foreach (var candidate in FindObjectsOfType<T>(true))
                {
                    if (candidate.retired)
                    {
                        continue;
                    }

                    if (found == null)
                    {
                        found = candidate;
                    }
                    else
                    {
                        candidate.RejectDuplicate(found);
                    }
                }

                if (found != null)
                {
                    instance = found;
                    return Prepare(found);
                }

                var go = new GameObject(typeof(T).Name);
                try
                {
                    var created = go.AddComponent<T>();
                    if (created.retired)
                    {
                        throw new ObjectDisposedException(typeof(T).FullName);
                    }

                    instance = created;
                    return Prepare(created);
                }
                catch
                {
                    Destroy(go);
                    throw;
                }
            }
        }

        /// <summary>Checks the published reference without creating or initializing a component.</summary>
        /// <remarks>尝试取得实例，返回是否成功。</remarks>
        public static bool TryGetInstance(out T value)
        {
            value = instance;
            return value != null && !value.retired;
        }

        /// <summary>Retires the reference immediately, including while native Destroy is deferred.</summary>
        /// <remarks>销毁实例。</remarks>
        public static void DestroyInstance()
        {
            if (!TryGetInstance(out var current))
            {
                return;
            }

            current.Dispose();
        }

        /// <summary>释放已有实例，不通过 Instance 创建或初始化组件。</summary>
        public static void Release() => DestroyInstance();
        /// <summary>结束Mono 单例对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            if (retired)
            {
                return;
            }

            // 先撤下当前组件的共享引用，因为 Unity 的 Destroy 可能要到帧末才生效。
            RetireSingleton();
            try
            {
                EndLifetime();
            }
            finally
            {
                Destroy(gameObject);
            }
        }

        /// <summary>准备 Mono 单例。</summary>
        private static T Prepare(T value)
        {
            try
            {
                if (!value.lifetimeStarted)
                {
                    value.lifetimeStarted = true;
                    value.OnStart();
                }

                // OnStart 的用户代码可以销毁自己，启动返回前要再次确认实例仍可用。
                if (value.retired)
                {
                    throw new ObjectDisposedException(typeof(T).FullName);
                }

                if (value.transform.parent == null)
                {
                    DontDestroyOnLoad(value.gameObject);
                }

                return value;
            }
            catch
            {
                value.Dispose();
                throw;
            }
        }

        /// <summary>拒绝重复项。</summary>
        private void RejectDuplicate(T existing)
        {
            RetireSingleton();
            OnDuplicateInstance(existing);
        }

        // Domain-specific initialization and cleanup remain in the component using this base.
        /// <summary>响应 Start。</summary>
        protected virtual void OnStart()
        {
        }

        /// <summary>与 OnStart 对应的清理钩子；同次运行只调用一次。</summary>
        protected virtual void OnExit()
        {
        }

        /// <summary>结束生命周期。</summary>
        protected void EndLifetime()
        {
            if (!lifetimeStarted || lifetimeEnded)
            {
                return;
            }

            lifetimeEnded = true;
            ending = true;
            try
            {
                OnExit();
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }
            finally
            {
                ending = false;
            }
        }

        /// <summary>响应重复项实例。</summary>
        protected virtual void OnDuplicateInstance(T existing)
        {
            Destroy(gameObject);
        }

        /// <summary>重置单例引用。</summary>
        protected static void ResetSingletonReference()
        {
            if (instance != null)
            {
                instance.lifetimeStarted = false;
                instance.lifetimeEnded = false;
            }

            instance = null;
        }

        /// <summary>终止使用单例。</summary>
        protected void RetireSingleton()
        {
            retired = true;
            if (ReferenceEquals(instance, this))
            {
                instance = null;
            }
        }

        /// <summary>Unity 创建组件时执行本组件的初始化逻辑。</summary>
        protected virtual void Awake()
        {
            if (retired)
            {
                RetireSingleton();
                Destroy(gameObject);
                return;
            }

            if (instance != null && instance != this)
            {
                RejectDuplicate(instance);
                return;
            }

            instance = (T)this;
            Prepare((T)this);
            if (transform.parent != null)
            {
                Logger.Warn("MonoSingleton should be placed on a root GameObject: " + typeof(T).Name);
            }
        }

        /// <summary>响应 Unity 应用退出通知。</summary>
        protected virtual void OnApplicationQuit()
        {
            EndLifetime();
        }

        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        protected virtual void OnDestroy()
        {
            RetireSingleton();
            EndLifetime();
        }
    }
}

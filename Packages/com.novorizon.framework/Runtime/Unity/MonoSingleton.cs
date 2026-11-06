#nullable disable
using System;
using UnityEngine;

namespace Game.Framework
{
    internal static class MonoSingletonLifecycle
    {
        internal static bool IsQuitting => SingletonLifecycle.IsQuitting;
        internal static int Epoch => SingletonLifecycle.Epoch;

        internal static void Reset() => SingletonLifecycle.Reset();
        internal static void Quit() => SingletonLifecycle.MarkQuitting();
    }

    public abstract class MonoSingleton<T> : MonoBehaviour, IDisposable where T : MonoSingleton<T>
    {
#if UNITY_EDITOR
        // 供生成的派生类代码调用；MonoSingleton 的使用方无需为此增加 Core 程序集引用。
        protected static bool RegisterEditorStaticReset(Action clear, Action restore)
        {
            return EditorStaticReset.Register(clear, restore);
        }
#endif
        private static T instance;
        private static bool ending;
        private bool retired;
        private bool lifetimeStarted;
        private bool lifetimeEnded;
        private int lifetimeEpoch = -1;
        static MonoSingleton()
        {
            SingletonLifecycle.Register(ResetForPlay, EndExistingLifetime);
        }

        private static void EndExistingLifetime()
        {
            if (instance != null)
            {
                instance.EndLifetime();
            }
        }

        private static void ResetForPlay()
        {
            try
            {
                EndExistingLifetime();
            }
            finally
            {
                instance = null;
                ending = false;
            }
        }

        public virtual string RootName => typeof(T).Name;
        protected bool IsDestroying => retired;

        public static T Instance
        {
            get
            {
                if (MonoSingletonLifecycle.IsQuitting || ending)
                {
                    return null;
                }

                if (!UnityThread.IsMainThread)
                {
                    throw new InvalidOperationException("MonoSingleton instances must be accessed on the initialized Unity main thread.");
                }

                if (TryGetInstance(out var current))
                {
                    return SingletonLifecycle.IsResetting ? current : Prepare(current);
                }

                if (SingletonLifecycle.IsResetting)
                {
                    throw new InvalidOperationException("Cannot create or initialize a MonoSingleton during reset: " + typeof(T).FullName);
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
        public static bool TryGetInstance(out T value)
        {
            value = instance;
            return value != null && !value.retired;
        }

        /// <summary>Retires the reference immediately, including while native Destroy is deferred.</summary>
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
        public void Dispose()
        {
            if (retired)
            {
                return;
            }

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

        private static T Prepare(T value)
        {
            try
            {
                if (value.lifetimeEpoch != MonoSingletonLifecycle.Epoch)
                {
                    value.lifetimeEpoch = MonoSingletonLifecycle.Epoch;
                    value.lifetimeStarted = false;
                    value.lifetimeEnded = false;
                }

                if (!value.lifetimeStarted)
                {
                    value.lifetimeStarted = true;
                    value.OnStart();
                }

                if (value.retired || MonoSingletonLifecycle.IsQuitting)
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

        private void RejectDuplicate(T existing)
        {
            RetireSingleton();
            OnDuplicateInstance(existing);
        }

        // Domain-specific initialization and cleanup remain in the component using this base.
        protected virtual void OnStart()
        {
        }

        /// <summary>与 OnStart 对应的清理钩子；同次运行只调用一次。</summary>
        protected virtual void OnExit()
        {
        }

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

        protected virtual void OnDuplicateInstance(T existing)
        {
            Destroy(gameObject);
        }

        protected static void ResetSingletonReference()
        {
            if (instance != null)
            {
                instance.lifetimeStarted = false;
                instance.lifetimeEnded = false;
            }

            instance = null;
        }

        protected void RetireSingleton()
        {
            retired = true;
            if (ReferenceEquals(instance, this))
            {
                instance = null;
            }
        }

        protected virtual void Awake()
        {
            if (retired || MonoSingletonLifecycle.IsQuitting)
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

        protected virtual void OnApplicationQuit()
        {
            MonoSingletonLifecycle.Quit();
            EndLifetime();
        }

        protected virtual void OnDestroy()
        {
            RetireSingleton();
            EndLifetime();
        }
    }
}

///------------------------------------
/// Author：Novorizon
/// Mail：novorizon@hotmail.com
/// Date：2022-10-11
/// Description：单例
///------------------------------------
using System;
using System.Threading;

namespace Game.Framework
{
    // 单例访问保护所需的内部状态；不保存实例、清理回调或释放顺序。
    internal static class SingletonRuntimeState
    {
        internal static volatile bool IsQuitting;
        internal static int Epoch;
        internal static bool IsResetting
        {
            get
            {
#if UNITY_EDITOR
                return EditorStaticReset.IsResetting;
#else
                return false;
#endif
            }
        }
    }

    /// <summary>普通服务的共享引用；可由应用入口注册，也兼容原有的按需创建方式。</summary>
    public abstract class Singleton<T>
        where T : new()
    {
#if UNITY_EDITOR
        // 供生成的派生类代码调用；MonoSingleton 的使用方无需为此增加 Core 程序集引用。
        protected static bool RegisterEditorStaticReset(Action clear, Action restore)
        {
            return EditorStaticReset.Register(clear, restore);
        }
#endif
        /// <summary>统一的无参初始化入口；有初始化工作的服务重写本方法。</summary>
        public virtual void Initialize()
        {
        }

        private static readonly object gate = new object ();
        private static Lazy<T> lazy = CreateLazy();
        private static bool resetting;
        private static bool creating;
        private static bool entryOwned;
        private static T entryInstance;
#if UNITY_EDITOR
        static Singleton()
        {
            EditorStaticReset.Register(ReleaseCurrent);
        }
#endif

        private static Lazy<T> CreateLazy()
        {
            return new Lazy<T>(() =>
            {
                creating = true;
                try
                {
                    return new T();
                }
                finally
                {
                    creating = false;
                }
            }, LazyThreadSafetyMode.ExecutionAndPublication);
        }
        public static T Instance
        {
            get
            {
                lock (gate)
                {
                    if (entryOwned)
                    {
                        if (ReferenceEquals(entryInstance, null))
                        {
                            throw new InvalidOperationException("The application entry has not registered " + typeof(T).FullName);
                        }

                        return entryInstance;
                    }

                    EnsureAvailable();
                    return lazy.Value;
                }
            }
        }

        public static bool IsCreated
        {
            get
            {
                lock (gate)
                {
                    return entryOwned ? !ReferenceEquals(entryInstance, null) : lazy.IsValueCreated;
                }
            }
        }

        public static bool TryGetInstance(out T value)
        {
            lock (gate)
            {
                if (entryOwned)
                {
                    value = entryInstance;
                    return !ReferenceEquals(value, null);
                }

                value = lazy.IsValueCreated ? lazy.Value : default;
                return lazy.IsValueCreated;
            }
        }

        /// <summary>声明此服务由应用入口管理；必须在第一次访问前调用。</summary>
        public static void UseEntryLifetime()
        {
            lock (gate)
            {
                if (entryOwned)
                {
                    return;
                }

                if (creating || resetting || lazy.IsValueCreated)
                {
                    throw new InvalidOperationException("Select entry ownership before accessing " + typeof(T).FullName);
                }

                entryOwned = true;
            }
        }

        internal static void RegisterEntryInstance(T value)
        {
            if (ReferenceEquals(value, null))
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (gate)
            {
                if (!entryOwned || !ReferenceEquals(entryInstance, null) || SingletonRuntimeState.IsQuitting || SingletonRuntimeState.IsResetting)
                {
                    throw new InvalidOperationException("Cannot register another entry instance of " + typeof(T).FullName);
                }

                entryInstance = value;
            }
        }

        internal static void UnregisterEntryInstance(T value)
        {
            lock (gate)
            {
                if (entryOwned && ReferenceEquals(entryInstance, value))
                {
                    entryInstance = default;
                }
            }
        }

        protected static void ClearInstanceIfCurrent(T value)
        {
            lock (gate)
            {
                if (lazy.IsValueCreated && ReferenceEquals(lazy.Value, value))
                {
                    lazy = CreateLazy();
                }
            }
        }

        private static void ReleaseCurrent()
        {
            Lazy<T> previous;
            lock (gate)
            {
                if (entryOwned)
                {
                    return;
                }

                if (creating || resetting)
                {
                    throw new InvalidOperationException("Cannot reset a service during creation or reset: " + typeof(T).FullName);
                }

                previous = lazy;
                lazy = CreateLazy();
                resetting = true;
            }

            try
            {
                if (previous.IsValueCreated && previous.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            finally
            {
                lock (gate)
                {
                    resetting = false;
                }
            }
        }

        /// <summary>释放已有实例并清空引用，不创建尚未使用的单例。</summary>
        public static void Release()
        {
            try
            {
                ReleaseCurrent();
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }
        }

        private static void EnsureAvailable()
        {
            if (creating || resetting
                || (SingletonRuntimeState.IsQuitting && !SingletonRuntimeState.IsResetting)
                || (!lazy.IsValueCreated && SingletonRuntimeState.IsResetting))
            {
                throw new InvalidOperationException("Cannot create or access a singleton during creation, reset or shutdown: " + typeof(T).FullName);
            }
        }
    }
}

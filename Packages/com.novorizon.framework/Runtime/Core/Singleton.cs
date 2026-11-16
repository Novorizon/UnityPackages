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

    /// <summary>按需创建的普通单例；运行时由所属入口集中 Release。</summary>
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
        /// <summary>统一的无参初始化入口。实例由 Instance 准备；有初始化工作的服务重写本方法。</summary>
        public virtual void Initialize()
        {
        }

        private static readonly object gate = new object ();
        private static Lazy<T> lazy = CreateLazy();
        private static bool resetting;
        private static bool creating;
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
                    return lazy.IsValueCreated;
                }
            }
        }

        public static bool TryGetInstance(out T value)
        {
            lock (gate)
            {
                value = lazy.IsValueCreated ? lazy.Value : default;
                return lazy.IsValueCreated;
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

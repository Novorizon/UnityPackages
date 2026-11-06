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
    /// <summary>按需创建的普通单例，自动登记新运行时的引用重置和已有实例的退出清理。</summary>
    public abstract class Singleton<T>
        where T : new()
    {
        private static readonly object gate = new object ();
        private static Lazy<T> lazy = CreateLazy();
        private static bool resetting;
        static Singleton()
        {
            SingletonLifecycle.Register(ResetSingleton, ShutdownSingleton);
        }

        private static Lazy<T> CreateLazy() => new Lazy<T>(() => new T(), LazyThreadSafetyMode.ExecutionAndPublication);
        public static T Instance
        {
            get
            {
                lock (gate)
                {
                    if (resetting || (SingletonLifecycle.IsQuitting && !SingletonLifecycle.IsResetting) || (!lazy.IsValueCreated && (SingletonLifecycle.IsResetting || SingletonLifecycle.IsQuitting)))
                    {
                        throw new InvalidOperationException("Cannot create a singleton during reset or shutdown: " + typeof(T).FullName);
                    }

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

        private static void ResetSingleton()
        {
            Lazy<T> previous;
            lock (gate)
            {
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

        private static void ShutdownSingleton()
        {
            if (TryGetInstance(out var value) && value is IDisposable)
            {
                ResetSingleton();
            }
        }
    }
}

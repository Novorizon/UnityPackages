using System;

namespace Game.Framework
{
    /// <summary>通过工厂创建的共享服务，支持显式重置和退出时禁止重新创建。</summary>
    public abstract class ConfigurableSingleton<T>
        where T : class
    {
        private static readonly object gate = new object ();
        private static T instance;
        private static bool creating;
        private static bool resetting;
        private static bool stopped;
        public static bool IsCreated
        {
            get
            {
                lock (gate)
                {
                    return instance != null;
                }
            }
        }

        public static bool TryGetInstance(out T value)
        {
            lock (gate)
            {
                value = instance;
                return value != null;
            }
        }

        /// <summary>释放已有实例，不创建新实例；清理错误在框架内部记录。</summary>
        public static void Release()
        {
            try
            {
                ResetCore(value =>
                {
                    if (value is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }, null);
            }
            catch (Exception error)
            {
                Logger.Exception(error);
            }
        }

        protected static T GetOrCreateInstance(Func<T> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            lock (gate)
            {
                EnsureAvailable();
                return instance ?? Create(factory);
            }
        }

        protected static void ConfigureInstance(Func<T> factory)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            lock (gate)
            {
                EnsureAvailable();
                if (instance != null)
                {
                    throw new InvalidOperationException("Configure the service before accessing its instance: " + typeof(T).FullName);
                }

                Create(factory);
            }
        }

        private static T Create(Func<T> factory)
        {
            if (creating)
            {
                throw new InvalidOperationException("Reentrant singleton creation: " + typeof(T).FullName);
            }

            creating = true;
            try
            {
                instance = factory() ?? throw new InvalidOperationException("The singleton factory returned null: " + typeof(T).FullName);
                return instance;
            }
            finally
            {
                creating = false;
            }
        }

        protected static void ResetInstance(Action<T> release, bool stopping = false)
        {
            ResetCore(release, stopping);
        }

        private static void ResetCore(Action<T> release, bool? stopping)
        {
            T previous;
            lock (gate)
            {
                if (creating || resetting)
                {
                    throw new InvalidOperationException("Cannot reset a service during creation or reset: " + typeof(T).FullName);
                }

                previous = instance;
                instance = null;
                if (stopping.HasValue)
                {
                    stopped = stopping.Value;
                }

                resetting = true;
            }

            // 清理在锁外执行；期间不允许创建服务，避免与旧实例的清理交错。
            try
            {
                if (previous != null)
                {
                    release?.Invoke(previous);
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

        protected static void ReleaseInstance(T value)
        {
            lock (gate)
            {
                if (ReferenceEquals(instance, value))
                {
                    instance = null;
                }
            }
        }

        private static void EnsureAvailable()
        {
            if (stopped || resetting)
            {
                throw new InvalidOperationException("The service is stopping or resetting: " + typeof(T).FullName);
            }
        }
    }
}

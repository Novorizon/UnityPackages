///------------------------------------
/// Author：Novorizon
/// Mail：novorizon@hotmail.com
/// Date：2022-10-11
/// Description：单例
///------------------------------------
using System;

namespace Game.Framework
{
    /// <summary>按需创建普通单例，实际资源由服务自己清理。</summary>
    public abstract class Singleton<T> : IDisposable where T : Singleton<T>, new()
    {
        private static readonly object gate = new object();
        private static Lazy<T> lazy = CreateLazy();

#if UNITY_EDITOR
        static Singleton()
        {
            EditorStaticReset.Register(() =>
            {
                T previous;
                lock (gate)
                {
                    previous = lazy.IsValueCreated ? lazy.Value : null;
                    lazy = CreateLazy();
                }

                previous?.Dispose();
            });
        }
#endif

        private static Lazy<T> CreateLazy()
        {
            return new Lazy<T>(() => new T());
        }

        public static T Instance
        {
            get
            {
                lock (gate)
                {
                    return lazy.Value;
                }
            }
        }

        public static bool TryGetInstance(out T value)
        {
            lock (gate)
            {
                value = lazy.IsValueCreated ? lazy.Value : null;
                return value != null;
            }
        }

        public virtual void Initialize()
        {
        }

        public virtual void Release()
        {
        }

        public virtual void Dispose()
        {
        }
    }
}

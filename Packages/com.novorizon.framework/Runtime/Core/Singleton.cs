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
        /// <summary>为 Singleton 准备类型首次使用时所需的静态状态。</summary>
        static Singleton()
        {
            EditorStaticReset.Register(() =>
            {
                T previous;
                lock (gate)
                {
                    previous = lazy.IsValueCreated ? lazy.Value : null;
                    // 先替换缓存，再在锁外清理上一轮 Play 留下的实例。
                    lazy = CreateLazy();
                }

                previous?.Dispose();
            });
        }
#endif

        /// <summary>创建 Lazy。</summary>
        private static Lazy<T> CreateLazy()
        {
            return new Lazy<T>(() => new T());
        }

        /// <summary>读取当前类型的缓存实例，首次读取时按需创建。</summary>
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

        /// <summary>只取得已经创建的实例，检查过程不会创建对象。</summary>
        public static bool TryGetInstance(out T value)
        {
            lock (gate)
            {
                value = lazy.IsValueCreated ? lazy.Value : null;
                return value != null;
            }
        }

        /// <summary>初始化单例。</summary>
        public virtual void Initialize()
        {
        }

        /// <summary>给派生类预留释放钩子，基类默认不执行操作。</summary>
        public virtual void Release()
        {
        }

        /// <summary>结束单例对相关资源的持有，并执行该类型的清理流程。</summary>
        public virtual void Dispose()
        {
        }
    }
}

///------------------------------------
/// Author：Novorizon
/// Mail：novorizon@hotmail.com
/// Date：2022-10-11
/// Description：单例
///------------------------------------
using System;

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

        // 子类的静态字段由 Source Generator 登记。
        protected static bool RegisterEditorStaticReset(Action clear, Action restore)
        {
            return EditorStaticReset.Register(clear, restore);
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
                    bool editorReset = SingletonRuntimeState.IsResetting;
                    if (editorReset ? !lazy.IsValueCreated : SingletonRuntimeState.IsQuitting)
                    {
                        throw new InvalidOperationException("Cannot access the singleton during reset or shutdown: " + typeof(T).FullName);
                    }

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

        /// <summary>清空当前实例的共享引用。</summary>
        public virtual void Release()
        {
            lock (gate)
            {
                if (lazy.IsValueCreated && ReferenceEquals(lazy.Value, this))
                {
                    lazy = CreateLazy();
                }
            }
        }

        public virtual void Dispose()
        {
        }
    }
}

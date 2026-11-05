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
    public abstract class Singleton<T> where T : new()
    {
        private static readonly Lazy<T> lazy = new Lazy<T>(() => new T(), LazyThreadSafetyMode.ExecutionAndPublication);

        public static T Instance => lazy.Value;

        public static bool IsCreated => lazy.IsValueCreated;
    }
}

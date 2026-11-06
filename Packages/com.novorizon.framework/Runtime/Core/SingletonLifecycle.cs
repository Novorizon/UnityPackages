using System;
using System.Collections.Generic;
using System.Threading;

namespace Game.Framework
{
    /// <summary>登记类型级清理回调；Unity 启动入口统一调用，不扫描或改写子类静态字段。</summary>
    public static class SingletonLifecycle
    {
        private sealed class Entry
        {
            internal Action Reset;
            internal Action Shutdown;
            internal int Order;
            internal int Sequence;
        }

        private static readonly object gate = new object ();
        private static readonly List<Entry> entries = new List<Entry>();
        private static int transitioning;
        private static int quitting;
        private static int epoch;
        private static bool shutdownCompleted;
        public static bool IsResetting => Volatile.Read(ref transitioning) != 0;
        public static bool IsQuitting => Volatile.Read(ref quitting) != 0;
        public static int Epoch => Volatile.Read(ref epoch);

        /// <summary>类型静态构造中登记一次。order 较小者先执行，依赖清理由所属模块协调。</summary>
        public static void Register(Action reset, Action shutdown = null, int order = 0)
        {
            if (reset == null)
            {
                throw new ArgumentNullException(nameof(reset));
            }

            lock (gate)
            {
                foreach (var entry in entries)
                {
                    if (entry.Reset == reset && entry.Shutdown == shutdown)
                    {
                        return;
                    }
                }

                entries.Add(new Entry { Reset = reset, Shutdown = shutdown, Order = order, Sequence = entries.Count });
            }
        }

        /// <summary>开始新一次运行；保留登记表，因为关闭域重载时静态构造不会再次执行。</summary>
        public static void Reset()
        {
            Run(false);
        }

        /// <summary>退出时清理已使用的实例，不创建新实例；重复调用安全。</summary>
        public static void Shutdown()
        {
            Run(true);
        }

        public static void MarkQuitting()
        {
            Volatile.Write(ref quitting, 1);
        }

        private static void Run(bool stopping)
        {
            Entry[] snapshot;
            lock (gate)
            {
                if (transitioning != 0 || (stopping && shutdownCompleted))
                {
                    return;
                }

                Volatile.Write(ref transitioning, 1);
                if (stopping)
                {
                    MarkQuitting();
                }

                if (!stopping)
                {
                    Volatile.Write(ref quitting, 0);
                    Interlocked.Increment(ref epoch);
                    shutdownCompleted = false;
                }

                snapshot = entries.ToArray();
            }

            Array.Sort(snapshot, (a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Sequence.CompareTo(b.Sequence));
            try
            {
                foreach (var entry in snapshot)
                {
                    try
                    {
                        if (stopping)
                        {
                            entry.Shutdown?.Invoke();
                        }
                        else
                        {
                            entry.Reset();
                        }
                    }
                    catch (Exception error)
                    {
                        try
                        {
                            Logger.Exception(error);
                        }
                        catch
                        {
                        // 日志接收方的异常不能阻止其余单例释放。
                        }
                    }
                }
            }
            finally
            {
                lock (gate)
                {
                    shutdownCompleted = stopping;
                    Volatile.Write(ref transitioning, 0);
                }
            }
        }
    }
}

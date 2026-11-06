#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace Game.Framework
{
    /// <summary>生成代码登记入口，仅编辑器存在；登记表跨越关闭 Domain Reload 的多次 Play。</summary>
    public static class EditorStaticReset
    {
        private sealed class Entry
        {
            internal Action Clear;
            internal Action Restore;
        }

        private static readonly List<Entry> entries = new List<Entry>();
        private static readonly object gate = new object();
        private static volatile bool resetting;
        public static bool IsResetting => resetting;

        /// <summary>供单例基类和生成器使用；业务不需要手动登记。</summary>
        public static bool Register(Action clear, Action restore = null)
        {
            if (clear == null)
            {
                throw new ArgumentNullException(nameof(clear));
            }

            lock (gate)
            {
                foreach (var entry in entries)
                {
                    if (entry.Clear == clear && entry.Restore == restore)
                    {
                        return true;
                    }
                }

                entries.Add(new Entry { Clear = clear, Restore = restore });
                return true;
            }
        }

        /// <summary>先全部恢复 C# 默认值，再按字段声明恢复显式初始值；不会清空登记表。</summary>
        public static void RestoreDeclaredValues()
        {
            Entry[] snapshot;
            lock (gate)
            {
                if (resetting)
                {
                    throw new InvalidOperationException("Static field restoration cannot be reentered.");
                }

                resetting = true;
                snapshot = entries.ToArray();
            }

            var errors = new List<Exception>();
            try
            {
                foreach (var entry in snapshot)
                {
                    Invoke(entry.Clear, errors);
                }

                foreach (var entry in snapshot)
                {
                    Invoke(entry.Restore, errors);
                }
            }
            finally
            {
                lock (gate)
                {
                    resetting = false;
                }
            }

            if (errors.Count != 0)
            {
                throw new AggregateException("Could not restore singleton static declarations. See the inner exceptions.", errors);
            }
        }

        private static void Invoke(Action action, List<Exception> errors)
        {
            if (action == null)
            {
                return;
            }

            try
            {
                action();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
    }
}
#endif

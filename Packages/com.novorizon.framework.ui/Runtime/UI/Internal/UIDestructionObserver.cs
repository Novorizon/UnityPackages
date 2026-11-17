#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UI
{
    // Independent of UIManager/factory lifetime. Sleeps when no native destruction is pending.
    /// <summary>提供界面 DestructionObserver的实现。</summary>
    internal sealed class UIDestructionObserver : MonoBehaviour
    {
        /// <summary>表示界面 DestructionObserver中的凭据。</summary>
        internal sealed class Ticket
        {
            internal GameObject? Target;
            internal Action? Finished;
            internal int Index = -1;
        }

        static readonly List<Ticket> pending = new();
        static UIDestructionObserver? instance;
        static bool quitting;
        /// <summary>观察界面 DestructionObserver。</summary>
        internal static Ticket Observe(GameObject target, Action finished)
        {
            var ticket = new Ticket
            {
                Target = target,
                Finished = finished,
                Index = pending.Count
            };
            pending.Add(ticket);
            if (!quitting)
            {
                EnsureDriver();
            }

            return ticket;
        }

        /// <summary>检查并确保驱动可用。</summary>
        static void EnsureDriver()
        {
            if (instance == null)
            {
                var go = new GameObject("UI Native Destruction Observer");
                instance = go.AddComponent<UIDestructionObserver>();
                Object.DontDestroyOnLoad(go);
            }

            instance.enabled = pending.Count != 0;
        }

        /// <summary>取消界面 DestructionObserver。</summary>
        internal static void Cancel(Ticket? ticket)
        {
            if (ticket == null || ticket.Index < 0)
            {
                return;
            }

            int index = ticket.Index;
            Ticket last = pending[pending.Count - 1];
            pending[index] = last;
            last.Index = index;
            pending.RemoveAt(pending.Count - 1);
            ticket.Index = -1;
            ticket.Target = null;
            ticket.Finished = null;
            if (pending.Count == 0 && instance != null)
            {
                instance.enabled = false;
            }
        }

        /// <summary>清扫界面 DestructionObserver。</summary>
        static void Sweep()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (i >= pending.Count)
                {
                    continue;
                }

                Ticket ticket = pending[i];
                if (ticket.Target != null)
                {
                    continue;
                }

                Action? callback = ticket.Finished;
                Cancel(ticket);
                try
                {
                    callback?.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        /// <summary>推进该对象的一次更新。</summary>
        void Update() => Sweep();
        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        void OnDestroy()
        {
            if (ReferenceEquals(instance, this))
            {
                instance = null;
            }

            Sweep();
        }

        /// <summary>停止 For 退出。</summary>
        internal static void StopForQuit()
        {
            quitting = true;
        }

        /// <summary>重置 ForPlay。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetForPlay()
        {
            quitting = false;
            // Domain Reload may be disabled. Release dead targets retained from the previous Play session.
            Sweep();
            if (pending.Count != 0)
            {
                EnsureDriver();
            }
        }
    }
}

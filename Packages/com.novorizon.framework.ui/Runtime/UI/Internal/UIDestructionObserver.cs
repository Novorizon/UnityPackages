#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UI
{
    // Independent of UIManager/factory lifetime. Sleeps when no native destruction is pending.
    internal sealed class UIDestructionObserver : MonoBehaviour
    {
        internal sealed class Ticket
        {
            internal GameObject? Target;
            internal Action? Finished;
            internal int Index = -1;
        }

        static readonly List<Ticket> pending = new();
        static UIDestructionObserver? instance;
        static bool quitting;
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

        void Update() => Sweep();
        void OnDestroy()
        {
            if (ReferenceEquals(instance, this))
            {
                instance = null;
            }

            Sweep();
        }

        internal static void StopForQuit()
        {
            quitting = true;
        }

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
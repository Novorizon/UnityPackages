#nullable disable
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Game.Framework
{
    internal static class MonoSingletonLifecycle
    {
        private static readonly object gate = new object();
        private static readonly List<Action> resets = new List<Action>();
        private static int quitting;
        internal static bool IsQuitting => Volatile.Read(ref quitting) != 0;
        internal static void Register(Action reset) { lock (gate) resets.Add(reset); }
        internal static void Reset()
        {
            Volatile.Write(ref quitting, 0);
            Action[] snapshot; lock (gate) snapshot = resets.ToArray();
            foreach (var reset in snapshot) reset();
        }
        internal static void Quit() { Volatile.Write(ref quitting, 1); }
    }
    public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
    {
        private static T instance;
        private bool retired;
        static MonoSingleton() { MonoSingletonLifecycle.Register(() => instance = null); }
        public virtual string RootName => typeof(T).Name;
        protected bool IsDestroying => retired;

        public static T Instance
        {
            get
            {
                if (MonoSingletonLifecycle.IsQuitting) return null;
                if (!UnityThread.IsMainThread) throw new InvalidOperationException("MonoSingleton instances must be accessed on the initialized Unity main thread.");
                if (TryGetInstance(out var current)) return Prepare(current);

                T found = null;
                foreach (var candidate in FindObjectsOfType<T>(true))
                {
                    if (candidate.retired) continue;
                    if (found == null) found = candidate;
                    else candidate.RejectDuplicate(found);
                }
                if (found != null)
                {
                    instance = found;
                    return Prepare(found);
                }

                var go = new GameObject(typeof(T).Name);
                try
                {
                    var created = go.AddComponent<T>();
                    if (created.retired) throw new ObjectDisposedException(typeof(T).FullName);
                    instance = created;
                    return Prepare(created);
                }
                catch { Destroy(go); throw; }
            }
        }

        /// <summary>Checks the published reference without creating or initializing a component.</summary>
        public static bool TryGetInstance(out T value)
        {
            value = instance;
            return value != null && !value.retired;
        }

        /// <summary>Retires the reference immediately, including while native Destroy is deferred.</summary>
        public static void DestroyInstance()
        {
            if (!TryGetInstance(out var current)) return;
            current.RetireSingleton();
            Destroy(current.gameObject);
        }

        private static T Prepare(T value)
        {
            try
            {
                value.OnStart();
                if (value.retired || MonoSingletonLifecycle.IsQuitting) throw new ObjectDisposedException(typeof(T).FullName);
                if (value.transform.parent == null) DontDestroyOnLoad(value.gameObject);
                return value;
            }
            catch
            {
                value.RetireSingleton();
                Destroy(value.gameObject);
                throw;
            }
        }

        private void RejectDuplicate(T existing)
        {
            RetireSingleton();
            OnDuplicateInstance(existing);
        }

        // Domain-specific initialization and cleanup remain in the component using this base.
        protected virtual void OnStart() { }
        protected virtual void OnDuplicateInstance(T existing) { Destroy(gameObject); }
        protected static void ResetSingletonReference() { instance = null; }
        protected void RetireSingleton()
        {
            retired = true;
            if (ReferenceEquals(instance, this)) instance = null;
        }

        protected virtual void Awake()
        {
            if (retired || MonoSingletonLifecycle.IsQuitting) { RetireSingleton(); Destroy(gameObject); return; }
            if (instance != null && instance != this) { RejectDuplicate(instance); return; }
            instance = (T)this;
            Prepare((T)this);
            if (transform.parent != null) Logger.Warn("MonoSingleton should be placed on a root GameObject: " + typeof(T).Name);
        }
        protected virtual void OnApplicationQuit() { MonoSingletonLifecycle.Quit(); }
        protected virtual void OnDestroy() { RetireSingleton(); }
    }
}

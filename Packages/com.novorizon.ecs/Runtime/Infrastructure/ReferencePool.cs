using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ECS
{
    /// <summary>
    /// Main-thread pool for managed class instances. Generic creation uses new T(), not reflection.
    /// Configure factories before first use; Type/string creation only uses registered factories.
    /// </summary>
    /// <remarks>提供引用池的实现。</remarks>
    public partial class ReferencePool : SingletonBehaviour<ReferencePool>
    {
        /// <summary>定义实例状态可选的枚举值。</summary>
        private enum InstanceStatus { CheckedOut, Initializing, Recycling, Pooled, Discarded }

        /// <summary>实例对应的状态。保存状态或定义允许的状态值。</summary>
        private sealed class InstanceState
        {
            internal readonly Type Type;
            internal readonly object Owner;
            internal InstanceStatus Status;
            internal bool Initialized;
            /// <summary>使用调用方提供的type、owner创建 InstanceState。</summary>
            internal InstanceState(Type type, object owner)
            {
                Type = type;
                Owner = owner;
                Status = InstanceStatus.CheckedOut;
            }
        }

        /// <summary>表示引用池中的桶。</summary>
        private sealed class Bucket
        {
            internal readonly Type Type;
            internal readonly Func<object> Factory;
            internal readonly Delegate Registration;
            internal readonly Stack<object> Available = new Stack<object>();
            internal bool Creating;
            /// <summary>使用调用方提供的type、factory、registration创建 Bucket。</summary>
            internal Bucket(Type type, Func<object> factory, Delegate registration)
            {
                Type = type;
                Factory = factory;
                Registration = registration;
            }
        }

        /// <summary>表示引用池中的默认值工厂。</summary>
        private static class DefaultFactory<T> where T : new()
        {
            internal static readonly Func<object> Create = () => new T();
        }

        private readonly Dictionary<Type, Bucket> buckets = new Dictionary<Type, Bucket>();
        private readonly Dictionary<string, Type> names = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly HashSet<string> ambiguousNames = new HashSet<string>(StringComparer.Ordinal);
        // A checked-out object abandoned by its caller must remain collectable.
        private static readonly ConditionalWeakTable<object, InstanceState> instances = new ConditionalWeakTable<object, InstanceState>();
        private object owner = new object();
        private bool deleted;

        public int RegisteredTypeCount { get { return buckets.Count; } }

        /// <summary>响应已初始化。</summary>
        protected override void OnInitialized()
        {
            // A Unity component may survive a no-domain/no-scene-reload Play session.
            // New ownership prevents outstanding objects from the previous session entering this pool.
            Clear();
            buckets.Clear();
            names.Clear();
            ambiguousNames.Clear();
            owner = new object();
            deleted = false;
            base.OnInitialized();
        }

        /// <summary>Idempotently registers the default constructor without replacing a custom factory.</summary>
        /// <remarks>登记引用池。</remarks>
        public void Register<T>() where T : new()
        {
            RequireUsable();
            var type = typeof(T);
            if (!buckets.ContainsKey(type))
            {
                ValidateType(type);
                AddRegistration(type, DefaultFactory<T>.Create, DefaultFactory<T>.Create);
            }
        }

        /// <summary>Registers a factory. Re-registering the same delegate is safe; replacing it is rejected.</summary>
        /// <remarks>登记引用池。</remarks>
        public void Register<T>(Func<T> factory) where T : class
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            RequireUsable();
            var type = typeof(T);
            ValidateType(type);
            if (IsExistingRegistration(type, factory)) return;
            AddRegistration(type, () => factory(), factory);
        }

        /// <summary>登记引用池。</summary>
        public void Register(Type type, Func<object> factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            RequireUsable();
            ValidateType(type);
            if (IsExistingRegistration(type, factory)) return;
            AddRegistration(type, factory, factory);
        }

        /// <summary>判断是否为现有登记。</summary>
        private bool IsExistingRegistration(Type type, Delegate registration)
        {
            if (!buckets.TryGetValue(type, out var bucket)) return false;
            if (bucket.Registration.Equals(registration)) return true;
            throw new InvalidOperationException("A factory is already registered for " + type + ". Configure it before first use.");
        }

        /// <summary>添加登记。</summary>
        private void AddRegistration(Type type, Func<object> factory, Delegate registration)
        {
            // Add only exact CLR types; aliases are a lookup convenience, never the pool identity.
            buckets.Add(type, new Bucket(type, factory, registration));
            AddAlias(type.AssemblyQualifiedName, type);
            AddAlias(type.FullName, type);
        }

        /// <summary>添加 Alias。</summary>
        private void AddAlias(string name, Type type)
        {
            if (string.IsNullOrEmpty(name) || ambiguousNames.Contains(name)) return;
            if (names.TryGetValue(name, out var previous) && previous != type)
            {
                names.Remove(name);
                ambiguousNames.Add(name);
            }
            else names[name] = type;
        }

        /// <summary>Resolves registered full or assembly-qualified names without loading/scanning assemblies.</summary>
        /// <remarks>解析类型。</remarks>
        public Type ResolveType(string registeredName)
        {
            RequireUsable();
            if (string.IsNullOrEmpty(registeredName)) throw new ArgumentException("A registered type name is required.", nameof(registeredName));
            if (ambiguousNames.Contains(registeredName))
                throw new ArgumentException("Ambiguous registered type name; use a Type or an unambiguous assembly-qualified name: " + registeredName, nameof(registeredName));
            if (!names.TryGetValue(registeredName, out var type))
                throw new InvalidOperationException("No factory is registered for " + registeredName + ". Call Register<T>() or Register(Type, factory) first.");
            return type;
        }

        /// <summary>生成实例。</summary>
        public T SpawnInstance<T>(params object[] args) where T : new()
        {
            var result = SpawnInstanceUninitialized<T>(args);
            InitializeInstance(result);
            return result;
        }

        /// <summary>生成实例未初始化。</summary>
        public T SpawnInstanceUninitialized<T>(params object[] args) where T : new()
        {
            ValidateArguments(args);
            Register<T>();
            return (T)Rent(buckets[typeof(T)]);
        }

        /// <summary>生成实例。</summary>
        public object SpawnInstance(Type type, params object[] args)
        {
            var result = SpawnInstanceUninitialized(type, args);
            InitializeInstance(result);
            return result;
        }

        /// <summary>生成实例未初始化。</summary>
        public object SpawnInstanceUninitialized(Type type, params object[] args)
        {
            RequireUsable();
            ValidateArguments(args);
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!buckets.TryGetValue(type, out var bucket))
            {
                ValidateType(type);
                throw new InvalidOperationException("No factory is registered for " + type + ". Call Register<T>() or Register(Type, factory) first.");
            }
            return Rent(bucket);
        }

        /// <summary>生成实例。</summary>
        public object SpawnInstance(string registeredName, params object[] args)
        {
            var result = SpawnInstanceUninitialized(registeredName, args);
            InitializeInstance(result);
            return result;
        }

        /// <summary>生成实例未初始化。</summary>
        public object SpawnInstanceUninitialized(string registeredName, params object[] args)
        {
            ValidateArguments(args);
            return SpawnInstanceUninitialized(ResolveType(registeredName));
        }

        /// <summary>租用引用池。</summary>
        private object Rent(Bucket bucket)
        {
            RequireUsable();
            if (bucket.Available.Count > 0)
            {
                var cached = bucket.Available.Pop();
                if (!instances.TryGetValue(cached, out var cachedState) ||
                    !ReferenceEquals(cachedState.Owner, owner) ||
                    cachedState.Status != InstanceStatus.Pooled || cachedState.Type != bucket.Type)
                    throw new InvalidOperationException("The pool contains an invalid cached instance.");
                cachedState.Status = InstanceStatus.CheckedOut;
                cachedState.Initialized = false;
                return cached;
            }

            if (bucket.Creating)
                throw new InvalidOperationException("A factory cannot recursively create its own type while no cached instance is available: " + bucket.Type);
            object created;
            bucket.Creating = true;
            try { created = bucket.Factory(); }
            finally { bucket.Creating = false; }
            RequireUsable();
            if (created == null || created.GetType() != bucket.Type)
                throw new InvalidOperationException("The factory must return a non-null instance of exactly " + bucket.Type + ".");
            if (instances.TryGetValue(created, out var existing))
                throw new InvalidOperationException("The factory returned an instance already owned by a pool (" + existing.Status + ").");
            instances.Add(created, new InstanceState(bucket.Type, owner));
            return created;
        }

        /// <summary>Owners such as World set context first, then initialize once through this guard.</summary>
        /// <remarks>初始化实例。</remarks>
        internal void InitializeInstance(object instance)
        {
            RequireUsable();
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!instances.TryGetValue(instance, out var state) || !ReferenceEquals(state.Owner, owner) ||
                state.Status != InstanceStatus.CheckedOut || state.Initialized)
                throw new InvalidOperationException("Only a fresh checked-out instance can be initialized.");
            state.Status = InstanceStatus.Initializing;
            try
            {
                if (instance is IInitializeable initializeable) initializeable.OnInitialized();
                RequireUsable();
                state.Initialized = true;
                state.Status = InstanceStatus.CheckedOut;
            }
            catch (Exception initializationError)
            {
                state.Status = InstanceStatus.Recycling;
                try
                {
                    if (instance is IRecycleable recycleable) recycleable.OnRecycle();
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException("Instance initialization and cleanup both failed.", initializationError, cleanupError);
                }
                finally { state.Status = InstanceStatus.Discarded; }
                throw;
            }
        }

        /// <summary>回收实例。</summary>
        public void RecycleInstance(object instance)
        {
            RequireUsable();
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            if (!instances.TryGetValue(instance, out var state) || !ReferenceEquals(state.Owner, owner) ||
                state.Status != InstanceStatus.CheckedOut)
                throw new InvalidOperationException("Only an instance checked out from this pool can be recycled once.");
            state.Status = InstanceStatus.Recycling;
            try
            {
                if (instance is IRecycleable recycleable) recycleable.OnRecycle();
            }
            catch
            {
                state.Status = InstanceStatus.Discarded;
                throw;
            }
            if (deleted || IsDestroying)
            {
                state.Status = InstanceStatus.Discarded;
                return;
            }
            state.Status = InstanceStatus.Pooled;
            buckets[state.Type].Available.Push(instance);
        }

        /// <summary>Drops idle objects only. Factories and outstanding lease bookkeeping remain valid.</summary>
        /// <remarks>清空引用池。</remarks>
        public void Clear()
        {
            foreach (var bucket in buckets.Values) bucket.Available.Clear();
        }

        /// <summary>响应 Delete。</summary>
        protected override void OnDelete()
        {
            deleted = true;
            Clear();
            buckets.Clear();
            names.Clear();
            ambiguousNames.Clear();
            base.OnDelete();
        }

        /// <summary>检查 Usable 是否满足前置条件。</summary>
        private void RequireUsable()
        {
            if (deleted || IsDestroying) throw new ObjectDisposedException(nameof(ReferencePool));
        }

        /// <summary>验证参数。</summary>
        private static void ValidateArguments(object[] args)
        {
            if (args != null && args.Length != 0)
                throw new ArgumentException("Constructor arguments are not supported. Register a factory that captures the required values.", nameof(args));
        }

        /// <summary>验证类型。</summary>
        private static void ValidateType(Type type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || type.AssemblyQualifiedName == null)
                throw new ArgumentException("A concrete closed managed class type is required.", nameof(type));
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                throw new ArgumentException("UnityEngine.Object instances must use Unity creation and lease APIs, not the managed reference pool.", nameof(type));
        }
    }
}

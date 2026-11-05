using System;
using System.Collections.Generic;
using System.Threading;
using Google.Protobuf;

namespace Game.Framework.Network.Dispatching
{
    internal sealed class NotificationRouter
    {
        private sealed class Listener : IDisposable
        {
            internal Action<object> Invoke;
            internal int Active = 1;
            internal Action Remove;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref Active, 0) == 0) return;
                try { Interlocked.Exchange(ref Remove, null)?.Invoke(); }
                finally { Volatile.Write(ref Invoke, null); }
            }
            internal void Deactivate()
            { Interlocked.Exchange(ref Active, 0); Volatile.Write(ref Invoke, null); Interlocked.Exchange(ref Remove, null); }
        }
        private sealed class Binding
        {
            internal Type Type;
            internal Func<byte[], object> Parse;
            internal readonly List<Listener> Listeners = new List<Listener>();
            internal Listener[] Snapshot = Array.Empty<Listener>();
        }
        private readonly object sync = new object();
        private readonly Dictionary<int, Binding> bindings = new Dictionary<int, Binding>();
        internal IDisposable Subscribe<T>(int messageId, MessageParser<T> parser, Action<T> handler) where T : class, IMessage<T>
        {
            if (parser == null || handler == null) throw new ArgumentNullException(nameof(handler));
            lock (sync)
            {
                if (!bindings.TryGetValue(messageId, out var binding))
                {
                    binding = new Binding { Type = typeof(T), Parse = CreateParser(parser) }; bindings.Add(messageId, binding);
                }
                else if (binding.Type != typeof(T)) throw new InvalidOperationException("Message ID is already registered with another message type.");
                var listener = new Listener { Invoke = CreateHandler(handler) };
                listener.Remove = () =>
                {
                    lock (sync)
                    {
                        if (!binding.Listeners.Remove(listener)) return;
                        binding.Snapshot = binding.Listeners.ToArray();
                        if (binding.Listeners.Count == 0 && bindings.TryGetValue(messageId, out var current) && ReferenceEquals(current, binding))
                            bindings.Remove(messageId);
                    }
                };
                binding.Listeners.Add(listener); binding.Snapshot = binding.Listeners.ToArray(); return listener;
            }
        }
        // Separate factories keep the long-lived parser closure from retaining the first UI handler.
        private static Func<byte[], object> CreateParser<T>(MessageParser<T> parser) where T : class, IMessage<T> => bytes => parser.ParseFrom(bytes);
        private static Action<object> CreateHandler<T>(Action<T> handler) => value => handler((T)value);
        internal Action Capture(int messageId, byte[] payload, Action<Exception> parseError, Action<Exception> callbackError)
        {
            Binding binding; Listener[] listeners;
            lock (sync)
            {
                if (!bindings.TryGetValue(messageId, out binding)) return null;
                listeners = binding.Snapshot;
            }
            // Keep the closure in a separate method: missing bindings allocate nothing.
            // Capture-time snapshots stay immutable when a listener is added or removed later.
            return CreateDispatch(binding.Parse, listeners, payload, parseError, callbackError);
        }
        private static Action CreateDispatch(Func<byte[], object> parse, Listener[] listeners, byte[] payload,
            Action<Exception> parseError, Action<Exception> callbackError)
        {
            return () =>
            {
                object message;
                try { message = parse(payload); }
                catch (Exception error) { parseError(error); return; }
                foreach (var listener in listeners)
                {
                    var invoke = Volatile.Read(ref listener.Invoke);
                    if (Volatile.Read(ref listener.Active) == 0 || invoke == null) continue;
                    try { invoke(message); } catch (Exception error) { callbackError(error); }
                }
            };
        }
        internal void Clear()
        {
            lock (sync)
            {
                foreach (var binding in bindings.Values) foreach (var listener in binding.Listeners) listener.Deactivate();
                bindings.Clear();
            }
        }
    }
}

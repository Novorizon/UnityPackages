using System;
using System.Collections.Generic;

namespace Game.Framework
{
    /// <summary>记录入口本轮已创建的服务，并按创建顺序的反方向清理。</summary>
    public sealed class EntryServiceScope : IDisposable
    {
        private readonly Stack<Action> cleanups = new Stack<Action>();
        private bool disposed;

        public T Register<T>(T service) where T : Singleton<T>, IDisposable, new()
        {
            ThrowIfDisposed();
            Singleton<T>.RegisterEntryInstance(service);
            cleanups.Push(() =>
            {
                Singleton<T>.UnregisterEntryInstance(service);
                service.Dispose();
            });
            return service;
        }

        public T Own<T>(T service) where T : class, IDisposable
        {
            ThrowIfDisposed();
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            cleanups.Push(service.Dispose);
            return service;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            while (cleanups.Count > 0)
            {
                try
                {
                    cleanups.Pop()();
                }
                catch (Exception error)
                {
                    Logger.Exception(error);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(EntryServiceScope));
            }
        }
    }
}

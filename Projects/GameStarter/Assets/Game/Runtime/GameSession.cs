using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    public enum StartupState { NotStarted, Starting, Ready, Failed, Stopped }

    internal sealed class GameSession : IDisposable
    {
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly List<IStartupModule> started = new List<IStartupModule>();
        private readonly Action<Exception> log;
        private TaskCompletionSource<bool> completion;
        private bool stopped;
        private IStartupModule[] readyModules = Array.Empty<IStartupModule>();
        public StartupState State { get; private set; }

        internal GameSession(Action<Exception> log) { this.log = log; }

        internal Task StartAsync(IEnumerable<IStartupModule> modules)
        {
            if (completion != null) return completion.Task;
            if (stopped) throw new ObjectDisposedException(nameof(GameSession));
            var selected = modules.ToArray();
            var unique = new HashSet<IStartupModule>();
            foreach (var module in selected)
                if (module == null || !unique.Add(module)) throw new ArgumentException("Startup modules must be non-null and unique.");
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            State = StartupState.Starting;
            _ = InitializeAsync(selected, lifetime.Token);
            return completion.Task;
        }

        private async Task InitializeAsync(IStartupModule[] modules, CancellationToken token)
        {
            try
            {
                foreach (var module in modules)
                {
                    token.ThrowIfCancellationRequested();
                    started.Add(module);
                    await module.InitializeAsync(token);
                }
                token.ThrowIfCancellationRequested();
                readyModules = modules;
                State = StartupState.Ready;
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            { completion.TrySetCanceled(token); }
            catch (Exception exception)
            {
                if (!stopped) { State = StartupState.Failed; CancelLifetime(); ReleaseModules(); }
                completion.TrySetException(exception);
            }
        }

        internal void Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (State != StartupState.Ready) return;
            // A module may request shutdown from its own update.
            var snapshot = readyModules;
            foreach (var module in snapshot)
            {
                if (State != StartupState.Ready) break;
                try { module.Tick(deltaTime, unscaledDeltaTime); }
                catch (Exception exception) { State = StartupState.Failed; CancelLifetime(); ReleaseModules(); log(exception); break; }
            }
        }

        private void ReleaseModules()
        {
            readyModules = Array.Empty<IStartupModule>();
            var snapshot = started.ToArray();
            started.Clear();
            for (int i = snapshot.Length - 1; i >= 0; i--)
                try { snapshot[i].Shutdown(); }
                catch (Exception exception) { log(exception); }
        }

        private void CancelLifetime()
        {
            try { lifetime.Cancel(); } catch (Exception exception) { log(exception); }
        }

        public void Dispose()
        {
            if (stopped) return;
            stopped = true;
            State = StartupState.Stopped;
            CancelLifetime();
            ReleaseModules();
            lifetime.Dispose();
        }
    }
}

using System;
using System.Threading.Tasks;
using Game.Framework;
using UnityEngine;

namespace Game
{
    // AOT project entry. Concrete game systems are optional StartupModule components.
    public sealed class GameEntry : MonoSingleton<GameEntry>
    {
        [SerializeField] private StartupModule[] startupModules = Array.Empty<StartupModule>();
        private static GameEntry sessionOwner;
        private GameSession session;
        private bool closing;
        public StartupState State => session?.State ?? StartupState.NotStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            if (sessionOwner != null)
            {
                var previous = sessionOwner;
                previous.CloseSession();
                previous.closing = false;
            }
            sessionOwner = null;
            ResetSingletonReference();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ResumeSession()
        {
            // Scene reload can be disabled in Enter Play Mode options.
            foreach (var entry in FindObjectsOfType<GameEntry>(true))
                if (entry.gameObject.activeInHierarchy && !entry.IsDestroying)
                {
                    entry.closing = false;
                    _ = Instance;
                    break;
                }
        }

        protected override void OnSingletonAccessed()
        {
            if (closing || session != null) return;
            sessionOwner = this;
            var current = new GameSession(Debug.LogException);
            session = current;
            _ = StartSessionAsync(current);
        }

        private async Task StartSessionAsync(GameSession current)
        {
            try
            {
                await current.StartAsync(startupModules);
                if (ReferenceEquals(session, current)) Debug.Log("GAME_STARTER_READY");
            }
            catch (OperationCanceledException) when (!ReferenceEquals(session, current)) { }
            catch (Exception exception) { if (ReferenceEquals(session, current)) Debug.LogException(exception); }
        }

        private void Update() { if (!IsDestroying) session?.Tick(Time.deltaTime, Time.unscaledDeltaTime); }
        protected override void OnApplicationQuit() { base.OnApplicationQuit(); CloseSession(); }
        protected override void OnDestroy() { base.OnDestroy(); CloseSession(); }
        private void CloseSession()
        {
            closing = true;
            var previous = session;
            session = null;
            previous?.Dispose();
            if (ReferenceEquals(sessionOwner, this)) sessionOwner = null;
        }
    }
}

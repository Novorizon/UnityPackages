using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.HotUpdate;
using HybridCLR;
using UnityEngine;

namespace Game.Framework.HotUpdate
{
    public sealed class HotGameBootstrap : MonoSingleton<HotGameBootstrap>, IGameHost
    {
        private static HotGameBootstrap sessionOwner;
        private CancellationTokenSource lifetime;
        private IGameModule hotGame;
        private bool hotGameStopped;
        private bool ready;
        private bool initializationReturned;
        private bool startupClosed;
        private string hotGameVersion;
        public static string Status { get; private set; } = "Starting";
        public static bool Failed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (sessionOwner != null)
            {
                sessionOwner.CloseSession();
            }

            sessionOwner = null;
            ResetSingletonReference();
            Status = "Starting";
            Failed = false;
        }

        // Unity 识别此特性，在首个场景加载后调用 AutoCreate，无需 GameEntry 手动调用。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            try
            {
                if (Resources.Load<TextAsset>(UpdateSettings.ResourceName) && UpdateSettings.Load().autoStart)
                {
                    // 单例基类在场景中查找宿主；不存在时创建 GameObject 并添加本组件。
                    _ = Instance;
                }
            }
            catch (Exception exception)
            {
                FailStartup(exception);
            }
        }

        protected override void OnStart()
        {
            if (lifetime != null)
            {
                return;
            }

            sessionOwner = this;
            ready = initializationReturned = startupClosed = hotGameStopped = false;
            hotGame = null;
            lifetime = new CancellationTokenSource();
            _ = StartAsync(lifetime);
        }

        private async Task StartAsync(CancellationTokenSource session)
        {
            CancellationToken stopToken = session.Token;
            PreparedRelease release = null;
            try
            {
                var settings = UpdateSettings.Load();
                await Task.Yield();
                stopToken.ThrowIfCancellationRequested();
#if UNITY_EDITOR
                // Editor uses Unity's compiled assembly and does not prove IL2CPP/metadata behavior.
                Log("Editor mode: running compiled source; remote release loading is disabled.");
                var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == settings.entryAssembly);
#else
                using (var preparation = CancellationTokenSource.CreateLinkedTokenSource(stopToken))
                {
                    preparation.CancelAfter(TimeSpan.FromSeconds(settings.prepareTimeoutSeconds));
                    try
                    {
                        release = await new UpdateCoordinator(settings, Log).Prepare(preparation.Token);
                    }
                    catch (OperationCanceledException) when (!stopToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("Content preparation exceeded its time budget.");
                    }
                }

                stopToken.ThrowIfCancellationRequested();
                for (int i = 0; i < release.AotMetadata.Length; i++)
                {
                    var code = RuntimeApi.LoadMetadataForAOTAssembly(release.AotMetadata[i], HomologousImageMode.SuperSet);
                    if (code != LoadImageErrorCode.OK)
                    {
                        throw new InvalidOperationException("AOT metadata load failed: " + release.Manifest.aotMetadata[i].assemblyName + " / " + code);
                    }
                }

                release.AotMetadata = null;
                // Never attempt an in-process fallback once metadata or managed code has been loaded.
                Assembly assembly = null;
                for (int i = 0; i < release.Assemblies.Length; i++)
                {
                    var expectedName = release.Manifest.assemblies[i].assemblyName;
                    if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == expectedName))
                    {
                        throw new InvalidOperationException("Hot update assembly already loaded: " + expectedName);
                    }

                    var loaded = Assembly.Load(release.Assemblies[i]);
                    if (loaded.GetName().Name != expectedName)
                    {
                        throw new InvalidOperationException("Unexpected assembly identity.");
                    }

                    if (expectedName == settings.entryAssembly)
                    {
                        assembly = loaded;
                    }
                }

                release.Assemblies = null;
#endif
                if (assembly == null)
                {
                    throw new InvalidOperationException("Entry assembly was not found: " + settings.entryAssembly);
                }

                Type entry = assembly.GetType(settings.entryType, true);
                if (!typeof(IGameModule).IsAssignableFrom(entry))
                {
                    throw new InvalidOperationException("Incompatible hot game contract.");
                }

                // 脚本文件定义类型；这里才创建运行时对象，相当于 new HotGame()。
                // 通过接口调用，让 AOT 宿主不必引用热更程序集里的具体类型。
                hotGame = (IGameModule)Activator.CreateInstance(entry);
                using (var startup = CancellationTokenSource.CreateLinkedTokenSource(stopToken))
                {
                    Task initialize = hotGame.InitializeAsync(new SessionHost(this, session), startup.Token);
                    Task deadline = Task.Delay(TimeSpan.FromSeconds(settings.startupTimeoutSeconds), startup.Token);
                    if (await Task.WhenAny(initialize, deadline) != initialize)
                    {
                        startup.Cancel();
                        stopToken.ThrowIfCancellationRequested();
                        throw new TimeoutException("Game initialization exceeded its time budget.");
                    }

                    await initialize;
                    startup.Cancel();
                }

                stopToken.ThrowIfCancellationRequested();
                initializationReturned = true;
                if (!ready)
                {
                    throw new InvalidOperationException("Hot game did not acknowledge startup health.");
                }

                stopToken.ThrowIfCancellationRequested();
                if (release?.Id != null)
                {
                    release.Cache.MarkHealthy(release.Id, release.Manifest.version > release.AcceptedVersion);
                }

                release?.Cache?.Dispose();
                startupClosed = true;
                Status = "Ready / " + hotGameVersion;
                Log("STARTUP_OK " + hotGameVersion);
                if (Application.isBatchMode && !Application.isEditor)
                {
                    Application.Quit(0);
                }
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                release?.Cache?.Dispose();
                if (ReferenceEquals(lifetime, session))
                {
                    startupClosed = true;
                }
            }
            catch (Exception exception)
            {
                if (!ReferenceEquals(lifetime, session))
                {
                    release?.Cache?.Dispose();
                    return;
                }

                startupClosed = true;
                StopHotGame();
                if (release?.Id != null)
                {
                    try
                    {
                        release.Cache.Reject(release.Id, exception.Message);
                    }
                    catch (Exception cacheError)
                    {
                        Debug.LogException(cacheError);
                    }
                }

                release?.Cache?.Dispose();
                startupClosed = true;
                FailStartup(exception);
            // The next process can safely choose another release; this process cannot unload its code.
            }
        }

        private static void FailStartup(Exception exception)
        {
            Failed = true;
            Status = "Startup failed. Please restart the game.";
            Debug.LogException(exception);
            if (Application.isBatchMode && !Application.isEditor)
            {
                Application.Quit(1);
            }
        }

        public void Log(string message)
        {
            Debug.Log("[Startup] " + message);
        }

        private sealed class SessionHost : IGameHost
        {
            private readonly HotGameBootstrap owner;
            private readonly CancellationTokenSource session;
            internal SessionHost(HotGameBootstrap owner, CancellationTokenSource session)
            {
                this.owner = owner;
                this.session = session;
            }

            public void Log(string message)
            {
                if (ReferenceEquals(owner.lifetime, session))
                {
                    owner.Log(message);
                }
            }

            public void ReportReady(string version)
            {
                if (!ReferenceEquals(owner.lifetime, session))
                {
                    throw new InvalidOperationException("Startup session has ended.");
                }

                owner.ReportReady(version);
            }
        }

        public void ReportReady(string version)
        {
            if (ready || initializationReturned || startupClosed || lifetime == null || lifetime.IsCancellationRequested || string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException("Invalid startup acknowledgement.");
            }

            ready = true;
            hotGameVersion = version;
        }

        protected override void OnExit()
        {
            CloseSession();
            if (ReferenceEquals(sessionOwner, this))
            {
                sessionOwner = null;
            }
        }

        private void CloseSession()
        {
            startupClosed = true;
            lifetime?.Cancel();
            StopHotGame();
            lifetime?.Dispose();
            lifetime = null;
        }

        private void StopHotGame()
        {
            if (hotGameStopped)
            {
                return;
            }

            hotGameStopped = true;
            try
            {
                hotGame?.Shutdown();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HybridCLR;
using UnityEngine;

namespace Game.Framework.HotUpdate
{
    public sealed class HotUpdateManager : ConfigurableSingleton<HotUpdateManager>, IDisposable
    {
        private CancellationTokenSource lifetime;
        private Task startupTask;
        private UpdateSettings settings;
        private object startupContext;
        private IGameModule hotGame;
        private bool hotGameStopped;
        private bool ready;
        private bool initializationReturned;
        private bool startupClosed;
        private bool disposed;
        private string hotGameVersion;
        public static HotUpdateManager Instance => GetOrCreateInstance(() => new HotUpdateManager());
        public string Status { get; private set; } = "Not started";
        public bool IsReady { get; private set; }
        public bool Failed { get; private set; }

        /// <summary>初始化时显式传入配置；实际更新由 StartAsync 执行。</summary>
        public override void Initialize()
        {
            throw new InvalidOperationException("Pass UpdateSettings to Initialize, then await StartAsync.");
        }

        public static string GetPlatformId() => UpdateCoordinator.PlatformId();

        /// <summary>只保存配置和本次启动上下文，不检查更新、不下载或加载代码。</summary>
        public void Initialize(UpdateSettings settings, object startupContext = null, CancellationToken token = default)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HotUpdateManager));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (this.settings != null)
            {
                if (!ReferenceEquals(this.settings, settings) || !ReferenceEquals(this.startupContext, startupContext))
                {
                    throw new InvalidOperationException("HotUpdateManager is already initialized with another configuration.");
                }

                return;
            }

            settings.Validate();
            token.ThrowIfCancellationRequested();
            this.settings = settings;
            this.startupContext = startupContext;
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            Status = "Initialized";
        }

        /// <summary>显式执行更新、代码加载和 HotGame 启动；重复调用返回同一任务。</summary>
        public Task StartAsync()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HotUpdateManager));
            }

            if (settings == null)
            {
                throw new InvalidOperationException("Initialize HotUpdateManager before starting updates.");
            }

            if (startupTask != null)
            {
                return startupTask;
            }

            lifetime.Token.ThrowIfCancellationRequested();
            Status = "Starting";
            startupTask = RunAsync(settings, startupContext, lifetime);
            return startupTask;
        }

        /// <summary>兼容入口：依次执行配置初始化与实际更新。</summary>
        public Task InitializeAsync(UpdateSettings settings = null, CancellationToken token = default)
        {
            return InitializeAsync(settings, null, token);
        }

        /// <summary>将项目自己的启动参数传给热更入口；封装不解释或序列化该对象。</summary>
        public Task InitializeAsync(UpdateSettings settings, object startupContext, CancellationToken token = default)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HotUpdateManager));
            }

            if (startupTask != null)
            {
                return startupTask;
            }

            if (this.settings == null)
            {
                Initialize(settings ?? new UpdateSettings(), startupContext, token);
            }
            else if (settings != null)
            {
                Initialize(settings, startupContext, token);
            }
            else if (startupContext != null && !ReferenceEquals(this.startupContext, startupContext))
            {
                throw new InvalidOperationException("HotUpdateManager is already initialized with another startup context.");
            }

            return StartAsync();
        }

        private async Task RunAsync(UpdateSettings settings, object startupContext, CancellationTokenSource session)
        {
            CancellationToken stopToken = session.Token;
            PreparedRelease release = null;
            try
            {
                await Task.Yield();
                stopToken.ThrowIfCancellationRequested();
                Assembly assembly = null;
                bool compiled = settings.loadFromCompiledAssemblies;
#if UNITY_EDITOR
                compiled = true;
#endif
                if (compiled)
                {
                    Log("Compiled game module mode; remote DLL loading is disabled.");
                    if (settings.prepareContentAsync != null)
                    {
                        await settings.prepareContentAsync(new ReleaseContentContext("compiled", settings.content, true), stopToken);
                    }

                    assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == settings.entryAssembly);
                }
                else
                {
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
                }
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
                    Task initialize = hotGame.InitializeAsync(new SessionHost(this, session, startupContext), startup.Token);
                    try
                    {
                        Task deadline = Task.Delay(TimeSpan.FromSeconds(settings.startupTimeoutSeconds), startup.Token);
                        if (await Task.WhenAny(initialize, deadline) != initialize)
                        {
                            _ = ObserveInitializationAsync(initialize);
                            stopToken.ThrowIfCancellationRequested();
                            throw new TimeoutException("Game initialization exceeded its time budget.");
                        }

                        await initialize;
                    }
                    finally
                    {
                        Cancel(startup);
                    }
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

                startupClosed = true;
                IsReady = true;
                Status = "Ready / " + hotGameVersion;
                Log("STARTUP_OK " + hotGameVersion);
            }
            catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
            {
                startupClosed = true;
                StopHotGame();
                Status = "Stopped";
                throw;
            }
            catch (Exception exception)
            {
                if (!ReferenceEquals(lifetime, session))
                {
                    throw;
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

                Failed = true;
                Status = "Startup failed. Please restart the game.";
                // DLL 无法在进程内卸载；失败交给调用方，下一次进程启动再选择版本。
                throw;
            }
            finally
            {
                release?.Cache?.Dispose();
            }
        }

        private static async Task ObserveInitializationAsync(Task initialize)
        {
            try
            {
                await initialize;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public void Log(string message)
        {
            Debug.Log("[Startup] " + message);
        }

        private sealed class SessionHost : IGameContextHost
        {
            private readonly HotUpdateManager owner;
            private readonly CancellationTokenSource session;
            public object StartupContext { get; }
            internal SessionHost(HotUpdateManager owner, CancellationTokenSource session, object startupContext)
            {
                this.owner = owner;
                this.session = session;
                StartupContext = startupContext;
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

        private void ReportReady(string version)
        {
            if (ready || initializationReturned || startupClosed || lifetime == null || lifetime.IsCancellationRequested || string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException("Invalid startup acknowledgement.");
            }

            ready = true;
            hotGameVersion = version;
        }

        public void NotifyApplicationPause(bool paused)
        {
            if (IsReady && hotGame is IGameApplicationEvents events)
            {
                events.OnApplicationPause(paused);
            }
        }

        public void NotifyApplicationFocus(bool focused)
        {
            if (IsReady && hotGame is IGameApplicationEvents events)
            {
                events.OnApplicationFocus(focused);
            }
        }

        /// <summary>取消加载并清理热更入口；重复调用安全，不卸载已经加载的 DLL。</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            startupClosed = true;
            IsReady = false;
            Status = "Stopped";
            try
            {
                Cancel(lifetime);
                StopHotGame();
            }
            finally
            {
                lifetime?.Dispose();
                lifetime = null;
                settings = null;
                startupContext = null;
                ReleaseInstance(this);
            }
        }

        private static void Cancel(CancellationTokenSource source)
        {
            try
            {
                source?.Cancel();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
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

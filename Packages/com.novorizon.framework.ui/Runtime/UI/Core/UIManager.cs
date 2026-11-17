#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Game.Framework;

namespace UI
{
    /// <summary>维护 UI 根节点、各层导航和输入返回键处理。</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "UI", "Game.Framework.UI", "UIRoot")]
    public sealed partial class UIManager : MonoSingleton<UIManager>
    {
        static bool quitting;
        internal static bool IsQuitting => quitting;

        public new static UIManager Instance
        {
            get
            {
                if (quitting)
                {
                    throw new InvalidOperationException("The application is stopping; UIManager cannot be recreated.");
                }

                return MonoSingleton<UIManager>.Instance ?? throw new InvalidOperationException("The application is stopping.");
            }
        }

        [SerializeField]
        UISettings? settings = null;
        public UIMessageBus Bus { get; } = new UIMessageBus();
        public PageNavigator Pages { get; private set; } = null !;
        public PopupManager Popups { get; private set; } = null !;
        public PanelManager Panels { get; private set; } = null !;
        public OverlayManager Overlays { get; private set; } = null !;
        public ToastManager Toasts { get; private set; } = null !;
        public DialogManager Dialogs { get; private set; } = null !;

        BackRequest? pendingBack;
        CancellationTokenSource? backLifetime;
        /// <summary>表示界面管理器中的返回请求。</summary>
        sealed class BackRequest
        {
            readonly object gate = new();
            readonly CancellationTokenSource cancellation;
            readonly CancellationToken token;
            readonly TaskCompletionSource<bool> cancellationPropagated = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int waiters;
            bool finished, accepting = true, cancelling;
            internal readonly TaskCompletionSource<bool> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly CancellationToken OwnerToken;
            internal readonly DialogManager Dialogs;
            /// <summary>使用调用方提供的owner、dialogs创建 BackRequest。</summary>
            internal BackRequest(CancellationToken owner, DialogManager dialogs)
            {
                OwnerToken = owner;
                Dialogs = dialogs;
                cancellation = CancellationTokenSource.CreateLinkedTokenSource(owner);
                token = cancellation.Token;
            }

            internal CancellationToken Token => token;
            internal bool IsCompleted => Completion.Task.IsCompleted;

            internal Task CancellationPropagation
            {
                get
                {
                    lock (gate)
                    {
                        return cancelling ? cancellationPropagated.Task : Task.CompletedTask;
                    }
                }
            }

            /// <summary>尝试处理 Join，返回是否成功。</summary>
            internal bool TryJoin(out BackWaiter? waiter)
            {
                lock (gate)
                {
                    if (finished || !accepting || token.IsCancellationRequested)
                    {
                        waiter = null;
                        return false;
                    }

                    waiters++;
                    waiter = new BackWaiter(this);
                    return true;
                }
            }

            /// <summary>离开返回请求。</summary>
            internal void Leave()
            {
                bool cancel;
                lock (gate)
                {
                    waiters--;
                    cancel = waiters == 0 && !finished;
                    if (cancel)
                    {
                        accepting = false;
                        cancelling = true;
                    }
                }

                if (!cancel)
                {
                    return;
                } // Invoke cancellation outside the lock; user callbacks may reenter UI.

                try
                {
                    cancellation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                } // Completion won the race with the last departure.
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
                finally
                {
                    cancellationPropagated.TrySetResult(true);
                }
            }

            /// <summary>结束返回请求。</summary>
            internal void Finish(bool result, Exception? failure)
            {
                lock (gate)
                {
                    finished = true;
                    accepting = false;
                }

                if (failure == null)
                {
                    Completion.TrySetResult(result);
                }
                else if (failure is OperationCanceledException cancelled)
                {
                    Completion.TrySetCanceled(cancelled.CancellationToken);
                }
                else
                {
                    Completion.TrySetException(failure);
                }

                cancellation.Dispose();
            }
        }

        /// <summary>表示界面管理器中的返回等待方。</summary>
        sealed class BackWaiter : IDisposable
        {
            BackRequest? owner;
            /// <summary>使用调用方提供的owner创建 BackWaiter。</summary>
            internal BackWaiter(BackRequest owner)
            {
                this.owner = owner;
            }

            /// <summary>结束返回等待方对相关资源的持有，并执行该类型的清理流程。</summary>
            public void Dispose() => Interlocked.Exchange(ref owner, null)?.Leave();
        }

        IUIAssetLoader loader = new ResourcesAssetLoader();
        readonly Dictionary<UILayer, Transform> layerRoots = new();
        UIInstanceFactory? factory;
        Canvas? rootCanvas;
        GameObject? ownedEventSystem;
        EventSystem? preferredEventSystem;
        EventSystem? currentEventSystem;
        Camera? explicitCamera;
        bool destroyed, rebuilding, initialized, initializing, platformSubscribed, shuttingDown, warnedMissingInput;
        Task shutdownCompletion = Task.CompletedTask;
        readonly List<Task> retiredFactories = new();
        public bool IsInitialized => initialized && !destroyed && !shuttingDown;
        public int ActiveViewCount => factory?.ActiveCount ?? 0;
        public int CachedViewCount => factory?.CachedCount ?? 0;

        /// <summary>Unity 创建组件时执行本组件的初始化逻辑。</summary>
        protected override void Awake()
        {
            if (quitting)
            {
                Destroy(gameObject);
                return;
            }

            base.Awake();
        }

        /// <summary>响应 Start。</summary>
        protected override void OnStart()
        {
            destroyed = false;
            if (!shutdownCompletion.IsCompleted)
            {
                // 允许通过 Instance 重复等待关闭；不能在关闭期间隐式重新初始化。
                return;
            }

            Initialize();
        }

        /// <summary>响应 Unity 组件启用。</summary>
        void OnEnable()
        {
            if (!quitting && !destroyed && !initialized && !initializing)
            {
                base.Awake();
            }
        }

        /// <summary>初始化界面管理器。</summary>
        public override void Initialize()
        {
            ThrowIfUnavailable();
            if (initialized || initializing)
            {
                return;
            }

            initializing = true;
            try
            {
                if (transform.parent != null)
                {
                    transform.SetParent(null, false);
                }

                DontDestroyOnLoad(gameObject);
                if (settings == null)
                {
                    settings = Resources.Load<UISettings>("UISettings");
                }

                EnsureCanvasHierarchy();
                CreateManagers(factory?.NextId ?? 1);
                SubscribePlatform();
                initialized = true;
                RefreshPlatform();
            }
            catch
            {
                Shutdown();
                throw;
            }
            finally
            {
                initializing = false;
            }
        }

        /// <summary>在不可用时抛出异常。</summary>
        void ThrowIfUnavailable()
        {
            if (destroyed)
            {
                throw new ObjectDisposedException(nameof(UIManager));
            }

            if (quitting || shuttingDown)
            {
                throw new InvalidOperationException("UIManager is stopping.");
            }

            if (!shutdownCompletion.IsCompleted)
            {
                throw new InvalidOperationException("Await UIManager.ShutdownAsync before initializing it again.");
            }
        }

        /// <summary>设置设置。</summary>
        public void SetSettings(UISettings uiSettings)
        {
            ThrowIfUnavailable();
            settings = uiSettings != null ? uiSettings : throw new ArgumentNullException(nameof(uiSettings));
            Initialize();
            EnsureCanvasHierarchy();
            factory?.SetCacheLimits(Math.Max(0, settings.maxCachedInstances), Math.Max(0, settings.maxCachedPerPath));
            RefreshPlatform();
        }

        /// <summary>Use a project-owned system without taking ownership. Null restores automatic discovery.</summary>
        /// <remarks>设置事件系统。</remarks>
        public void SetEventSystem(EventSystem? eventSystem)
        {
            ThrowIfUnavailable();
            preferredEventSystem = eventSystem;
            RefreshPlatform();
        }

        /// <summary>Use an explicit UI camera. Null restores configured Overlay/MainCamera mode.</summary>
        /// <remarks>设置摄像机。</remarks>
        public void SetCamera(Camera? camera)
        {
            ThrowIfUnavailable();
            explicitCamera = camera;
            RefreshPlatform();
        }

        /// <summary>Refresh scene dependencies after a runtime camera or input-system change.</summary>
        /// <remarks>刷新平台。</remarks>
        public void RefreshPlatform()
        {
            ThrowIfUnavailable();
            if (!initialized && !initializing)
            {
                Initialize();
            }

            EnsureEventSystem();
            RefreshCamera();
        }

        /// <summary>订阅平台。</summary>
        void SubscribePlatform()
        {
            if (platformSubscribed)
            {
                return;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Application.quitting += OnApplicationQuitting;
            platformSubscribed = true;
        }

        /// <summary>取消订阅平台。</summary>
        void UnsubscribePlatform()
        {
            if (!platformSubscribed)
            {
                return;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Application.quitting -= OnApplicationQuitting;
            platformSubscribed = false;
        }

        /// <summary>响应场景已加载。</summary>
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!quitting && initialized)
            {
                RefreshPlatform();
            }
        }

        /// <summary>响应场景已卸载。</summary>
        void OnSceneUnloaded(Scene scene)
        {
            if (!quitting && initialized)
            {
                RefreshPlatform();
            }
        }

        /// <summary>响应活跃场景变化。</summary>
        void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (!quitting && initialized)
            {
                RefreshPlatform();
            }
        }

        /// <summary>响应应用正在退出。</summary>
        void OnApplicationQuitting()
        {
            quitting = true;
            UIDestructionObserver.StopForQuit();
            Shutdown();
        }

        /// <summary>在常规帧更新以后执行本组件的处理。</summary>
        void LateUpdate()
        {
            if (!initialized || quitting || destroyed)
            {
                return;
            } // A destroyed/disabled project system may disappear without a scene event.

            // Probe only on loss, not with a global Find every frame.
            if (!ReferenceEquals(currentEventSystem, null) && (currentEventSystem == null || !currentEventSystem.isActiveAndEnabled))
            {
                EnsureEventSystem();
            }

            if (rootCanvas != null && rootCanvas.renderMode == RenderMode.ScreenSpaceCamera && (rootCanvas.worldCamera == null || !rootCanvas.worldCamera.isActiveAndEnabled))
            {
                RefreshCamera();
            }
        }

        /// <summary>Close all views and release the old loader's leases before switching.</summary>
        /// <remarks>为页面和弹窗等视图提供资源加载器。</remarks>
        public void SetAssetLoader(IUIAssetLoader assetLoader)
        {
            ThrowIfUnavailable();
            if (assetLoader == null)
            {
                throw new ArgumentNullException(nameof(assetLoader));
            }

            RecreateManagers(assetLoader, clearBus: false);
        }

        /// <summary>清理当前缓存项。</summary>
        public void ClearCache() => factory?.ClearCache();
        /// <summary>重置全部。</summary>
        public void ResetAll()
        {
            if (destroyed || quitting)
            {
                return;
            }

            RecreateManagers(loader, clearBus: true);
        }

        /// <summary>重新创建管理器。</summary>
        void RecreateManagers(IUIAssetLoader assetLoader, bool clearBus)
        {
            if (rebuilding)
            {
                throw new InvalidOperationException("UI managers cannot be rebuilt from a view cleanup callback.");
            }

            rebuilding = true;
            try
            {
                int nextId = factory?.NextId ?? 1;
                StopBackRequests();
                RetireFactory(clearBus ? UICloseReason.Reset : UICloseReason.Disposed);
                if (destroyed || quitting)
                {
                    return;
                }

                if (clearBus)
                {
                    Bus.Clear();
                }

                loader = assetLoader;
                CreateManagers(nextId);
            }
            finally
            {
                rebuilding = false;
            }
        }

        /// <summary>创建管理器。</summary>
        void CreateManagers(int startId)
        {
            factory = new UIInstanceFactory(loader, layerRoots, startId, settings != null ? Math.Max(0, settings.maxCachedInstances) : 32, settings != null ? Math.Max(0, settings.maxCachedPerPath) : 2);
            Pages = new PageNavigator(factory);
            Popups = new PopupManager(factory);
            Panels = new PanelManager(factory);
            Overlays = new OverlayManager(factory);
            Toasts = new ToastManager(factory);
            Dialogs = new DialogManager(Popups);
            StopBackRequests();
            backLifetime = new CancellationTokenSource();
        }

        /// <summary>停止返回 Requests。</summary>
        void StopBackRequests()
        {
            CancellationTokenSource? lifetime = backLifetime;
            backLifetime = null;
            pendingBack = null;
            if (lifetime == null)
            {
                return;
            }

            try
            {
                lifetime.Cancel();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                lifetime.Dispose();
            }
        }

        /// <summary>关闭界面管理器。</summary>
        void Shutdown()
        {
            if (shuttingDown || (!initialized && !initializing && !shutdownCompletion.IsCompleted))
            {
                return;
            }

            shuttingDown = true;
            try
            {
                UnsubscribePlatform();
                StopBackRequests();
                var previousShutdown = shutdownCompletion;
                RetireFactory(UICloseReason.Disposed);
                if (!previousShutdown.IsCompleted)
                {
                    retiredFactories.Add(previousShutdown);
                }

                shutdownCompletion = Task.WhenAll(retiredFactories);
                retiredFactories.Clear();
                Bus.Clear();
                initialized = false;
                currentEventSystem = preferredEventSystem = null;
                if (ownedEventSystem != null)
                {
                    ownedEventSystem.SetActive(false);
                    Destroy(ownedEventSystem);
                    ownedEventSystem = null;
                }
            }
            finally
            {
                shuttingDown = false;
            }
        }

        /// <summary>终止使用工厂。</summary>
        void RetireFactory(UICloseReason reason)
        {
            retiredFactories.RemoveAll(task => task.IsCompleted);
            if (factory != null)
            {
                factory.Dispose(reason);
                var released = factory.DisposeAsync();
                if (!released.IsCompleted)
                {
                    retiredFactories.Add(released);
                }
            }
        }

        /// <summary>关闭所有窗口，等待未完成加载及原生对象销毁后释放的 prefab 租约。</summary>
        /// <remarks>关闭 UI，并返回可等待的资源清理任务。</remarks>
        public Task ShutdownAsync()
        {
            Shutdown();
            return shutdownCompletion;
        }

        /// <summary>响应退出。</summary>
        protected override void OnExit()
        {
            destroyed = true;
            Shutdown();
        }

        /// <summary>Consumes Back at the highest eligible layer, including a veto or active input gate.</summary>
        /// <remarks>处理返回。</remarks>
        public bool HandleBack()
        {
            if (destroyed || quitting || shuttingDown || rebuilding || !initialized)
            {
                return false;
            }

            if (pendingBack != null && !pendingBack.IsCompleted)
            {
                return true;
            }

            if (Overlays.IsBlocking)
            {
                return true;
            }

            UIHandle? popup = Popups.Peek();
            if (popup.HasValue)
            {
                if (popup.Value.View!.CloseGuard != null)
                {
                    StartBackConfirmation();
                }
                else
                {
                    Popups.CloseTop(UICloseReason.Back);
                }

                return true;
            }

            UIHandle? panel = Panels.PeekBackClosablePanel();
            if (panel.HasValue)
            {
                if (panel.Value.View!.CloseGuard != null)
                {
                    StartBackConfirmation();
                }
                else
                {
                    Panels.HideAnyBackClosablePanel();
                }

                return true;
            }

            if (Pages.Count > 1)
            {
                if (Pages.Peek()!.Value.View!.CloseGuard != null)
                {
                    StartBackConfirmation();
                }
                else
                {
                    Pages.Pop(UICloseReason.Back);
                }

                return true;
            }

            return false;
        }

        /// <summary>启动返回 Confirmation。</summary>
        void StartBackConfirmation() => _ = ObserveBackAsync(HandleBackAsync());
        /// <summary>异步观察返回。</summary>
        static async Task ObserveBackAsync(Task<bool> operation)
        {
            try
            {
                await operation;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        /// <summary>Repeated Back shares one attempt; cancellation only abandons that caller's wait.</summary>
        /// <remarks>异步处理一次返回操作，并报告是否有 UI 消费了它。</remarks>
        public Task<bool> HandleBackAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (destroyed || quitting || shuttingDown || rebuilding || !initialized || backLifetime == null)
            {
                return Task.FromResult(false);
            }

            BackRequest? request = pendingBack;
            if (request != null && !request.IsCompleted)
            {
                if (request.TryJoin(out BackWaiter? joined))
                {
                    return AwaitBackRequestAsync(request, joined!, cancellationToken);
                } // Last caller cancelled. Finish the old attempt before selecting a new top.

                return RetryAfterBackCleanupAsync(request, backLifetime, cancellationToken);
            }

            request = new BackRequest(backLifetime.Token, Dialogs);
            if (!request.TryJoin(out BackWaiter? waiter))
            {
                request.Finish(false, null);
                return Task.FromResult(false);
            }

            // Register the initiator and publish before invoking a user guard, which may reenter Back.
            pendingBack = request;
            Task<bool> waiting = AwaitBackRequestAsync(request, waiter!, cancellationToken);
            _ = CompleteBackAsync(request);
            return waiting;
        }

        /// <summary>异步等待返回请求。</summary>
        static async Task<bool> AwaitBackRequestAsync(BackRequest request, BackWaiter waiter, CancellationToken cancellationToken)
        {
            try
            {
                // Departure is immediate on cancellation, before another caller can join the old attempt.
                using (cancellationToken.Register(static state => ((BackWaiter)state!).Dispose(), waiter))
                {
                    return await UICancellation.WaitAsync(request.Completion.Task, cancellationToken);
                }
            }
            finally
            {
                waiter.Dispose();
            }
        }

        /// <summary>异步重试之后返回清理。</summary>
        async Task<bool> RetryAfterBackCleanupAsync(BackRequest previous, CancellationTokenSource owner, CancellationToken cancellationToken)
        {
            try
            {
                await UICancellation.WaitAsync(previous.Completion.Task, cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(owner, backLifetime))
            {
                return false;
            }

            return await HandleBackAsync(cancellationToken);
        }

        /// <summary>异步完成返回。</summary>
        async Task CompleteBackAsync(BackRequest request)
        {
            bool result = false;
            Exception? failure = null;
            try
            {
                request.Token.ThrowIfCancellationRequested();
                result = await HandleBackCoreAsync(request.Token);
            }
            catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                if (request.Token.IsCancellationRequested)
                {
                    // Wait only for framework-owned cancelled dialogs to finish their UI cleanup,
                    // not for arbitrary business guard tasks that may ignore cancellation forever.
                    try
                    {
                        await request.CancellationPropagation;
                        await request.Dialogs.DrainCancelledAsync(request.OwnerToken);
                    }
                    catch (OperationCanceledException) when (request.OwnerToken.IsCancellationRequested)
                    {
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                    }
                }

                request.Finish(result, failure);
            }
        }

        /// <summary>异步处理返回核心。</summary>
        async Task<bool> HandleBackCoreAsync(CancellationToken cancellationToken)
        {
            if (Overlays.IsBlocking)
            {
                return true;
            }

            if (Popups.Peek().HasValue)
            {
                await Popups.CloseTopAsync(UICloseReason.Back, cancellationToken);
                return true;
            }

            if (Panels.PeekBackClosablePanel().HasValue)
            {
                await Panels.HideAnyBackClosablePanelAsync(cancellationToken);
                return true;
            }

            if (Pages.Count > 1)
            {
                await Pages.PopAsync(UICloseReason.Back, cancellationToken);
                return true;
            }

            return false;
        }

        /// <summary>检查并确保事件系统可用。</summary>
        void EnsureEventSystem()
        {
            EventSystem? selected = null;
            if (preferredEventSystem != null && preferredEventSystem.isActiveAndEnabled && (ownedEventSystem == null || preferredEventSystem.gameObject != ownedEventSystem))
            {
                selected = preferredEventSystem;
            }

            if (selected == null)
            {
                foreach (EventSystem candidate in FindObjectsOfType<EventSystem>())
                {
                    if (candidate.isActiveAndEnabled && (ownedEventSystem == null || candidate.gameObject != ownedEventSystem))
                    {
                        selected = candidate;
                        break;
                    }
                }
            }

            if (selected != null)
            {
                // Never disable, destroy or reparent a project-owned EventSystem.
                if (ownedEventSystem != null)
                {
                    ownedEventSystem.SetActive(false);
                }

                currentEventSystem = selected;
                EventSystem.current = selected;
                warnedMissingInput = false;
                return;
            }

            bool create = settings == null || settings.autoCreateEventSystem;
            if (!SupportsLegacyInput)
            {
                if (create && !warnedMissingInput)
                {
                    Debug.LogWarning("[UI] Input System-only mode: provide a project EventSystem with its input module, then call SetEventSystem/RefreshPlatform.");
                    warnedMissingInput = true;
                }

                create = false;
            }

            if (!create)
            {
                if (ownedEventSystem != null)
                {
                    ownedEventSystem.SetActive(false);
                }

                currentEventSystem = null;
                return;
            }

            if (ownedEventSystem == null)
            {
                ownedEventSystem = new GameObject("UI EventSystem");
                ownedEventSystem.transform.SetParent(transform, false);
                ownedEventSystem.AddComponent<EventSystem>();
                ownedEventSystem.AddComponent<StandaloneInputModule>();
            }

            ownedEventSystem.SetActive(true);
            currentEventSystem = ownedEventSystem.GetComponent<EventSystem>();
            EventSystem.current = currentEventSystem;
        }

        static bool SupportsLegacyInput
        {
            get
            {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
                return false;
#else
                return true;
#endif
            }
        }

        /// <summary>刷新摄像机。</summary>
        void RefreshCamera()
        {
            if (rootCanvas == null)
            {
                return;
            }

            Camera? selected = explicitCamera;
            if (selected != null && !selected.isActiveAndEnabled)
            {
                selected = null;
            }

            if (selected == null && settings != null && settings.useMainCamera)
            {
                Camera? main = Camera.main;
                if (main != null && main.isActiveAndEnabled)
                {
                    selected = main;
                }
            }

            rootCanvas.renderMode = selected != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            rootCanvas.worldCamera = selected;
        }

        /// <summary>检查并确保画布层级可用。</summary>
        void EnsureCanvasHierarchy()
        {
            if (rootCanvas == null)
            {
                rootCanvas = GetComponentInChildren<Canvas>(true);
            }

            if (rootCanvas == null)
            {
                var canvasGo = new GameObject("UICanvasRoot", typeof(RectTransform));
                canvasGo.transform.SetParent(transform, false);
                rootCanvas = canvasGo.AddComponent<Canvas>();
            }

            CanvasScaler? scaler = rootCanvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = rootCanvas.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            if (rootCanvas.GetComponent<GraphicRaycaster>() == null)
            {
                rootCanvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            if (settings != null)
            {
                rootCanvas.planeDistance = Math.Max(0.01f, settings.canvasPlaneDistance);
                scaler.referenceResolution = new Vector2(Math.Max(1, settings.referenceWidth), Math.Max(1, settings.referenceHeight));
                scaler.screenMatchMode = settings.screenMatchMode;
                scaler.matchWidthOrHeight = Math.Max(0, Math.Min(1, settings.matchWidthOrHeight));
            }

            RefreshCamera();
            CreateOrUpdateLayer(rootCanvas.transform, UILayer.Background);
            CreateOrUpdateLayer(rootCanvas.transform, UILayer.Page);
            CreateOrUpdateLayer(rootCanvas.transform, UILayer.Panel);
            CreateOrUpdateLayer(rootCanvas.transform, UILayer.Popup);
            CreateOrUpdateLayer(rootCanvas.transform, UILayer.Toast);
            CreateOrUpdateLayer(rootCanvas.transform, UILayer.Overlay);
        }

        /// <summary>创建 Or 更新层级。</summary>
        void CreateOrUpdateLayer(Transform parent, UILayer layer)
        {
            string name = $"Layer_{layer}";
            Transform? target = parent.Find(name);
            if (target == null)
            {
                var go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
                target = go.transform;
            }

            Canvas? canvas = target.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = target.gameObject.AddComponent<Canvas>();
            }

            canvas.overrideSorting = true;
            // Keep serialized UILayer.Panel=25 compatible, but render ordinary panels below modal popups.
            int priority = layer == UILayer.Panel ? 15 : (int)layer;
            canvas.sortingOrder = priority * Math.Max(1, Math.Min(819, settings != null ? settings.sortingOrderStep : 100));
            if (target.GetComponent<GraphicRaycaster>() == null)
            {
                target.gameObject.AddComponent<GraphicRaycaster>();
            }

            RectTransform? rt = target.GetComponent<RectTransform>();
            if (rt == null)
            {
                rt = target.gameObject.AddComponent<RectTransform>();
            }

            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            layerRoots[layer] = target;
        }
    }
}

# UI、资源、网络与启动清理

这些接口面向 Unity 游戏，不包含业务协议、玩家数据或存档字段。配套 GameStarter 在 GameEntry 中接线，保留 StartupModule 与 GameSession。

## UI

现有 UILayer、页面返回栈、关闭确认及缓存继续使用。本次补齐以下行为：

- 非多实例窗口的重复打开可选择 UIDuplicatePolicy.Refresh、Focus、Reject。Refresh 保留原来的 OnOpen 行为；Focus 只聚焦，不重建本次打开的作用域；Reject 抛出异常。PopupOptions 与 PanelOptions 可配置。
- 页面 Push 支持 PageRepeatPolicy.Allow、ReuseTop、RefreshTop、RejectTop，只针对目标路径与栈顶相同的情况。默认 Allow 保持旧行为。
- 单例窗口按种类、层级和路径区分，避免相同 prefab 路径在不同管理器间误复用。重新聚焦弹窗会同步调整显示顺序、遮罩和返回栈。
- ICancellableUIAssetLoader 是可选扩展，逐次取消传到加载器；旧 IUIAssetLoader 继续兼容，迟到结果仍会释放。
- await UIManager.ShutdownAsync() 等待未完成获取和实例原生销毁后的租约释放；更换过加载器的旧工厂也纳入等待。不能在尚未完成关闭时重新初始化。

```csharp
await UIManager.Instance.Popups.OpenAsync("UI/Settings", options: new PopupOptions
{
    SingletonByPath = true,
    DuplicatePolicy = UIDuplicatePolicy.Focus
});
await UIManager.Instance.Pages.PushAsync("UI/Home", repeatPolicy: PageRepeatPolicy.ReuseTop);
```

缓存窗口关闭时，其 OnOpen 作用域结束；prefab 租约继续保留到缓存淘汰或实例销毁。窗口重新打开需要重新取得属于本次打开的动态资源。

## 资源

原 LoadAssetAsync/ReleaseHandle 仍是共享缓存接口。新的 AcquireAsync 每次持有独立 YooAsset 句柄，同地址的其他租约和共享缓存互不释放。

```csharp
using (var lease = await ResourceManager.Instance.AcquireAsync<TextAsset>("Config/Example", token))
{
    string json = lease.Asset.text;
}
```

资源使用时间超过这个方法时，应保持租约，不要在使用者结束之前 Dispose。只有资源获取成功才返回租约；取消或失败不会要求调用方释放未取得的对象。

取消只停止这个调用方的等待。原生 I/O 不能强制中断时，管理器保留底层句柄到原操作完成再释放，不把迟到结果交给已关闭的使用者。ShutdownAsync 会使现有租约失效，并等待未结束的原生获取释放。

ResourceScope 管理一组获取。在 UIView 的生命周期回调中可用 `Track(ResourceManager.Instance.CreateScope())` 把它交给窗口本次打开的作用域；窗口关闭时取消未完成获取并逆序释放。CreateSceneScope(scene) 将作用域绑定到具体已加载场景，卸载时自动释放，其他场景和全局作用域不受影响。

```csharp
using var scope = ResourceManager.Instance.CreateSceneScope(scene);
var texture = await scope.AcquireAsync<Texture2D>("Textures/Example", token);
// texture.Asset 保持有效，直到租约、scope 或资源管理器被关闭。
```

GameStarter 开启 Initialize Yoo Asset 后，会安装使用这些租约的 UI 加载器；配置文本读取也使用短期独立租约，不再占用共享缓存。UI 包与 YooAsset 包之间没有增加强依赖，适配接线在 GameStarter。

## 网络

StateChanged 按派发队列顺序提供连接、鉴权、重连与断开状态。Unity 默认自动派发；手动宿主继续调用 Update。Dispose 后停止回调，可读取最终 State。

ConnectAsync(token) 的 token 仅取消该调用方的等待。多个调用方共用连接流程，取消其中一个不会断开其他人。需要停止连接及自动重连时，调用并等待 DisconnectAsync；应用退出时 DisposeAsync。

请求超时覆盖发送等待与响应等待，取消或超时会移除 pending 项，迟到响应不能完成后续请求。已发出的数据无法撤回；框架不自动重放业务请求。断线按现有配置进行退避和重连次数限制，鉴权可通过 ConnectionRejectedException 明确终止重连。

退出时取消回调或断开失败不能阻止传输层释放；DisposeAsync 最后聚合报告清理错误，事件处理器错误通过 Error 通知。

## GameEntry

初始化顺序仍在 InitializeManagersAsync 中显式编写。初始化或取得本次拥有的服务时登记释放操作；资源初始化中途失败也获得清理机会。已由外部初始化的服务只借用，不由入口销毁。

关闭、模块初始化失败、公共 Manager 失败或模块 Tick 失败时：先停止并清理 StartupModule，再按相反顺序释放本入口拥有的网络、ECS、GPU、UI、配置、音频、场景、存储、数据和资源。某一步失败记录日志，继续后续清理。

await GameEntry.ShutdownAsync() 可以等待完整清理。UI 与 ECS/GPU 原生对象销毁完成前保留依赖资源；新入口等待上一入口的异步清理，防止旧清理污染新资源。Framework 的线程、事件和时钟驱动由 Framework 持有，入口不重复创建或停止它们。

主动退出游戏时，应先等待 ShutdownAsync，再调用 Application.Quit。Unity 的 OnDestroy/OnApplicationQuit 只能发起异步清理，不能阻塞等待后续帧；强制结束进程不能保证执行异步退出流程。

本次改动涉及 AOT 公开接口；使用 HybridCLR 的已有游戏需要重新构建主包，不能只导出热更 DLL。原生 Unity 导入、实际场景/音频和 IL2CPP 仍需在目标工程验收。

场景加载器解绑或 Play 重置不会提前释放仍被已加载场景使用的 YooAsset 句柄。YooAsset 自身按 sceneUnloaded 自动释放；关闭过程中迟到但成功的场景加载也遵守相同规则。解绑管理器不等于卸载场景，项目仍需显式管理场景切换。

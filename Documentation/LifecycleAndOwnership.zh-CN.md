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

GameStarter 开启 Initialize Yoo Asset 后，会安装使用这些租约的 UI 加载器；Luban 清单和数据表的字节读取也使用短期独立租约，不再占用共享缓存。UI 包与 YooAsset 包之间没有增加强依赖，适配接线在 GameStarter。

## 网络

StateChanged 按派发队列顺序提供连接、鉴权、重连与断开状态。Unity 默认自动派发；手动宿主继续调用 Update。Dispose 后停止回调，可读取最终 State。

ConnectAsync(token) 的 token 仅取消该调用方的等待。多个调用方共用连接流程，取消其中一个不会断开其他人。需要停止连接及自动重连时，调用并等待 DisconnectAsync；应用退出时 DisposeAsync。

请求超时覆盖发送等待与响应等待，取消或超时会移除 pending 项，迟到响应不能完成后续请求。已发出的数据无法撤回；框架不自动重放业务请求。断线按现有配置进行退避和重连次数限制，鉴权可通过 ConnectionRejectedException 明确终止重连。

退出时取消回调或断开失败不能阻止传输层释放；DisposeAsync 最后聚合报告清理错误，事件处理器错误通过 Error 通知。

## GameEntry

GameEntry 默认先准备资源并加载热更，公共 Manager 由 HotGame.InitializeManagersAsync 直接初始化；关闭热更时使用 GameEntry 的同名方法。OnExit 停止启动模块和热更入口，对应入口逐项调用 Release。StartupModule 与 GameSession 保留；入口不再记录 Manager 所有权，不区分借用实例，不执行跨 Manager 的初始化失败回滚。

MonoSingleton 提供成对的 OnStart/OnExit，每次运行各调用一次。重复访问 Instance 不重复启动；Dispose、Release、原生销毁和退出回调共用同一清理钩子。重复清理、部分初始化资源清理和错误记录在组件内部处理。Release 只操作已有实例，退出时不创建新 Manager；ConfigurableSingleton.Release 保留既有退出保护。

GameEntry.ShutdownAsync 保留兼容入口，发起 OnExit 后返回；不再汇总等待所有 Manager 的异步清理。UI 的窗口销毁和 prefab 租约释放、ResourceManager 的迟到获取及句柄释放均在各包内部继续完成。资源管理器在关闭期间收到 InitializeAsync 时，内部等待上次关闭；业务入口不维护清理任务队列。需要显式等待某个服务完成时，使用它自己的 ShutdownAsync。

Framework 的线程、事件和时钟驱动仍由 Framework 管理。

本次改动涉及 AOT 公开接口；使用 HybridCLR 的已有游戏需要重新构建主包，不能只导出热更 DLL。原生 Unity 导入、实际场景/音频和 IL2CPP 仍需在目标工程验收。

场景加载器解绑或 Play 重置不会提前释放仍被已加载场景使用的 YooAsset 句柄。YooAsset 自身按 sceneUnloaded 自动释放；关闭过程中迟到但成功的场景加载也遵守相同规则。解绑管理器不等于卸载场景，项目仍需显式管理场景切换。

原生实例持有的资源可以通过 AcquireAsync<T>(..., keepAliveUntilDisposed: true) 声明：普通关闭会等待使用方 Dispose 租约后再释放句柄。GameStarter 的 UI 适配已使用此策略，窗口原生销毁之前 prefab 句柄保持有效。未完成获取会被正常取消，Play 重置仍可强制回收旧运行的租约。

## 单例自动登记和静态声明恢复

三种单例基类共享 SingletonLifecycle，自动登记实例释放及引用重置。GameEntry 先关闭项目模块，ECS 自己维护 World → Entity → Pool 的清理顺序；Data/Input 等普通服务依靠基类及 IDisposable，无需逐类 Unity 启动特性。登记表在关闭 Domain Reload 的多次 Play 之间持续有效，清理不创建未使用的单例。

Framework 内置 Source Generator 从 partial 单例子类的声明生成静态字段恢复，不需要手写字段赋值。关闭 Domain Reload 时，统一启动入口先清理旧实例，再执行生成代码；开启时不重复执行。Player 不生成编辑器静态重置代码。

关闭 Scene Reload 不意味着要覆盖实例配置。音频/UI/GPU 使用 OnExit 释放运行资源、OnStart 重新启动；音量、速度、自定义加载器等实例配置保留。场景共享操作锁、ECS 组件类型 ID 等基础设施按原有生命周期约定处理。

完整规则见 [StaticReset.md](../Packages/com.novorizon.framework/Documentation~/StaticReset.md)。

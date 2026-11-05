# Novorizon UI Framework

面向 Unity 2022.3 的独立 UI 模块，提供页面、弹窗、面板、状态层和短提示的生命周期管理。

## 安装与依赖

目标版本为 Unity 2022.3 LTS，使用 uGUI，包名为 `com.novorizon.framework.ui`。在 Package Manager 选择 **Add package from disk**，打开本目录的 `package.json`；也可在项目的 `Packages/manifest.json` 添加本地包路径：

```json
"com.novorizon.framework.ui": "file:/绝对路径/Framework/Framework/UI"
```

Git UPM 安装使用仓库 URL 加 `?path=/Framework/UI#<版本或提交>`，其中仓库内路径区分大小写。已有项目若使用旧包名 `com.yourcompany.ui-framework-pro-skeleton`，应先移除旧 manifest 项再添加新包，避免同一组脚本被导入两次。运行时程序集为 `Game.Framework.UI`，业务命名空间继续使用 `UI`；自定义业务 asmdef 需显式引用该程序集及所用的 Game.Framework.Core / Game.Framework.Unity。此包独立发布，依赖匹配版本的 `com.novorizon.framework` 与 `com.unity.ugui`，复用单例和局部消息基础能力；不强制安装 YooAsset 或 TextMeshPro。可通过 `IUIAssetLoader` 接入项目自己的资源适配器。

编辑器工具只编入 `Game.Framework.UI.Editor`，菜单位于 **Tools / Novorizon / UI**。安装包不会自动创建场景对象；需要时通过 `UIManager.Instance` 或场景中的 `UIBootstrap` 初始化。

在 Package Manager 的 Samples 中导入 **UI Demo**，点击 **Tools / Novorizon / Demos / Create UI Demo Scene** 后 Play。该演示在独立场景中运行时创建窗口模板、按钮与列表，使用内存 IUIAssetLoader，需先安装主框架，不需要手工绑定 Prefab 或安装资源库。操作与清理见 [UI Demo README](Samples~/Demo/README.md)。

**UI Categories** 保留为最小业务脚本，需要自己制作并绑定 Prefab，不与完整 Demo 混用。两组样例源码均位于 `Samples~`，安装包时不参与项目编译，Import 后由独立程序集编译。SimpleToast 使用 uGUI Text，可按项目需求替换。Windows、Android、iPhone 的触摸、返回键与渲染效果需在目标设备上验证。

## 分类与管理器（哪一类用哪些接口/入口）
- **Page**：`UIPage : UIView` + `PageNavigator`
  入口：`UIManager.Instance.Pages.PushAsync/ReplaceAsync/Pop/ResetToAsync`
  原因：需要“返回路径”历史 → **栈**
- **Popup**：`UIPopup : UIView` + `PopupManager`
  入口：`UIManager.Instance.Popups.OpenAsync/CloseTop/Close`
  特性：`PopupOptions.Modal` 创建遮罩；返回键先关最上层
- **Panel（侧边栏/非模态面板）**：`UIPanel : UIView` + `PanelManager`
  入口：`ShowAsync/Hide/ToggleAsync`（多数是开关状态，不进 page 栈）
  可选：`GroupId` → `PushInGroupAsync/PopGroup` 做“面板内深入”栈
- **Overlay（Loading/Blocker 等状态层）**：`UIOverlay : UIView` + `OverlayManager`
  入口：`using var token = await Overlays.ShowBlockingAsync(path);`
  原因：它是“状态”不是“路径” → **token/引用计数** 防止并发提前关闭
- **Toast（短提示）**：`UIToast : UIView` + `ToastManager`
  入口：`Toasts.Enqueue(path, args)`
  原因：短暂提示，不需要返回 → **队列**

## Back 优先级（标准建议）
`UIManager.Instance.HandleBack()` / `HandleBackAsync()`：
1) 有活动或正在加载的阻塞 Overlay 时消耗返回操作，不关闭下层
2) 请求关闭最上层 Popup；顶层拒绝关闭时也不穿透下层
3) 请求隐藏任意 HideOnBack 的 Panel
4) Pop Page（如果栈深度>1）
5) 没有目标时返回 false（交给游戏退出逻辑）

同步返回入口也会启动异步关闭确认；需要等待结束时使用 `await HandleBackAsync()`。待处理的返回操作会合并，避免连续按键弹出多份确认。

## 快速使用
```csharp
await UI.UIManager.Instance.Pages.ResetToAsync("UI/Pages/MainMenuPage");
await UI.UIManager.Instance.Popups.OpenAsync("UI/Popups/SettingsPopup");
UI.UIManager.Instance.Toasts.Enqueue("UI/Toasts/SimpleToast", "Saved!");
```


## 生命周期与资源所有权

0.5 增加本次打开作用域、显示/隐藏通知、可取消业务入口、异步关闭确认、类型化弹窗结果、安全区与虚拟列表。完整代码与资源适配说明见 [业务功能用法](Documentation~/Features.md)。

所有 UI 接口须从 Unity 主线程调用。`OnCreate` 每个实例只调用一次；`OnOpen` 每次打开调用，活动单例再次打开也会更新参数；`OnClose` 每次打开至多调用一次；`OnDestroyed` 在原生视图真正销毁时调用。业务清理应覆盖 `OnDestroyed`，不要自行定义 Unity `OnDestroy` 消息；需要管理器恢复栈或队列时，应通过管理器关闭，不要只调用 `SetActive(false)`。

`OpenToken` 与 `Track(subscription)` 绑定本次打开；关闭或活动单例刷新时取消任务并退订，缓存重开创建新作用域。`DestroyToken` 与 `TrackUntilDestroyed` 绑定整个实例，关闭进缓存不会取消。异步业务开始时捕获令牌，并在 await 后检查；不要在旧异步回调中重新读取一个可能属于新一轮打开的 OpenToken。`OnShow/OnHide` 表示框架管理的显示/隐藏，页面被下一页覆盖不会因此关闭或取消 OpenToken。

`CanClose` 控制立即允许关闭，`CloseGuard` 提供可等待的确认；直接关闭带 Guard 的窗口时应使用对应的 `CloseAsync/PopAsync/HideAsync`。Reset、销毁、取消未完成的打开与 Dialog 的显式结果完成属于强制结束，保证清理；它们不被普通关闭拦截阻塞。`LastCloseReason` 保留本次真正关闭的原因。

`UIHandle` 表示一次活动打开。缓存复用会生成新的 `Id`；旧句柄的 `IsValid` 为 false，旧句柄不能关闭后续打开的视图。页面历史中的重复路径会使用独立实例，避免一份视图对应多份历史。`CacheOnClose=false` 会实际销毁实例。

默认缓存最多 **每路径 2 个、总计 32 个**；超过上限时销毁本次归还实例。`UISettings.maxCachedInstances/maxCachedPerPath` 可配置容量，零表示禁用。`SetSettings` 立即更新限额，调小容量会释放已有缓存；活动视图仍由管理器持有。

```csharp
var root = UI.UIManager.Instance;
root.ClearCache();       // 功能结束时释放失活缓存
root.Toasts.Clear();     // 清空提示队列并结束当前提示，取消尚未完成的打开
root.ResetAll();         // 场景/功能切换：关闭所有 UI、清空消息订阅，重建空管理器
```

根对象默认跨场景保留，因此场景或功能切换时由业务明确调用 `ResetAll`。`SetAssetLoader` 会关闭旧管理器与所有旧视图，再创建新管理器；旧管理器引用随之失效，业务应重新从根获取。根销毁也执行相同资源清理，并销毁由根创建的 EventSystem。不要在视图的清理回调中再次调用 `ResetAll/SetAssetLoader`；这种重入会抛出 `InvalidOperationException`，以免覆盖中间管理器。

打开视图要求父 UI 层在层级中激活；若整个根或对应层失活，打开会抛出 `InvalidOperationException`，不会偷偷激活隐藏的 UI。异步加载结束后也会复查父层，拒绝打开时归还已取得但未实例化的资源。视图通过失活临时节点创建，工厂登记所有权后再激活。回调若已经关闭或重开视图，原来的 Open 调用返回无效句柄，回调中新取得的句柄独立有效。

`IUIAssetLoader` 的每次成功 `LoadPrefabAsync` 都代表一份资源租约；工厂缓存期间保留它，在对应实例的 Unity `OnDestroy` 后调用一次 `ReleasePrefab`。共享同一个 prefab 时，适配器须对每次获取计数，不能因一份实例关闭而提前卸载其他视图依赖的 prefab。工厂不拥有 loader 对象本身，也不会 Dispose 项目共享的资源系统。Unity 的 `Destroy` 在帧末完成，资源释放也会等到原生销毁。

从未激活的 MonoBehaviour 可能没有 `OnDestroy` 通知。集中销毁观察器为这类中止路径提供补偿：仅在存在待销毁对象时启用，确认 GameObject 真正销毁后幂等归还租约，不按每个视图逐帧轮询，也不在发送 Destroy 请求时提前释放。

退出阶段不会再为观察器创建新对象。若引擎不再执行后续帧，不能保证晚于最后一次检查销毁的对象还有机会执行托管归还回调；关闭 Domain Reload 后，下次 Play 初始化会清扫已经销毁的遗留对象。不要依赖应用退出之后继续执行业务清理。

界面窗口的 Prefab 通过 `IUIAssetLoader` 访问，UI 包不引用具体资源管理库。默认 loader 使用 `Resources.LoadAsync<GameObject>`，也可实现接口或使用 `DelegateUIAssetLoader` 接入游戏自己的加载/归还函数。它不会在一次关闭时卸载 Resources 共享资产；`ClearCache` 释放实例，Resources 资产的全局卸载由业务统一安排。异步加载不能直接中止的 loader，在 UI 已取消或销毁后仍可能完成底层读取，工厂会归还迟到的租约，不会创建视图。业务管理器的打开接口均可传 `cancellationToken`，调用方取消抛出 `OperationCanceledException`；底层工厂保留取消时返回无效句柄的兼容行为。

YooAsset **2.3.18 为可选依赖**。如需适配，应通过 `IUIAssetLoader` 持有专用 `AssetHandle`，每次成功加载都有对应释放；也可以自行维护有明确使用者计数的共享租约。主仓库已有的 `ResourceManager.ReleaseHandle(location)` 会释放该 location 的共享缓存，不应简单地在任意 UI 的 `ReleasePrefab` 中调用它。UI 包不自动初始化、更新或销毁 YooAsset 包。

## 管理器行为

- `Pages.PushAsync/ReplaceAsync/ResetToAsync` 按调用顺序完成，加载失败保留旧页面。导航尚未完成时，同步 `Pop` 返回 false。
- `Panels.Hide(handle)` 关闭指定实例，适用于 `AllowMultiple=true`；`Hide(path)` 关闭最近打开的同路径实例。组面板关闭顶层后恢复上一层。
- 相同路径的并发 `Overlays.ShowBlockingAsync` 共享一次加载、首次参数和引用计数；最后一个 token 释放后关闭。仍持有 token 时请求不同路径会抛出 `InvalidOperationException`。
- Toast 在 `Complete`、关闭或外部销毁时推进队列，等待期间不逐帧轮询；`Dispose` 后不能继续入队。`ToastOptions.MergeKey` 保留兼容，目前不合并提示。
- 缓存弹窗重新打开时会创建并绑定新的模态遮罩。未引入遮罩池，避免在没有 Profiler 数据的情况下增加额外状态。
- Page/Panel 的业务 OnClose 异常仍向调用方抛出，但内部会恢复当前栈顶；Toast 单条异常记录后继续后续提示。外部 Destroy 弹窗会立即注销句柄，不必调用 Count/Peek 触发清理。

## 输入、相机与层级

0.4 默认使用 ScreenSpaceOverlay，避免依赖每个场景的主相机。需要相机 UI 时集中配置一次：

```csharp
var root = UI.UIManager.Instance;
root.SetCamera(uiCamera);              // 项目专用 UI Camera
root.SetEventSystem(projectInput);    // 可选：项目已有的 EventSystem
// 运行期切换相机/输入系统后，可显式刷新；场景加载/卸载会自动刷新。
root.RefreshPlatform();
```

`SetCamera(null)` 恢复 UISettings 策略：`useMainCamera=true` 时使用当前主相机，否则 Overlay。旧版自动使用 Camera.main 的项目，应显式启用该选项或设置 UI Camera。`SetEventSystem(null)` 恢复自动发现。项目自有 EventSystem 的销毁和重复管理由项目负责，框架不会改动其激活状态或所有权；发现项目系统时仅停用自建备用系统，项目系统离开后可以恢复备用系统。

`UISettings.autoCreateEventSystem=false` 可禁用备用系统。仅启用新 Input System 的项目必须自行提供配置好 InputSystemUIInputModule 的 EventSystem；UI 包不会强制引入新输入包，也不会在这种模式下创建不兼容的 StandaloneInputModule。应用退出后不要再通过 Instance 获取 UI；库会拒绝重建。

默认层级为 Background → Page → Panel → Popup → Toast → Overlay。Panel 的公开枚举数值仍为 25，渲染时使用 15 的优先级，避免改变旧序列化数据；业务不能再假定排序值总是枚举值乘 step。模态遮罩与弹窗同处 Popup 层，覆盖普通 Page/Panel。`ShowBlockingAsync` 管理 Overlay 的租约与引用计数；Overlay Prefab 还需配置覆盖屏幕、可接收射线的 Image 等组件，才能实际阻止底层输入。

## 局部消息

`UIMessageBus` 可供 UI 包内部使用；业务领域事件可继续订阅主框架 EventManager。两者分别拥有订阅，不会互相转发。UIMessageBus 只从主线程调用，订阅/退订时更新快照，稳定订阅下 Publish 不创建数组或装箱值类型消息。

保留原快照规则：回调中退订或 Clear 影响之后的发布，当前已取得的快照仍按原顺序执行；嵌套 Publish 使用更新后的订阅。单个回调抛异常会向发布者传播并中断本次发布，业务应处理自己的回调异常。页面关闭时应退订，缓存不会自动解除业务订阅。

在页面中可用 `Track(root.Bus.SubscribeScoped<Message>(handler))` 自动拥有和退订。Scoped 注册各自独立，Dispose 后也会跳过当前快照中尚未执行的该回调；原 Subscribe 保留原有规则。实例长期订阅可用 TrackUntilDestroyed。

## 验证范围

仓库中的 [.NET 合同回归与复现说明](../../tests/Game.Framework.UI.Tests/README.md) 验证缓存数量、租约配对和异步生命周期；[同工作量前后对比](../../docs/ui-lifecycle-validation.md) 记录可复现的对象数量结果。合同桩不会随 UPM 包导入，且不能替代 Unity 编辑器、Profiler 或设备验证。

## 0.7 迁移

UI 总入口从 `UIRoot` 改名为 `UIManager`，调用 `UIManager.Instance.Initialize()`。原脚本 GUID 保留，并提供 Unity MovedFrom 标记；项目源码中的旧类型名需要更新。Canvas 和层级节点仍由该入口管理，没有增加额外转发层。

# Novorizon Diagnostics

`Tools/Novorizon/Diagnostics` 打开只读 Editor 窗口，支持过滤、暂停、手动采样和 JSON 快照导出。窗口打开时每 0.5 秒采样，关闭后解除 update 订阅；Player 不编入这个包的代码。匹配结果按每页最多 100 条记录展示，用 Previous / Next 翻页并显示页数和总匹配数量。租约保留两行信息与创建栈悬停提示。采样、搜索、比较模式或基线改变时才重建匹配缓存，普通重绘和翻页只绘制当前页；大量快照的数据采样、过滤与差异计算仍与记录数有关。

读取已加载包的公开 API，通过 TryGetInstance 取得现有服务，不调用 Instance，不启动或重建已释放服务。未安装、未加载、未创建或计数读取失败会显示状态，不中断其他计数。

覆盖主线程待派发、定时器、网络状态/待响应/待发、场景数量、活动/缓存 UI、页面/弹窗数量、阻塞层、音源和音源池、资源管理器独立获取数、GPU 播放器数量及当前语言。资源 OwnedAssetCount 包括管理器的独立获取及等待原生加载结束后释放的记录，不是地址去重数量、显存或全部资源引用数；GameObjectPool 汇总当前登记池数与活动/缓存实例数；ReferencePool 显示已经创建的共享池类型数，不访问 Shared 创建新池。

模块由诊断侧通过公开接口适配；Framework、UI 和资源包不反向依赖诊断包。不扫描私有字段，不提供强制清理按钮。

回归覆盖未创建服务不被构造、嵌套计数及异常隔离；窗口代码另外核对真实 Unity Editor DLL 的编译签名。实际窗口交互需在持有许可证的 Editor 验收。

## 资源归属与快照差异

资源包的 `CaptureLeaseDiagnostics()` 提供不可变标量记录，面板只通过现有实例的公开 API 采样。每条独立获取有 manager session/lease ID、地址、类型、owner 标签、scope ID、可选场景信息和 Loading/Active/Releasing 状态。取消等待后仍在进行的原生 I/O 保留为 Releasing，管理器结束释放流程后才移除；若服务实例已释放/重建，面板只观察当前实例，移除差异不证明旧实例原生工作已经结束。普通共享 `LoadAssetAsync` 缓存没有独立消费者语义，因此不伪装成租约归属；现有总量指标与这张表分开看。

项目为窗口、功能或场景指定标签，不传入/扫描拥有者对象：

```csharp
var scope = ResourceManager.Instance.CreateScope("UI/Inventory");
var icon = await scope.AcquireAsync<Sprite>("icons/sword", token);
// 将 scope Track 到窗口的本次打开；场景可用 CreateSceneScope(scene, "Battle/HUD")。
// 独立获取可用 AcquireOwnedAsync<Sprite>("icons/sword", owner: "Inventory/Preview")。
```

scope 及 lease 仍由项目按现有规则 Dispose；面板不释放资源，不创建服务，也不保存拥有者对象引用。无标签的独立获取明确显示 Unassigned；空 scope 不会被当作仍占用资源。记录数量不是显存、字节数或 YooAsset 原生引用计数。

用 Set baseline 保存当前快照，Compare baseline 查看 Added/Changed/Removed 变化，Clear baseline 清除比较。过滤可按 owner、场景、资源地址或状态搜索。Export JSON 同时导出完整的当前、基线和差异，不受过滤或当前页限制；按 session + lease ID 区分管理器重建前后的获取，不会因同地址或复用 ID 混淆。

创建堆栈默认关闭。项目可在 Editor/Development Build 中、获取前设置 `resources.CaptureLeaseCreationStacks = true`，仅之后的获取捕获，单条最多 8192 字符；正式非开发 Player 不捕获。关闭时不创建 StackTrace；没有历史记录表，记录跟随现有 ownedAssets 移除。面板中悬停记录可查看已捕获堆栈，JSON 含完整快照值；堆栈可能带项目路径，导出分享范围由项目决定。

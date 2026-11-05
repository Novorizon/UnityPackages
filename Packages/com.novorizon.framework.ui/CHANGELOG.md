# Changelog

## 0.5.1

- 增加可运行 UI Demo：通过 Editor 菜单生成独立场景，运行时创建全部模板与按钮。
- 内存资源接口演示即时/延迟加载、逐租约退休与归还；不引入具体资源库依赖。
- 分开完整 Demo 与需要手工绑定的 UI Categories，补充导入、运行、清理与验证说明。

## 0.5.0

- 保持 `IUIAssetLoader` 为资源边界，增加 `DelegateUIAssetLoader`，UI 不新增 YooAsset、Addressables 或主框架依赖。
- UIView 增加本次打开/实例生命周期取消令牌和订阅收集、显示/隐藏通知、关闭原因与异步关闭确认。
- 业务导航/弹窗/面板/Overlay 接口贯通可选 CancellationToken；取消不影响其他并发使用者，也不误关旧调用已复用的新句柄。
- 返回操作共享待处理确认，尊重顶层关闭拦截；Overlay 加载时即阻断下层返回。重置和销毁仍强制清理。
- 新增 DialogManager 与 UIResultPopup<TResult>，等待选择值或关闭原因，并隔离旧异步完成回调。
- 新增 UISafeArea，按屏幕安全区投影内容容器，支持尺寸/方向与相机 viewport 变化，稳定帧不写布局。
- 新增固定尺寸单列 UIVirtualList，按可见窗口复用，支持两种方向、解绑和数据刷新，不按数据总数创建条目。
- UIMessageBus 增加独立拥有的 SubscribeScoped，便于 UIView.Track 自动退订；原 Subscribe/Publish 快照规则保留。
- 补充 ConfirmPopup、ScopedPageExample、VirtualListExample 按需导入示例与回归测试。

## 0.4.0

- 将 UI 专项合入 `codex/tcp-client-integration`，继续作为仅依赖 uGUI 的独立可选包导出。
- 失活临时节点下创建视图，登记资源所有权后才激活；回调关闭或重开后，原调用不会再认领新句柄。
- 打开时要求父层有效且激活，异步加载完成后再次检查；增加原生销毁观察补偿，避免未激活实例漏归还资源，保持帧末销毁后才释放租约。
- 场景切换刷新输入与相机依赖；框架仅协调自建 EventSystem，不销毁项目自有输入对象。默认使用 Overlay，可指定专用 UI 相机。
- 保留 `UILayer.Panel=25` 的序列化值，渲染优先级改为 15，使普通面板位于模态弹窗与遮罩下方。
- 退出期间禁止重新创建 UIManager；支持显式设置项目 EventSystem、相机和刷新场景依赖。
- Page/Panel 关闭异常仍恢复当前下层，Toast 单条关闭异常不阻塞后续队列；弹窗外部销毁立即移除登记。
- UIMessageBus 在订阅变化时更新类型化快照，Publish 不再复制订阅数组，保留当前发布与重入时的快照语义。

## 0.3.0

- 将 UI 整理为独立 `com.novorizon.framework.ui` 包，面向 Unity 2022.3；保留 `UI` 命名空间，示例按需导入。
- 按 prefab 路径缓存多个视图，默认每路径 2 个、合计 32 个；超过容量时销毁归还的实例。
- 每次重新打开缓存视图生成新句柄；重复关闭和过期句柄不会关闭后续打开的实例。
- 工厂统一释放活动视图、缓存、遮罩与资源租约；切换 loader、根销毁和加载中取消均有清理路径。
- Resources 使用真实异步请求；Toast 通过完成通知推进队列，支持 Clear/Dispose。
- 页面及组面板异步导航按请求顺序处理；多实例面板支持按句柄关闭，阻塞 Overlay 共享并发加载和 token。

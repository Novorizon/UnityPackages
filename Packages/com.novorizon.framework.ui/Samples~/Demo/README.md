# 可运行 UI Demo

适用 Unity 2022.3，依赖 `com.novorizon.framework.ui`、Framework、uGUI 与 TextMeshPro。无需 YooAsset、Addressables、DOTween、外部 Prefab 或服务器。

## 开始

1. 在 Package Manager 找到 **Novorizon Framework UI**，导入 **UI Demo** 样例。UI 包会安装 TextMeshPro；新工程先使用 **Window > TextMeshPro > Import TMP Essential Resources** 导入标准资源，并确认项目的 TMP Settings 已指定默认 TMP Font Asset。
2. 点击菜单 **Tools > Novorizon > Demos > Create UI Demo Scene**。编辑器会先询问如何处理当前未保存场景；取消保存流程就不会创建 Demo。每次使用新场景路径，不覆盖已有资源。
3. 打开生成的 `Assets/NovorizonDemos/UIDemo*.unity`，进入 Play。脚本会创建全部布局、模板与输入系统。
4. Game 视图建议先选 16:9 或手机竖屏。面板中的全部按钮都可直接操作。

场景创建菜单和 launcher 都会预先检查 TMP 默认字体；缺少设置时显示明确错误，不创建部分 UI 或静默显示空白文字。示例文本使用项目的 `TMP_Settings.defaultFontAsset`，不加载 LegacyRuntime.ttf。默认标准字体适合本示例的英文文案，业务中的中文/日文等需使用覆盖相应字形的 TMP Font Asset，并配置 fallback。

也可在自己的**空场景**创建一个 GameObject 并挂 `UIDemoLauncher`。已有 UIManager 或 EventSystem 时会拒绝启动并在 Console 说明原因；Demo 不会接管其他游戏的窗口或输入对象。不要把 launcher 挂到正式启动场景。

旧输入系统由 Demo 自建 StandaloneInputModule；仅启用新 Input System 的项目通过反射使用已安装的 InputSystemUIInputModule 与默认动作，不给 UI 包增加新依赖。若 Console 提示缺少对应输入模块，检查项目自己的 Input System 安装与 Player Settings。

## 按钮与预期

| 操作 | 可观察的结果 |
| --- | --- |
| 10,000-item inventory | 打开第二个 Page，主页隐藏；滚动仅保留可见窗口和 overscan 的条目，顶部显示实际实例数。 |
| Inventory 的 Mark unsaved / Back | 返回会弹确认；No 保留页面，Yes 关闭并恢复主页。 |
| Modal popup | 遮罩阻挡下层，点击外面或 Close 关闭；它位于普通 Panel 上方。 |
| Toggle panel | 独立显示/隐藏非模态面板，不改变 Page 历史。 |
| Await dialog result | Yes/No 是有值结果，遮罩关闭是无值结果；结果显示在主页状态区。 |
| Overlay + cancel | 显示四秒 Loading；Back 被门禁阻挡，Overlay 内 Cancel 只取消该所有者。 |
| Toast queue | 顺序显示短提示。 |
| Publish local pulse | 仍打开的页面收到消息；关闭页面的 Track 订阅会自动释放。 |
| Switch resource loader | 在即时与 400 ms 延迟的内存加载器之间切换。旧窗口关闭，迟到加载/原生销毁后租约仍归还给旧加载器。 |
| Reset owned UI | 清理 Demo 自己的 UI 并重建主页。 |

主页和列表的交互区域使用 UISafeArea；背景保持全屏。刘海/底部手势区应在有真实安全区的手机或 Device Simulator 中验证，普通桌面窗口的 safeArea 通常等于全屏。

## 代码入口

- `UIDemoLauncher`：启动、按钮业务动作、自己拥有的 Root/输入/配置与加载器清理。
- `UIDemoAssetLoader`：最小 `IUIAssetLoader` 实现。每次获取计租约，Retire 后拒绝新加载，等实际视图归还全部租约才销毁运行时模板。
- `UIDemoHomePage`：OpenToken、Track、显示/隐藏、订阅与状态展示。
- `UIDemoInventoryPage`：虚拟列表与异步关闭确认。
- `UIDemoConfirmPopup`：`UIResultPopup<bool>` 与类型化结果。
- `UIDemoWidgets`：手工创建的简化布局。业务项目可替换成美术制作的 Prefab。

示例文字统一使用 `TextMeshProUGUI`，业务字段使用 `TMP_Text`；固定高度虚拟行关闭自动换行，超长文字使用省略号。旧版本示例中自己制作或修改的 Legacy Text Prefab 需要显式重建/替换文字组件，并为 TMP 指定 Font Asset；此升级不会把序列化的 Unity Font 自动转换为 TMP Font Asset。

所有视图在自己的 OnCreate 中绑定本实例按钮，OnOpen 接收控制器。Demo 没有依赖“在模板上绑定运行时 UnityEvent 并让 Instantiate 复制监听”的行为，也不需要手工拖按钮引用。

## 清理与验证范围

离开场景或销毁 launcher 会取消其任务、销毁其自己创建的 UIManager 和输入对象，并退休加载器。模板容器独立持久化，直到最后的视图/迟到加载租约归还才销毁，避免在 `Destroy` 请求后提前卸载来源。应用退出后没有后续主线程帧的极端尾部时序，不能由此示例保证继续执行托管归还回调。

该 Demo 用于功能学习；绑定文字和状态字符串会分配，不是零 GC 基准。源码的 .NET 合同编译与租约测试不能代替真实 Unity 的渲染、射线、输入、Prefab 克隆与 IL2CPP 验证。父文档记录本轮实际运行过的检查。

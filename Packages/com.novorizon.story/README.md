# Story（可选剧情模块）

剧情流独立于业务和 UI。`StoryPlayer` 只负责顺序、分支、等待、命令、取消和结果；`StoryStage` 使用 TMP 显示人物、对白、选项，`StoryVideoPlayer` 负责 Unity VideoPlayer。游戏任务、奖励、存档规则由业务通过 `IStoryCommands` 接入。无需 DataManager，也无需资源管理器单例。

## 先运行示例

在 Package Manager 导入 **Basic Story**，导入 TMP Essential Resources，然后执行 **Tools > Novorizon > Demos > Create Basic Story Scene**。进入 Play：点击先显示整句，再点击继续；选择分支；顶部可切换 English / Français。示例使用自己生成的背景、立绘和本地翻译，不需要先编写业务 API。

示例组件上可指定真实 `AudioClip` / `VideoClip`。未指定视频时会明确提示，并构建不含视频节点的剧情。指定后视频通过实际解码画面播放。视频编解码支持取决于平台；不要用 Editor 成功代替 iOS/Android 真机验收。默认完整安装包含 Video 模块；只安装 Story 包无需 UI、TMP 或 Video。包显式安装 Unity Audio 模块以支持默认舞台的配音，纯核心程序集不引用任何 Unity 引擎类型。

## 策划在 Inspector 配置

1. Create > Novorizon > Story > Sequence：填写稳定的 `sequenceId`、`version`、节点 `id`，最后以 `End` 结束；点击 **Validate Story**。
2. Create > Novorizon > Story > Asset Library：把业务资源 ID 映射到 Sprite、AudioClip、VideoClip 等项目资产。
3. Create > Novorizon > Story > Settings：选择 Sequence / Asset Library，设置打字速度和视频准备超时。
4. 场景添加 `StoryStage`，绑定 TMP speaker/body、背景 Image、按 ID 命名的立绘槽、选项容器、含 TMP 标签的 Button 模板和继续按钮。`dialogueRoot` 应是舞台的子物体，不能包含舞台自身。
5. 有视频时添加 `StoryVideoPlayer`，把同一个 Asset Library 拖到它的 Asset Library 字段，绑定 RawImage（和可选 AspectRatioFitter），再把该组件拖到 Stage 的 Video Provider；也可由代码显式 Configure 覆盖本地配置。RawImage 应放在舞台组件以外的子物体上，保持组件启用直至会话清理完成。
6. 添加 `StoryLauncher`，绑定 Settings / Stage；勾选 Play On Start，或将 Button UnityEvent 绑定 `Play()`。代码可 `await launcher.PlayAsync()` 获取完整结果。

`StoryStage` 本身不创建全局 Canvas 或 UIManager。项目可把它放进现有 UIPanel，使用 UI 的 `UISafeArea` 处理 iPhone 刘海、灵动岛和底部手势区；面板关闭时禁用舞台或取消会话。背景可铺满屏幕，文字、选项和继续按钮放在安全区内。示例已采用此结构。实际字体需有中文、日文等目标语言字形；TMP 默认字体不能覆盖所有文字。

### 节点字段

| Kind | 配置 |
| --- | --- |
| Say | `textKey`、可选 `speakerKey`；`resourceId` 可选语音；`durationSeconds > 0` 在整句显示后自动继续 |
| Choice | `choices` 中的稳定选项 ID、文本 key 和目标节点 ID |
| SetPortrait | `slotId` + Sprite 资源 ID；空资源 ID 隐藏该槽 |
| SetBackground | Sprite 资源 ID；空资源 ID 隐藏背景 |
| PlayVideo | 视频资源 ID；`canSkip` 控制当前视频允许跳过 |
| Wait | `durationSeconds`；由核心时钟处理 |
| Command | `commandId` 和字符串参数；调用项目注入的 `IStoryCommands` |
| End | 正常完成 |

空 `nextId` 使用下一个数组节点；显式 ID 用于跳转/分支。所有节点须可达，不允许无等待的无条件循环。播放开始时创建不可变快照；修改 Inspector 不会修改正在运行的会话。

## 多语言

文本字段存 key，翻译放在 `com.novorizon.localization` 的本地 `LocalizationTableAsset` / CSV / Luban 表中；同一套 key 可用于普通界面和剧情。项目先初始化 `LocalizationManager`，调用 `launcher.Configure(settings, stage, translations: localization)` 注入即可。舞台使用 `LocalizedText`，切换语言时当前对白、人物名和可见选项都会刷新，并保留已显示文字的大致进度。示例直接构建本地小表演示这一点，不要求 DataManager。

未注入且没有已初始化的 LocalizationManager 时，文本字段按原文显示，适合原型。正式内容建议使用翻译 key。语言切换只刷新文字；配音语言切换策略由业务决定，不会在一句话中途擅自重新播放语音。

## 资源与生命周期

`StoryAssetLibrary` 是最简单的本地引用方案，释放 lease 不会 Destroy 或 Unload 共享项目资产。Addressables、AssetBundle 或项目资源服务实现 `IStoryAssets.LoadAsync<T>`，每次返回独立 `StoryAssetLease<T>`；释放回调只释放本次取得的句柄。异步加载须响应 CancellationToken；晚到的结果也必须由 adapter/lease 明确释放。相关 Unity 调用和释放在主线程进行。

舞台按当前会话和步骤校验回调；旧按钮不会完成新步骤。对白结束停止并释放本句语音；立绘/背景替换或会话结束释放对应 lease。关闭/销毁只取消和释放自身拥有的内容。业务提供 AudioSource 时，它应专用于该舞台；默认按需创建舞台自己的 AudioSource。视频适配器每次拥有自己的 VideoPlayer、回调、RenderTexture 和资源 lease，退出时释放并恢复 RawImage 的原纹理。视频准备、首帧、播放错误、超时、取消和跳过均有完成路径。

`StoryLauncher` 将自身和舞台生命周期关联到会话取消。直接创建 `StoryPlayer(stage)` 时，也应向 `Play` 传入舞台 `LifetimeToken`，或在所有者销毁时取消会话。`StopAsync` 和 `session.Completion` 都等到清理结束；清理期间仍为 Busy。节点 `canSkip` 限制跳过；`session.Skip()` 跳过整段剧情，`stage.SkipVideo()` 只跳过当前允许的视频。

```csharp
launcher.Configure(settings, stage, projectAssets, localization, projectCommands, video);
StoryResult result = await launcher.PlayAsync(cancellationToken);
if (result.Status == StoryStatus.Completed)
{
    // 由业务判断是否发任务奖励。
}
```

Unity 呈现器的 Play / Advance / Dispose 和资源释放应从主线程调用。节点 `timeoutSeconds > 0` 会请求取消并产生 Failed / TimeoutException；如果项目适配器忽略取消，核心会保持 Busy 并等待真实退出，避免新旧操作重叠。视频准备超时从取得资源后开始；资源加载的总上限可使用节点 timeoutSeconds。

## 命令、存档和边界

`IStoryCommands` 只执行项目明确注册的命令。`context.CommandInstanceId` 标识内容与节点，业务幂等键还应包含任务/章节的本次运行标识，避免重复发奖，也避免把此节点永久视为已执行。命令失败产生 Failed；跳过、取消、失败不会触发 Launcher 的 Completed UnityEvent。

`StoryCheckpoint` 提供 sequence ID/version、恢复节点和选项。它是不可变运行时 DTO，不能直接交给 Unity `JsonUtility.ToJson`。存档层应转换到自己的 `[Serializable]` 字段记录，保存版本和选项，再显式构造 checkpoint。版本不匹配的恢复会被拒绝；迁移、任务进度和幂等日志由业务管理。checkpoint 不包含舞台图片、语音和视频进度；请在可重建舞台的业务边界恢复，或从恢复节点显式重新设置立绘/背景。第一版不包含图编辑器、Timeline/Animator 编排、自动嘴型、回看日志或存档后端。

## 按需依赖

- `Game.Framework.Story`：纯 C# 核心。
- `Game.Framework.Story.Unity`：ScriptableObject authoring、资源与媒体接口；不引用 VideoClip 或 UI。
- `Game.Framework.Story.UGUI`：安装 UI、TMP 和 Localization 后启用，全部文本为 TMP。
- `Game.Framework.Story.Video`：安装 Unity Video 模块和 uGUI 后启用；UGUI 程序集通过共享接口注入它，没有对 Video 程序集的反向依赖。

依赖关闭时只禁用相应适配器；核心仍可配合项目自定义 presenter 使用。

# 剧情模块架构与 Cube 参考审查

`v0.1.0-preview.34` 已提供独立可选的 `com.novorizon.story`，使用说明见 [Story README](../Packages/com.novorizon.story/README.md)。它负责可复用的播放流程与默认表现；GameStarter/游戏负责触发、任务、奖励、存档和具体资源服务的接线。本次未复制 Cube 的代码、Prefab、图片或第三方插件。

## Cube 实际提供了什么

审查基线：[Novorizon/Cube，25836cb62a462891add136a686ceebbb21621958](https://github.com/Novorizon/Cube/tree/25836cb62a462891add136a686ceebbb21621958)。这是对源码和数据的静态审查，未运行 Cube 游戏。

| 文件 | 已有能力与可参考内容 | 提取模块时需要调整 |
| --- | --- | --- |
| [StoryManager.cs](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/Story/StoryManager.cs) | 将表转换为运行时配置，按步骤排序，记录播放位置与完成 ID | 直接依赖具体 DataManager、QuestManager、StorageManager 与消息类型；这些接线应留在项目 |
| [StoryPresenter.cs](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/Story/StoryPresenter.cs) | 将打开窗口与管理器调用分开 | 窗口打开失败仍调用 completed，可能被业务当作剧情完成；需要显式结果与取消 |
| [StoryPanel.cs](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/Story/StoryPanel.cs) | TMP 文本、插画与文字混合、推进模式、stepVersion 隔离旧回调，关闭时停止协程 | Panel 同时承担推进、资源加载和表现；插画使用全局同步加载，未体现单独租约；按 Prefab 路径关闭会话，应改为持有自己的窗口句柄 |
| [StoryMotionPlayer.cs](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/Story/StoryMotionPlayer.cs) | 用 RawImage.uvRect 和 unscaledDeltaTime 做静态插画推拉、平移 | 参数来自 GameConfig；模块应接收配置，且完成通知须受当前步骤取消与代际校验约束 |
| [SimpleGuideManager.cs](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/Story/SimpleGuideManager.cs)、[GuideTargetRegistry.cs](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/Story/GuideTargetRegistry.cs) | 用稳定目标 ID 等待按钮出现，绑定/退订点击 | 新手引导可作为后续扩展动作，不需要成为首版剧情核心的必需依赖 |
| [story_step.xml](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Data/Defines/story_step.xml)、[tbstorystep.json](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Data/Json/tbstorystep.json) | 故事与步骤分表、稳定步骤 ID、插画和推进参数 | 运行时存档仍使用数组下标，正文/Guide 提示为直接字符串；新设计使用稳定节点 ID、内容版本和本地化 Key |

[Cube 的 Story 文档](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Docs/Modules/Story.md)与代码一致：已有 Text、Illustration、Mixed、Guide 四类步骤，没有角色立绘槽位、分支对话树、配音时间轴、视频或 Timeline 编排。[DialogManager](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/Story/DialogManager.cs)和 [EventTriggerSystem](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/Assets/Scripts/Game/Story/EventTriggerSystem.cs)都是空占位，不能据此认定已有完整对话系统。

Cube 根 [LICENSE](https://github.com/Novorizon/Cube/blob/25836cb62a462891add136a686ceebbb21621958/LICENSE)为 GPL-3.0。当前只参考职责与使用场景，不把其实现或第三方资源复制到 SDK；后续若计划直接复用，需要单独核实所选文件的权利和发布许可证。

## 已实现的职责边界

| 程序集/层 | 负责 |
| --- | --- |
| Game.Framework.Story | 纯 C# 不可变序列、稳定节点 ID、分支、等待、命令端口、单会话 Busy、取消/跳过/超时、明确结果与检查点 |
| Game.Framework.Story.Unity | ScriptableObject Sequence/Settings/Asset Library、本地资源租约、媒体接口；不引用 UI/VideoClip |
| Game.Framework.Story.UGUI | TMP 打字/全文/推进、人物名、选项、立绘槽、背景、可选配音、语言切换 |
| Game.Framework.Story.Video | Unity VideoPlayer 准备、首帧、错误、超时、跳过与取消，拥有并释放自身 RenderTexture/资源租约 |
| GameStarter/游戏 | 何时播放、任务条件、自定义命令、奖励幂等、检查点持久化/迁移、资源及 UI 窗口适配 |

核心不继承 UI Panel，也不创建全局 StoryManager。项目可把 StoryStage 放入现有 UIPanel，复用窗口与输入阻挡；关闭自己的窗口时取消自己的会话。UI 包继续提供通用布局、生命周期和安全区。

## 最简单的使用方式

导入 Basic Story 示例并用菜单创建场景，可直接运行立绘对话、分支和切换语言；语音/视频使用项目指定的 AudioClip/VideoClip。正式内容在 Inspector 创建 Sequence、Asset Library 和 Settings，绑定 StoryLauncher/Stage；按钮的 UnityEvent 调用 Launcher.Play。业务需要结果时等待 PlayAsync，只在明确 Completed 时推进任务。

文本使用同一份 LocalizationTableAsset/CSV/Luban 的 key，不必在每句台词中调用 DataManager。默认表现全部使用 TMP；文字和按钮置于 UISafeArea 内，背景可铺满屏幕。目标语言字体、长文本及 iPhone 横竖屏仍需目标设备验收。

首版提供 Say、Choice、SetPortrait、SetBackground、PlayVideo、Wait、Command、End。Sequence 构建时检查重复 ID、未知目标、不可达节点及无交互/正时长等待的无条件循环。首版不包含节点图编辑器、条件表达式语言、Timeline、镜头编排、自动嘴型或引导系统；这些按实际业务再扩展。

## 会话、媒体与恢复

StoryPlayer 每次只运行一个会话，清理期间仍 Busy。Completion 在 CloseAsync 和正在执行的推进回调结束后发布；关闭/加载/视频错误都不冒充 Completed。所有旧回调通过会话与步骤代际失效；资源租约按本次所有权归还，不清空全局缓存。

适配器须响应取消。若忽略令牌，核心等待该操作真实结束，不允许旧副作用与新会话并行。超时请求取消并最终返回 Failed；不能强制中止任意项目任务。Unity 表现调用和资源释放在主线程执行。

检查点含 sequence ID/version、稳定 node ID 和选择历史，是当前节点执行前的不可变快照。它不保存画面状态、视频播放时间或业务事务，也不能直接用 JsonUtility 序列化属性。项目转换为自己的存档字段 DTO，再显式恢复；恢复节点可能重做该节点，项目需选择能重建舞台的安全边界并处理内容版本迁移。

CommandInstanceId 标识序列/版本/节点的内容位置，不能代表每次播放或循环的业务发生次数。奖励幂等键由项目结合任务/逻辑运行 ID 决定；模块不自动发奖或宣称 exactly-once。

## 验证

纯核心独立回归覆盖图/快照、分支、恢复、取消、跳过、超时和推进竞态。真实 Unity 验收覆盖 TMP、多语言、资源释放和自生成 VP8 视频解码，最终结果与平台边界见 [原生验收](../tools/NativeAcceptance/README.md)。iPhone/Metal、目标平台媒体编解码及 IL2CPP 仍需对应主机和真机执行。

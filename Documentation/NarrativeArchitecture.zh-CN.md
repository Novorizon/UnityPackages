# 剧情模块规划与 Cube 参考审查

本文是设计提案。尚未新增 `com.novorizon.story`，下述类型与 API 尚未实现；本次没有复制 Cube 的代码、Prefab、图片或第三方插件。

建议把可复用的剧情播放能力做成**独立、可选模块**：流程核心负责播放会话，UI 适配负责立绘与对话表现，GameStarter 的业务接入负责触发条件、任务、存档和游戏行为。UI 包继续提供窗口、输入阻挡与生命周期等通用能力。

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

## 三层职责

| 层 | 负责 | 对外边界 |
| --- | --- | --- |
| 可选剧情模块 | 会话、步骤推进、等待、取消、跳过、结果、稳定节点 ID、恢复检查点 | 输入规范化剧情数据与适配接口；输出完成结果、选择及业务动作请求 |
| UI/媒体适配 | TMP 打字与立即显示全文、立绘槽位、背景、对话框、选项、音频句柄、视频播放面 | 使用 UI、Localization、Audio、资源租约和 Unity VideoPlayer；不判断任务、背包、奖励 |
| GameStarter/游戏业务 | 何时触发、是否允许重复、剧情结束后的任务变化、存档持久化、游戏变量与自定义命令 | 提供数据/资源/媒体适配，处理结果；不重复编写通用播放循环 |

首版只新增一个可选剧情包即可，按程序集隔离：

| 建议程序集 | 依赖与职责 |
| --- | --- |
| Story.Runtime | 纯数据、会话和执行协议；不引用 UIView、TMP、VideoPlayer、Luban 生成表或具体资源管理器，可独立测试 |
| Story.UGUI | 默认 TMP 对话/选项/立绘 Presenter，依赖已有 UI/TMP/Localization；缺少所需包时不启用 |
| Story.Video | Unity VideoPlayer 的准备/播放/错误/取消适配，按 Video 模块及所需表现依赖启用；不加入核心程序集 |

样例明确列出各适配所需的 UI、TMP、本地化、音频、Video 依赖，由 GameStarter 一次组装。默认表现与 Prefab 放在 `Samples~/BasicStory`；多个游戏确实需要独立安装不同表现时，再拆适配包。首版不为每种动作建立一个 UPM 包或 Manager，也不把未完成的包加入 framework.all 默认依赖。

已有 UI 包的 UIView、OpenToken、窗口句柄、遮罩和 SafeContent 继续复用。剧情播放器不继承 UIPanel；项目也无需创建另一个负责同一播放会话的 StoryManager 单例。

## 业务怎样简单使用

目标使用流程：

1. 在项目创建 `StorySequence` 资产，配置对话、角色立绘、背景和视频步骤；每个节点有稳定字符串 ID。
2. 在默认 StoryStage Prefab 上配置立绘槽位、TMP 对话框、视频 RawImage 和安全区布局，拖入一份项目 StorySettings。
3. 项目入口一次性接好本地化、资源、音频和视频来源。配置型 Launcher 可按序列 ID 播放；按钮或任务只调用 Play，不读取生成表或资源句柄。
4. 项目收到播放结果后，决定推进任务、写入完成状态或继续游戏。

核心通过明确的端口连接外部能力，例如 IStoryPresenter、IStoryAssets、IStoryText、IStoryAudio、IStoryVideo 和 IStoryCommands。接口接收稳定 ID、规范化数据和取消令牌，返回带生命周期的结果；不把服务定位器或游戏管理器传进每个节点。接口名同样只是提案。

首版内容源优先选一种：小型本地内容用 ScriptableObject；已有 Excel/Luban 流水线的项目用表适配器。两者都输出相同的只读序列模型，避免为每个来源维护一套执行器。Luban 的具体生成类型留在项目适配器，不成为 SDK 公共类型。

建议的 API 形态如下，仅表示边界：

```csharp
StorySession session = player.Play(sequence, cancellationToken);
StoryResult result = await session.Completion;
```

一个会话只代表一次播放。项目可调用 Advance、Skip 或 Cancel；不要求业务在每句台词之间手工开关 UI 或管理资源。播放结果包含 Completed、Skipped、Cancelled、Failed，并携带序列/节点 ID 与必要的选择结果。失败、视频加载失败、关闭与取消不能自动变成 Completed；是否把跳过算为“已看过”由项目明确决定。

同一播放器首版只允许一个活动会话，重复 Play 明确返回 Busy/拒绝，或由调用方先取消上一会话；不隐式叠加两个对话框，不自动丢弃旧会话。

## 数据与步骤模型

首版覆盖用户提出的对话、角色立绘和视频即可：

| 步骤/数据 | 最小内容 |
| --- | --- |
| Say | speakerId、speakerNameKey、textKey、格式参数、可选 voiceId、点击/自动推进规则 |
| Choice | choiceId、optionTextKey、目标 nodeId；首版提供直接选择，不先引入条件表达式 |
| SetPortrait | slotId、characterId、portraitId/表情 ID、显示/隐藏与过渡参数 |
| SetBackground | backgroundId、可选平移/缩放预设和时长 |
| PlayVideo | mediaId、可否跳过、准备超时、结束/失败策略、后续节点 ID |
| Wait | 秒数或明确的外部信号 ID，带取消与超时策略 |
| Command | 稳定命令 ID 与受校验参数，交给项目业务处理 |
| End | 结束结果 |

正文、角色名、选项、继续/跳过按钮使用本地化 Key；资源使用稳定 ID，由项目映射到 Sprite、Texture、AudioClip、VideoClip 或受控 URL。立绘与背景分别建模：左/右/中槽位与表情切换是角色表现，整幅插画推拉是场景表现。

首版提供简单 Choice 分支，导入时检查不存在的目标、重复 ID、不可达节点和无等待循环。无需状态机编辑器、任意脚本执行或复杂表达式语言。下一阶段按需求增加受限 Condition 与显式 Jump；业务事实由项目只读适配提供，副作用仍通过命令执行。

## 播放与媒体生命周期

每次播放持有独立取消源、代际 ID 和资源作用域；每一步再持有自己的等待/媒体句柄。推进、取消、跳过和场景退出都必须先使旧步骤失效，迟到的图片/语音/视频回调不能修改新步骤或新会话。

默认表现规则：

- 对话打字时点击第一次显示全文，再点击才推进；自动推进显式说明从显示全文、语音结束或延时哪一个条件开始计算。
- 准备好本步骤需要的资源后再发布可见状态。失败保留可辨识的失败结果，不把空立绘/空视频静默当作正常播放结束。
- 默认只使用 UI 的本次窗口句柄关闭自己，释放本次资源租约、输入屏蔽租约和音频句柄；不销毁共享 UIManager 或清空全局资源缓存。
- 视频停止后解绑回调与目标 RenderTexture，再释放渲染目标/资源；仅取消等待时，尚在进行的原生准备操作仍需在完成后归还资源。
- Unity 对象操作在主线程执行；数据规范化与纯流程校验可以与 Unity 无关。会话结束通知至多一次，清理异常不能让其余资源永远不归还。

VideoPlayer 适配至少处理 prepareCompleted、loopPointReached、errorReceived、准备超时、取消和跳过；以不同结果返回会话。视频面保持宽高比，并按项目布局选择留黑边或裁切。视频解码/编码格式、暂停恢复、目标设备音画同步要原生验收。

不要让子对话框暂时隐藏就取消整段剧情：切换到视频时文字面板可以隐藏，根 StoryStage/业务所有者仍持有会话。根表现被真正关闭或场景所有者退出时，才按显式策略取消。

## 检查点与业务副作用

检查点包含 sequenceId、内容版本、稳定 nodeId 和选择/变量的必要快照；不序列化 GameObject、回调、资源句柄或仅靠数组下标恢复。表增删顺序后，仍能定位原节点；节点被删除或版本不兼容时，项目选择指定安全节点、重新开始或放弃恢复，不能随意夹取数组下标。

只在安全步骤边界恢复，未确认的文字节点可以重新显示，视频默认从该视频步骤重新开始。需要视频精确续播时再扩展明确的媒体检查点，不把任意时间轴状态混进首版通用存档。

剧情模块发出业务命令请求，项目执行并返回结果；奖励、任务完成、消耗物品等不可重复副作用由项目用稳定命令实例 ID 做幂等处理。播放失败/取消不发任务完成事件。模块只产生检查点 DTO，由 LocalStorage 或项目存档系统决定写入时机与迁移规则。

## 最小实施顺序

1. **首版完整小流程**：SO 配置、只读序列、稳定 ID、会话结果与取消/跳过；TMP 对话、立绘/背景、简单选项、可选配音、VideoPlayer 准备/播放/错误/超时。交付“立绘对话 → 选项 → 配音/视频 → 立绘对话”的本地 Demo，业务入口不手工读取 DataManager。
2. **业务适配与恢复**：可选 Luban 导入/适配、完成/失败/跳过结果接线、检查点和幂等命令；GameStarter 展示进入区域触发与保存恢复。
3. **按需高级能力**：受限 Condition/Jump、GuideTarget 稳定 ID、批量校验与预览。复杂角色走位、镜头切换和时间轴演出使用可选 Timeline/Cinemachine 适配，由剧情流程等待其完成，不重建一套通用电影时间轴。

建议的首轮验收：双击推进不重复执行、旧加载/旧视频回调不能覆盖新会话、加载失败不触发任务完成、关闭/取消/跳过时资源可归还、等待和准备有超时、检查点不依赖节点顺序、语言切换正确更新 TMP/字体、长文与横竖屏安全区正确、视频播放失败可观测。桌面测试通过后仍需 iPhone/Metal、视频解码与 IL2CPP 原生验收。

GameStarter 的工作应是组装演示、制作 Prefab 和适配具体游戏规则；通用会话推进与媒体等待协议由可选剧情模块统一实现。该提案尚未实现，后续应围绕上述最小范围单独开发和验证。

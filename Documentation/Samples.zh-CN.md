# Demo 与示例

所有包面向 Unity 2022.3。先安装需要的包，再在 Package Manager 的 Samples 中 Import。`Samples~` 默认不编译；将示例脚本导入 Assets 后才会检查它们的 Runtime/Editor 程序集。

| 示例 | 入口与用途 | 前置条件 |
| --- | --- | --- |
| [Framework Demo](../Packages/com.novorizon.framework/Samples~/Demo/README.md) | Tools → Novorizon → Demos → Create Core Demo Scene；事件、时钟、池、线程、Task 与单例 | 无服务器、外部 Prefab 或资源包 |
| [UI Demo](../Packages/com.novorizon.framework.ui/Samples~/Demo/README.md) | Create UI Demo Scene；窗口层级、返回、确认、Toast 和虚拟列表 | uGUI；布局与模板由脚本创建 |
| [Resource Demo](../Packages/com.novorizon.framework.yooasset/Samples~/Demo/README.md) | Create Resource Demo Scene；显式初始化、独立 TextAsset 租约与释放 | 项目配置 DefaultPackage 和一个 TextAsset 地址 |
| [ECS QuickStart](../Packages/com.novorizon.ecs/Samples~/QuickStart/README.md) | 空对象挂 QuickStartDemo；移动、暂停、恢复与重建 | Framework、Mathematics；Cube 自动创建 |
| [Spatial Queries](../Packages/com.novorizon.rtree/Samples~/SpatialQueries/README.md) | 空对象挂 SpatialQueryExample，使用组件菜单 | 不需要角色或场景资源 |
| [Basic Playback](../Packages/com.novorizon.gpuanimator/Samples~/BasicPlayback/README.md) | 单个 GPU 播放器，Inspector 注入后通过组件菜单播放 | 同一套烘焙 Renderer、材质、动画纹理与 bytes |
| [Manual Crowd](../Packages/com.novorizon.gpuanimator/Samples~/ManualCrowd/README.md) | 共享数据、手动 Tick 与实例管理 | 匹配的烘焙资源；关闭播放器自动推进 |
| [Framework Pool](../Packages/com.novorizon.gpuanimator/Samples~/FrameworkPool/README.md) | 池化 GPU 播放器、回收清理 | 匹配的烘焙 prefab 与 bytes |
| [GameStarter](https://github.com/Novorizon/GameStarter) | Bootstrap 场景：输入、音频、设置、Luban、UI/资源接线和 HybridCLR | 通过工程 manifest 安装全部依赖 |

技能包的运行时接入示例在 [Ability README](../Packages/com.novorizon.ability/README.md)，编辑器入口见[编辑器指南](../Packages/com.novorizon.ability/Documentation~/Editor/README.md)。Odin 由项目自行安装；缺少 Odin 不妨碍运行时编译。

## 云端编译

GameStarter 提供 `Game.Editor.SampleValidation.ImportSamples`，将当前已解析的自有包声明的全部 Samples 导入隔离的 `Assets/NovorizonSamples`。随后重新启动 Unity 并执行 CompileScripts，避免仅编译包 Runtime 却遗漏示例。使用独立的验证 checkout，保留日志和报告，详见 [Unity 编译与验收](https://github.com/Novorizon/GameStarter/blob/main/Documentation/UnityValidation.zh-CN.md)。

编译不等于功能验收。GPU 示例需要烘焙资源和渲染检查；资源示例需要采集配置；UI、输入、场景和生命周期需要实际 Play。没有外部资源的示例可以直接按各 README 运行。

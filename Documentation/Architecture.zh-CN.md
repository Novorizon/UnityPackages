# 仓库、包与工程

UnityPackages 维护 22 个独立 UPM 包；GameStarter 是另一个仓库中的 Unity 工程。消费者通过 Git/UPM 安装，不依赖维护工作区的目录布局。

## 依赖方向

- Framework 提供通用基础服务，不引用 UI、ECS、Ability、GpuAnimator、RTree、HybridCLR 或 YooAsset。
- ECS 使用 Framework 单例，保留自己的类型工厂、实体/系统池、结构事务和清理顺序；与 Ability 无依赖关系。
- Ability 只定义自己的单位、世界和表现接口，由游戏实现，不要求 ECS 实现 IUnit。
- UI 依赖 Framework 和 uGUI，以 IUIAssetLoader 接收资源，不强制依赖 YooAsset。
- YooAsset、HybridCLR、音频、输入、Luban 数据包单向使用 Framework。
- RTree 不依赖其他自有包；六种树共用一个包和程序集。
- GpuAnimator 使用 Framework 的统一单例能力，支持 Built-in；资源由调用方加载。
- framework.all 只提供 Editor 安装器和依赖目录，不被运行时引用。

全部代码面向 Unity，保留实际需要的程序集边界，不另外拆成两个 Core/Unity 产品。

## 启动工程

GameStarter 的 GameEntry 选择发行并准备资源，再调用 HotUpdateManager。HotGame 统一初始化 Manager、恢复设置、加载 Luban 表和启动业务。编译源模式与热更模式共用 HotGame；GameEntry 不直接引用热更表程序集。

包内 Samples 按需导入，默认不会进入游戏工程的活动 Assets。云端编译示例时必须显式导入，步骤见 [Demo 索引](Samples.zh-CN.md)。验收工程在包仓库外创建，不提交回本仓库。

## 第三方来源

官方包的版本写在对应适配包 package.json 中，Git 来源登记于 workspace.json。聚合包不捆绑上游源码或归档。

HybridCLR 官方原生安装器需要工程中的物理 Packages 路径，因此使用方通过 UPM Embed 后安装原生运行时。这个可写副本属于使用方工程，不属于 UnityPackages 的发布内容。


Localization 只依赖 Framework；生成的 Luban 表由项目适配，uGUI 和 Luban Editor 桥接按已安装包启用。Diagnostics 仅在 Editor 读取公开状态，不被运行时包反向引用。GpuAnimator.URP 是独立的 URP 14 适配包，聚合安装默认包含它；只安装基础 GPU 包时仍可显式使用 Built-in Shader。

Story 是独立可选模块：核心只处理不可变序列和会话；Unity authoring、TMP 舞台及 VideoPlayer 分程序集。业务触发、任务奖励与存档迁移属于 GameStarter/游戏。all 的依赖目录包含该包；只装核心包不要求 UI/TMP/Video。


## 平台服务与业务入口

业务统一通过 `IapManager`、`AdManager`、`AuthManager`、`AnalyticsManager`、`AttributionManager`、`FirebaseManager` 调用。Manager 的原 Client 基类保留兼容，启动层选择和注入 SDK adapter；不会新增自动初始化 SDK 的全局访问入口。

Analytics 只依赖 Framework；Attribution 依赖 Framework / Analytics。Firebase 的统计 adapter 依赖 Analytics 核心，Analytics 不反向依赖 Firebase。统计与归因的可选程序集只有在安装对应外部 SDK 后才参与编译。SDK 版本、功能分工及推荐组合见 [SDK 清单](SdkGuide.zh-CN.md)。

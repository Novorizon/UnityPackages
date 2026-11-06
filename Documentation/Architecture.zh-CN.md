# 仓库与包的边界

采用一个 Git 仓库维护多个独立 UPM 包。共享提交、版本标签和自动验证；按包决定安装范围、程序集引用及运行时依赖。

- Framework 继续提供通用单例、线程调度、事件、定时器、池、网络、场景和本地存储，不引用 UI、ECS、Ability、GPU、空间索引或 HybridCLR。
- UI、HybridCLR 封装、YooAsset 适配、ECS、输入、音频和 Luban 数据表单向使用 Framework。ECS 专用实体、系统顺序、存储和池策略由 ECS 实现。
- Ability 与 ECS 互不依赖，项目需要共同使用时由项目接线；没有添加 ECS 到 IUnit 的适配层。
- RTree 的六种树保持各自目录，运行时共用一个包、一个程序集；没有 Grid。
- GpuAnimator 维持独立包及 Built-in 支持范围，Framework 对象池集成为可选 Sample。
- Ability 编辑器与运行时属于同一个包；Editor 程序集只在 Unity Editor 编译，Odin 仅影响编辑器界面。
- 聚合安装包是独立 Editor 工具。Unity 基础库和包的运行时代码不引用它。

全部库都面向 Unity。保留现有内部程序集划分，不增加一套 Core/Unity 产品层。

## 独立工程

包仓库不包含 Assets/Packages/ProjectSettings 形式的 Unity 工程。

GameStarter 独立维护，默认通过 Git URL 安装 framework.all 及完整依赖来源，并在 GameEntry 初始化 Manager。工程的运行与交付不依赖包仓库的本地目录结构。

原生包验收工程按需生成在包仓库外的临时目录。仓库中的工具和 API 模型用于开发验证，不是 Unity 工程。

原 Cube 仅提取 Ability 的库源码。原游戏管理器、存档/账号流程和游戏场景不并入通用运行时。

## 第三方

官方 HybridCLR 和 YooAsset 只作为外部依赖声明，版本写在各自适配包的 package.json 中；Git 来源在 workspace.json 中登记。仓库不保存上游源码、Samples 或依赖归档，聚合包也不捆绑其他包。

安装时由 UPM 获取依赖。HybridCLR 9.0.3 官方原生安装器使用物理 Packages 路径，因此在使用方工程中仍调用 UPM Embed；这不生成仓库或发行包内的官方源码副本。待官方安装器支持缓存路径时可移除该兼容步骤。

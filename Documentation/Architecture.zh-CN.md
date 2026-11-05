# 仓库与包的边界

采用一个 Git 仓库维护多个独立 UPM 包。共享提交、版本标签和自动验证；按包决定安装范围、程序集引用及运行时依赖。

- Framework 继续提供通用单例、线程调度、事件、定时器、池和网络，不引用 UI、ECS、Ability、GPU、空间索引或 HybridCLR。
- UI、HybridCLR 封装、YooAsset 适配和 ECS 单向使用 Framework。ECS 专用实体、系统顺序、存储和池策略由 ECS 实现。
- Ability 与 ECS 互不依赖，项目需要共同使用时由项目接线；没有添加 ECS 到 IUnit 的适配层。
- RTree 的六种树保持各自目录，运行时共用一个包、一个程序集；没有 Grid。
- GpuAnimator 维持独立包及 Built-in 支持范围，Framework 对象池集成为可选 Sample。
- Ability 编辑器与运行时属于同一个包；Editor 程序集只在 Unity Editor 编译，Odin 仅影响编辑器界面。
- 聚合安装包是独立 Editor 工具。Unity 基础库和包的运行时代码不引用它。

全部库都面向 Unity。保留现有内部程序集划分，不增加一套 Core/Unity 产品层。

## 独立工程

包仓库不包含 Assets/Packages/ProjectSettings 形式的 Unity 工程。

GameStarter 独立维护，默认通过 Git URL 安装 Framework，并通过模块窗口安装其他库。工程的运行与交付不依赖包仓库的本地目录结构。

原生包验收工程按需生成在包仓库外的临时目录。仓库中的工具和 API 模型用于开发验证，不是 Unity 工程。

原 Cube 仅提取 Ability 的库源码。原游戏管理器、存档/账号流程和游戏场景不并入通用运行时。

## 第三方

官方 HybridCLR 和 YooAsset 以独立包保留原样源码、Samples、许可证及 meta，避免统一仓库仍必须从旧项目找依赖。更新第三方版本时独立核对源码与封装兼容性。Odin 商业插件不纳入仓库。

聚合安装器在源码工作区优先使用经过名称/版本检查的相邻包；从 tarball 安装时使用校验归档；从发布后的 Git URL 安装时使用同仓库、同标签的子目录来源。HybridCLR 使用 UPM Embed 及封装 Configure，首次 Player 构建再由封装处理原生运行时和 DLL/AOT 收集。

ECS 使用 `com.novorizon.ecs` 包名，源码位于 Runtime；GpuAnimator 的 Runtime、Editor、Shaders 直接位于包根目录，不保留旧项目名外壳。

# 仓库与包的边界

采用一个 Git 仓库维护多个独立 UPM 包。共享提交、版本标签和自动验证；按包决定安装范围、程序集引用及运行时依赖。

- Framework 继续提供通用单例、线程调度、事件、定时器、池和网络，不引用 UI、ECS、Ability、GPU、空间索引或 HybridCLR。
- UI、HybridCLR 封装、YooAsset 适配和 ECS 单向使用 Framework。ECS 专用实体、系统顺序、存储和池策略由 ECS 实现。
- Ability 与 ECS 互不依赖，项目需要共同使用时由项目接线；没有添加 ECS 到 IUnit 的适配层。
- RTree 的六种树保持各自目录，运行时共用一个包、一个程序集；没有 Grid。
- GpuAnimator 维持独立包及 Built-in 支持范围，Framework 对象池集成为可选 Sample。
- Ability 编辑器与运行时分为两个包，Odin 仅影响编辑器界面。
- 聚合安装包是独立 Editor 工具。Unity 基础库和包的运行时代码不引用它。

全部库都面向 Unity。保留现有内部程序集划分，不增加一套 Core/Unity 产品层。

## 工程

`Projects/GameStarter` 提供空业务的 GameEntry、项目 StartupModule 生命周期和可选模块窗口，默认只依赖 Framework。它使用 Framework 单例，实际游戏业务由项目模块接入。

`Projects/PackageValidation` 的清单引用全部本地包，并包含 Unity Test Framework，供原生导入与回归验收。包源码只存放在根目录 Packages，不在工程 Assets 中复制另一份运行时代码。

原 Cube 仅提取 Ability 运行时和编辑器。原游戏管理器、存档/账号流程和游戏场景不并入通用运行时。原仓库保留用于追溯；本轮没有删除、归档或合并其远端历史。

## 第三方

官方 HybridCLR 和 YooAsset 以独立包保留原样源码、Samples、许可证及 meta，避免统一仓库仍必须从旧项目找依赖。更新第三方版本时独立核对源码与封装兼容性。Odin 商业插件不纳入仓库。

聚合安装器在源码工作区优先使用经过名称/版本检查的相邻包；从 tarball 安装时使用校验归档；从发布后的 Git URL 安装时使用同仓库、同标签的子目录来源。HybridCLR 使用 UPM Embed 及封装 Configure，首次 Player 构建再由封装处理原生运行时和 DLL/AOT 收集。

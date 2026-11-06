# UnityPackages

Novorizon 的 Unity 多包仓库。基础组件库继续叫 **Framework**；各库共用一个代码仓库，各自保留独立 UPM 包、程序集和依赖关系。

统一远端为 [Novorizon/UnityPackages](https://github.com/Novorizon/UnityPackages)，当前发行标签为 `v0.1.0-preview.24`。可克隆完整仓库本地开发，也可通过独立包或聚合包安装。

## 包布局

| 目录 | 职责 |
| --- | --- |
| `Packages/com.novorizon.framework` | 单例、线程调度、事件、定时器、池、网络、场景、本地存储 |
| `Packages/com.novorizon.framework.ui` | 独立 UI 包 |
| `Packages/com.novorizon.framework.yooasset` | Framework 的 YooAsset 适配 |
| `Packages/com.novorizon.framework.hybridclr` | HybridCLR 默认配置、构建、补丁与启动封装 |
| `Packages/com.novorizon.ability` | Ability 运行时与编辑器 |
| `Packages/com.novorizon.ecs` | ECS 实体、系统、专用池及生命周期 |
| `Packages/com.novorizon.rtree` | 六种树各自一个目录，共用一个包 |
| `Packages/com.novorizon.gpuanimator` | Built-in GPU 动画烘焙、注入、播放及回收 |
| `Packages/com.novorizon.input` | Unity Input System、Map/设备过滤、屏蔽与重绑定 |
| `Packages/com.novorizon.audio` | 音源池、音乐交叉渐变、AudioMixer 路由 |
| `Packages/com.novorizon.data` | Luban Excel 生成、二进制表加载与查询 |
| `Packages/com.novorizon.framework.all` | 自动聚合安装入口 |

仓库共 **12 个自有包**，包名统一使用 `com.novorizon` 前缀。官方 HybridCLR、YooAsset、Luban 等第三方只通过依赖声明引用，由使用方的 Unity Package Manager 获取。UI 独立发布为包；Ability 运行时与编辑器在同一个包内。代码放在同一个仓库不代表运行时互相依赖。

## 使用

本仓库只发布 UPM 包，不包含 Unity 工程。需要使用库时，在自己的 Unity 工程中通过 Package Manager 安装。完整集合使用聚合包，独立库使用各自的 Git 子目录地址。

GameStarter 是单独维护的 Unity 启动工程，默认通过 Git 引用 framework.all 及完整依赖来源，在 GameEntry 集中初始化 Manager。它不使用指向本仓库的 `file:../../../` 路径，也不要求使用者克隆本仓库。

本地开发某个包时，可在外部 Unity 工程中使用 Add package from disk 选择本仓库对应的 package.json；发布安装仍使用下面的 Git 地址。

## Git 安装

每个包均有独立安装路径，例如：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework#v0.1.0-preview.24
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.ui#v0.1.0-preview.24
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.all#v0.1.0-preview.24
```

Unity 的 package.json 不支持 Git 传递依赖。需要全部库时使用聚合安装包；单独安装某个库时先安装它需要的自有基础包。聚合安装的所有 Git 来源使用同一个仓库、同一个版本标签；后续发行不应移动已发布标签。

## 维护与验证

Python 3.12+、.NET SDK 8+：

```sh
python3 tools/workspace.py check
python3 tools/workspace.py verify --dotnet /path/to/dotnet
# 修改包内容或版本后，刷新依赖安装目录：
python3 tools/workspace.py sync
python3 tools/workspace.py export --output /tmp/UnityPackages-release
```

验证包括导入基线比对、依赖闭包、程序集/GUID 冲突、包仓库结构，以及聚合安装、Ability 和空间查询回归。当前没有 Unity Editor，真实导入、渲染、IL2CPP 和设备行为尚未验收。详见 [验证范围](Documentation/Validation.md)。

来源及原提交记录在 [ORIGINS.json](ORIGINS.json)，导入文件基线在 [ImportBaseline.json](Documentation/ImportBaseline.json)。迁移保留各包的许可证和第三方通知；RTree 继续遵循 LGPL-2.1-or-later。仓库没有将全部源码重新授权为同一个许可证。

结构与依赖约定见 [架构说明](Documentation/Architecture.zh-CN.md)，远端发布步骤见 [发布说明](Documentation/Publication.md)。

## 从上一版升级

Ability 运行时和 Odin 编辑器已合为 `com.novorizon.ability`；ECS 的包名为 `com.novorizon.ecs`；GpuAnimator 从 `com.guanjinbiao.gpuanimator` 改名为 `com.novorizon.gpuanimator`。`v0.1.0-preview.24` 的聚合安装器会在同一次 UPM 操作中安装新包并移除旧包。直接安装单个包的项目，应同时替换清单中的旧包名和 Git 安装路径，避免相同程序集被导入两次；GpuAnimator 的 `testables` 条目也使用新包名。已有嵌入副本需要由项目先移出再升级。

Framework 的单例启动回调统一为 `OnStart`，项目中原来重写 `OnSingletonAccessed` 的类需要同步改名。配套 UI、ECS 和 HybridCLR 封装已同步；安装时使用同一发行标签。

ECS 源码直接位于包内 `Runtime/`；GpuAnimator 的 `Runtime/`、`Editor/`、`Shaders/` 位于包根目录，去掉旧工程名外壳。运行时源码和原脚本/资源 GUID 保留。

## 原生包验收

需要 Unity 验收工程时，在仓库外临时生成，不将工程提交到包仓库：

```sh
python3 tools/workspace.py unity-project --output /tmp/NovorizonPackageValidation
```

打开该目录进行原生导入与测试。当前环境没有 Unity Editor；生成器只生成验收用工程配置，不代表已经完成原生验收。

## 第三方依赖

例如 `com.novorizon.framework.yooasset/package.json` 声明 `"com.tuyoogame.yooasset": "2.3.18"`；HybridCLR 封装同样声明官方版本。仓库和发布的各包不包含这些上游包的源码或归档。

UPM 对 Git 传递依赖有限制，Git 分发使用 `workspace.json` 中的上游来源，由聚合安装器或 GameStarter 把来源交给使用方工程的 Package Manager 一起解析。官方包下载至使用方工程的 UPM 管理区域，不进入本仓库。聚合包本身也不再打入其他库的 Payload。

## v0.1.0-preview.24 升级

- 修复 UnityThread 在回调内切换宿主后，旧批次继续执行新会话任务的问题。Core 更新为 0.18.0-preview.2，引用 Core 的包同步递增版本，使聚合安装器能识别升级。
- all 支持损坏安装状态的手动重试，并把丢失 UPM 请求句柄后的注册查询限制为每半秒一次。
- API 调用方式保持兼容；Player 中的 Core 修复需重新构建 Player，只导出热更 DLL 不会替换现有 AOT 代码。

从较早版本升级时，还需留意以下迁移：

- com.novorizon.config 改名为 com.novorizon.data，运行时程序集改为 Game.Framework.Data；聚合安装器在同次 UPM 操作中替换旧包。
- Luban 统一由 DataManager.LoadAsync/Get/TryGet 加载及查询，加载时直接传入生成的 Tables 构造方式，无需注册。清单和表文件共用一个字节加载器。
- 删除旧 ConfigManager、通用 JSON 表 API，以及 Framework Core 中的动态对象 DataManager。动态数据和其他 JSON 由业务维护，LocalStorageManager 的存档 API 保留。
- GameStarter 的 GameEntry/HotGame 已同步初始化与退出清理。固定 Git 标签不会自动升级；更新 manifest 标签，或运行 GameStarter 的 sync-packages.py --ref v0.1.0-preview.24。
- 本次包含 AOT API 删除及程序集依赖变化，HybridCLR 项目需要重新 Build New Player，不能只导出热更 DLL。

场景、音频、Luban 数据表和本地存储的使用与边界见 [基础服务说明](Documentation/GameServices.zh-CN.md)。

UI 重复打开、资源租约与作用域、网络逐调用取消及 GameEntry 启停见 [生命周期与所有权](Documentation/LifecycleAndOwnership.zh-CN.md)。

OnStart/OnExit 的配对生命周期、Release 和 GameStarter 的直接初始化/退出示例见 [生命周期说明](Documentation/LifecycleAndOwnership.zh-CN.md)。GameStarter 已移除 ManagerLifetime 及入口层的 Manager 借用/失败回滚登记。

Framework 已内置编辑器静态重置生成器：单例子类有静态状态时加 partial，关闭 Domain Reload 后进入 Play 自动恢复声明值；Player 不生成额外重置代码。详细规则见 [静态重置](Packages/com.novorizon.framework/Documentation~/StaticReset.md)。

Luban 统一迁入 `com.novorizon.data` 的 DataManager，加载时直接传入生成的 Tables 构造方式，无需注册。旧 ConfigManager、通用 JSON 表 API 和 Core 动态对象容器已删除；详见 [Data 包升级说明](Packages/com.novorizon.data/README.md#升级)。

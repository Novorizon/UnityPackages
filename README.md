# UnityPackages

Novorizon 的 Unity 多包仓库。基础组件库继续叫 **Framework**；各库共用一个代码仓库，各自保留独立 UPM 包、程序集和依赖关系。

统一远端为 [Novorizon/UnityPackages](https://github.com/Novorizon/UnityPackages)，当前发行标签为 `v0.1.0-preview.3`。可克隆完整仓库本地开发，也可通过独立包或聚合包安装。

## 包布局

| 目录 | 职责 |
| --- | --- |
| `Packages/com.novorizon.framework` | 单例、主线程调度、事件、定时器、通用池、网络 |
| `Packages/com.novorizon.framework.ui` | 独立 UI 包 |
| `Packages/com.novorizon.framework.yooasset` | Framework 的 YooAsset 适配 |
| `Packages/com.novorizon.framework.hybridclr` | HybridCLR 默认配置、构建、补丁与启动封装 |
| `Packages/com.novorizon.ability` | Ability 运行时与编辑器 |
| `Packages/com.novorizon.ecs` | ECS 实体、系统、专用池及生命周期 |
| `Packages/com.novorizon.rtree` | 六种树各自一个目录，共用一个包 |
| `Packages/com.guanjinbiao.gpuanimator` | Built-in GPU 动画烘焙、注入、播放及回收 |
| `Packages/com.novorizon.framework.all` | 自动聚合安装入口 |
| `Packages/com.code-philosophy.hybridclr` | 未修改的官方 HybridCLR 9.0.3 |
| `Packages/com.tuyoogame.yooasset` | 未修改的官方 YooAsset 2.3.18 |

共 **9 个自有包、2 个官方依赖包**。UI 独立发布为包；Ability 运行时与编辑器在同一个包内。代码放在同一个仓库不代表运行时互相依赖。

## 本地使用

使用 Unity Hub 打开 **`Projects/GameStarter`**，建议 Unity 2022.3.62f1。它默认仅依赖 Framework，打开 Bootstrap 场景后可通过 Optional Modules 窗口选择其他库。本地安装来源是同仓库的 `Packages/`，清单使用相对路径，整体复制仓库后仍可解析。

需要一并安装所有库时，在项目的 Package Manager 中选择 **Add package from disk**，选取仓库内：

```text
Packages/com.novorizon.framework.all/package.json
```

聚合包自动发现同仓库的其他包，统一安装并配置 HybridCLR。也可以安装导出的单个 `com.novorizon.framework.all-0.1.0-preview.3.tgz`，其中附带另外 10 个包的校验归档。

`Projects/PackageValidation` 是依赖全部 11 个包的 Unity 验收工程。它用于导入、编译和包内测试，不包含游戏玩法。官方 uGUI、Mathematics、Scriptable Build Pipeline 和 Test Framework 等依赖仍由 Unity 注册表提供。Odin Inspector 商业插件由项目自行提供。

## Git 安装

每个包均有独立安装路径，例如：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework#v0.1.0-preview.3
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.ui#v0.1.0-preview.3
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.all#v0.1.0-preview.3
```

Unity 的 package.json 不支持 Git 传递依赖。需要全部库时使用聚合安装包；单独安装某个库时先安装它需要的自有基础包。聚合安装的所有 Git 来源使用同一个仓库、同一个版本标签；后续发行不应移动已发布标签。

## 维护与验证

Python 3.12+、.NET SDK 8+：

```sh
python3 tools/workspace.py check
python3 tools/workspace.py verify --dotnet /path/to/dotnet
# 修改包内容或版本后，刷新归档校验与工程配置：
python3 tools/workspace.py sync
python3 tools/workspace.py export --output /tmp/UnityPackages-release
```

验证包括导入基线比对、依赖闭包、程序集/GUID 冲突、项目相对路径，以及迁入的聚合安装、GameStarter 和空间查询回归。当前没有 Unity Editor，真实导入、渲染、IL2CPP 和设备行为尚未验收。详见 [验证范围](Documentation/Validation.md)。

来源及原提交记录在 [ORIGINS.json](ORIGINS.json)，导入文件基线在 [ImportBaseline.json](Documentation/ImportBaseline.json)。迁移保留各包的许可证和第三方通知；RTree 继续遵循 LGPL-2.1-or-later。仓库没有将全部源码重新授权为同一个许可证。

结构与依赖约定见 [架构说明](Documentation/Architecture.zh-CN.md)，远端发布步骤见 [发布说明](Documentation/Publication.md)。

## 从上一版升级

Ability 运行时和 Odin 编辑器已合为 `com.novorizon.ability`；ECS 的包名为 `com.novorizon.ecs`。`v0.1.0-preview.3` 的聚合安装器会在同一次 UPM 操作中安装新包并移除旧的独立 Editor/ECS 包。直接安装单个包的项目，应先移除旧的 `com.novorizon.ability.editor.odin` 与 `com.novorizon.ecsframework` 依赖，避免相同程序集被导入两次。已有嵌入副本需要由项目先移出再升级。

ECS 源码直接位于包内 `Runtime/`；GpuAnimator 的 `Runtime/`、`Editor/`、`Shaders/` 位于包根目录，去掉旧工程名外壳。运行时源码和原脚本/资源 GUID 保留。

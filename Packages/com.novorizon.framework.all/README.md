# Novorizon Framework All

安装一次，自动安装完整的 Novorizon Unity 库集合，并执行 HybridCLR 默认配置。适用于 Unity 2022.3，可用于 GameStarter 或其他 Unity 工程。

## 安装

**分发版**：Package Manager → `+` → **Add package from tarball…**，选择 `com.novorizon.framework.all-0.1.0-preview.3.tgz`。等待 Console 出现：

```text
[Novorizon All] 全部模块安装完成，HybridCLR 默认配置和热更入口已就绪。
```

**Git 源码版**：Package Manager → **Add package from git URL…**：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.all#v0.1.0-preview.3
```

源码版从 `Dependencies.json` 中固定的 Git 提交安装；附带 Payload~ 的 tarball 分发版直接使用经过 SHA-256 校验的归档，不需要 Git 下载这些库。uGUI、Mathematics、Scriptable Build Pipeline 等官方注册表依赖仍由 Unity 下载，分发版也需要正常访问 Unity 注册表。

## 包含的模块

| 包 | 版本 |
| --- | --- |
| Framework | 0.10.0-preview.1 |
| UI | 0.6.0-preview.1 |
| HybridCLR 封装 | 0.1.0-preview.2 |
| Ability（运行时与编辑器） | 0.3.0-preview.1 |
| RTree | 1.0.0-preview.2 |
| GpuAnimator | 3.2.0-preview.2 |
| ECS | 2.0.0-preview.3 |
| YooAsset 适配 | 0.10.0-preview.1 |
| 官方 HybridCLR | 9.0.3 |
| 官方 YooAsset | 2.3.18 |

Odin Inspector 商业插件由项目自行提供。缺少 Odin 时技能运行时和其他模块仍可用，编辑器包会显示安装提示。GpuAnimator 保持现有 Built-in 渲染管线支持范围。

在完整 UnityPackages 工作区安装时，安装器优先使用经过包名/版本核验的相邻目录，生成项目相对 file: 地址。

安装器统一使用表中的兼容版本；同版本已安装包会跳过。其他版本的嵌入包无法被 UPM 替换，安装器会保留它并指出冲突，处理后重试。

## 安装行为

Unity 不支持在包的 `package.json` 中声明 Git 传递依赖；这些库目前未统一发布到 scoped registry。因此本包作为 **Editor 自动聚合安装入口**，通过一次 `Client.AddAndRemove` 同时提交全部库，使自有包的版本依赖一起进入解析；运行时不引用本包。以后发布统一 registry 时可改为普通的版本依赖聚合包，使用方式仍为安装一个包。

包归档会先完整校验，再复制到项目的 `Packages/NovorizonFrameworkAll~/<聚合包版本>/`。项目清单使用相对 `file:` 地址，不能指向可删除的 `Library/PackageCache`。复制中断可重试修复，项目可与该归档目录一起提交或复制。

官方 HybridCLR 安装完成后调用 UPM `Client.Embed`，满足 9.0.3 安装器对物理 `Packages/com.code-philosophy.hybridclr/Data~` 路径的要求。待封装的 Editor 程序集加载后调用 `GameBuild.Configure`，生成默认 Resources 配置、热更 asmdef 与入口；已有配置、代码和场景由 Configure 保留。首次 IL2CPP 构建使用 **Tools/Novorizon/Hot Update/Build New Player**，DLL/AOT 收集与原生安装由封装处理。

安装过程跨 domain reload 和 Editor 重启保存到 `ProjectSettings/NovorizonFrameworkAll.install.json`。完成后不重复执行；升级聚合包版本会重新核对并配置。失败时保留已安装的模块，Console 显示原因，处理后使用 **Tools/Novorizon/All Packages/Retry Installation**。

各模块作为项目清单依赖安装；移除聚合包后，这些模块及归档仍保留，可通过 Package Manager 分别移除。安装库不会自动连接服务器、创建玩法系统或初始化资源下载。

## 验证

本轮执行了 Editor 源码编译、23 组自动安装 API 模型验证、分发包身份/版本/哈希与依赖闭包检查。没有可用 Unity Editor，真实 UPM 解析、资源导入、脚本重编译、HybridCLR 配置及 IL2CPP 构建仍需原生验收。

源码验证、打包和真实 Unity 验收命令见 [维护说明](Documentation~/Development.md)。本聚合包随 UnityPackages 中的其他独立包一起维护；Framework 基础包仍保持单向依赖边界。

本版安装会在同一次 Client.AddAndRemove 操作中移除旧独立 Ability Editor 和 ecsframework 包，保留原子更新语义。旧嵌入副本需先由项目移出。

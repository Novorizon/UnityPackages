# HybridCLR 封装

面向 Unity 2022.3，依赖 Framework 与官方 HybridCLR 9.0.3。封装项目配置、匹配的原生运行时安装、AOT 基线、热更 DLL 构建校验、签名发行验证、缓存与显式入口加载。

## 项目接入

完整工程使用 [GameStarter](https://github.com/Novorizon/GameStarter)。独立项目安装 framework.all 或同一发行标签的 Framework、本包及官方依赖。HybridCLR 原生安装器需要使用方工程中的可写 Embedded 包，由安装流程处理。

运行时由自己的 GameEntry 显式调用：

```csharp
var settings = new UpdateSettings
{
    entryAssembly = "Game.HotUpdate",
    entryType = "Game.HotUpdate.HotGame"
};
await HotUpdateManager.Instance.InitializeAsync(settings, cancellationToken);
```

HotGame 是公开、无参构造的普通 C# 类，实现 IGameModule。HotUpdateManager 创建它并调用 InitializeAsync；启动完成时由入口调用 host.ReportReady。共享启动参数可通过 InitializeAsync(settings, startupContext, token) 传入，在热更侧读取 IGameContextHost.StartupContext。

游戏逻辑和生成表放在 settings.hotUpdateRoot 下的 asmdef 中；AOT 程序集不能反向引用这些热更程序集。Editor 使用已编译的程序集，Player 使用验证后的 DLL；loadFromCompiledAssemblies 支持编译源入口。没有启动 JSON、场景引导组件或 RuntimeInitialize 自动创建的热更 GameObject。

重复初始化共用任务。退出使用 HotUpdateManager.Release；普通服务的初始化和退出由 HotGame 组织，管理器内部处理自己的重复释放。可选 IGameApplicationEvents 接收宿主转发的暂停/焦点通知。

## 资源配对

ReleaseManifest.content 保存资源包名和精确版本，与 DLL 哈希、基线一起签名。UpdateSettings.prepareContentAsync 在载入任何 AOT 元数据和托管程序集之前准备对应资源。回调收到不可变 ReleaseContentContext。

资源准备失败时，加载器拒绝该更新并尝试配套的内置发行；项目回调必须同时切回内置资源。GameStarter 已接好 YooAsset 的精确版本下载，以及内置发行使用离线资源的回退。代码加载后不在当前进程切换版本。

remoteBaseUrl 为清单根，remoteFilesBaseUrl 可单独指定 DLL 根；scopeRemoteByBaseline 按平台/渠道/基线划分路径。启用远程更新需要可信公钥和 HTTPS。

## 构建与发布

- GameBuild.Configure：建立工程配置和入口模板，不覆盖已有游戏入口。
- GameBuild.ValidatePlayerScripts：独立脚本编译与 DLL 输出检查，不构建原生 Player。
- GameBuild.BuildNewPlayer：配置程序集、安装匹配原生运行时、生成基线并构建 IL2CPP 主包。
- GameBuild.ExportUpdateToDirectory：编译本次 DLL，检查 AOT、元数据与原生桥接兼容性，输出未签名发行。

支持的发行目标为 Windows x64、Android ARM64 和 iOS ARM64。Unity、AOT 依赖、契约或程序集拓扑变化需新主包；热更表结构在现有 AOT 能力与拓扑范围内更新。

构建失败时不会把旧输出当作新补丁。客户端先验证 RSA-3072/SHA-256 签名，再核对基线、文件大小和 SHA-256。AOT 元数据来自主包；缓存分阶段提交，启动失败的更新会被隔离。已发布内容不能原地覆盖，回退使用更高发行版本号。

GameStarter 提供资源构建、签名、公私钥管理与发布目录整理。详细操作、云端编译和原主包验证新表结构见 [Unity 验收说明](https://github.com/Novorizon/GameStarter/blob/main/Documentation/UnityValidation.zh-CN.md)。

# 安装与快速开始

目标 Unity 2022.3 LTS，Windows / Android / iPhone，API Compatibility Level 为 .NET Standard 2.1。运行已生成的 Unity 游戏代码无需 .NET SDK；Luban 生成和维护回归需要 .NET 8 SDK。

## 选择安装包

| 包 | 必需依赖 | 使用情况 |
| --- | --- | --- |
| com.novorizon.framework | 已附带 Protobuf / BouncyCastle / Unsafe DLL | 基础服务与 TCP 客户端 |
| com.novorizon.framework.ui | 匹配版本的主包、com.unity.ugui 1.0.0 | 按需安装 UI |
| com.novorizon.framework.yooasset | 主包及官方 YooAsset 2.3.18 | 项目选择该资源库时安装 |

不使用 YooAsset 的项目安装主包，按需再装 UI。先安装匹配版本的主包，再安装 UI。UI 通过 IUIAssetLoader 接入项目自己的加载/释放接口，默认使用 Resources。

Package Manager → + → Add package from git URL：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework#v0.1.0-preview.25
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.ui#v0.1.0-preview.25
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.yooasset#v0.1.0-preview.25
```

一次粘贴一个需要的地址。正式项目可把分支替换成审核过的提交，避免自动跟随变更。已有同名包用 Package Manager 更新，不把第二份源码粘进 Assets。

本地克隆可 Add package from disk，选择对应的 package.json。导出包可 Add package from tarball，选择对应 .tgz。不要把整个仓库拖入 Assets，也不要重复导入同一包源码。

## 第一次验证

1. 安装主包，在 Samples 中 Import **Framework Demo**。
2. 菜单 Tools / Novorizon / Demos / Create Core Demo Scene。
3. 保存当前修改场景，新建独立演示场景后 Play。
4. 用仪表盘测试事件、计时器、池、后台工作回主线程及 Task。基础演示完全离线。

安装 UI 后，在 UI 包 Samples 中 Import **UI Demo**，用 Create UI Demo Scene 运行。窗口模板、按钮、列表行均由 Demo 创建，无需手工制作 Prefab。

Resource Demo 需要项目的 YooAsset 采集配置。各 Demo 只在 Import 样例并点击创建场景菜单后启用，不在安装包时修改项目或联网。

## 首次网络连接

创建 Resources/NetworkSettings 并配置 Host、Port、Encryption 和通信密钥；在 Unity 主线程调用：

```csharp
using Game.Framework.Network;

var network = NetworkManager.Instance;
network.Initialize();
await network.ConnectAsync(ct);
```

Initialize 只准备配置和派发，默认不执行账号登录。项目通过 Initialize(authenticate) 安装自己的认证回调，消息类型由项目提供；主包不包含进度、存档、Facebook 绑定或玩家 ID。

## 业务入口与拥有者

- EventManager.Instance：订阅返回 IDisposable，页面离开时退订。
- TimerManager.Instance：保存自己的 TimerHandle，离开时取消；不 Clear 全局计时器。
- GameObjectPool：每个功能/场景创建自己的池，结束时 Dispose。
- ReferencePool<T>.Shared：Rent/Return 成对，归还后不继续使用该对象。
- UnityThread：后台提交需要操作 Unity 的工作；无需每个模块另建主线程队列。
- UI.UIManager.Instance：管理页面与弹窗；窗口 Prefab 通过 IUIAssetLoader 获取和归还。

## 程序集与导入检查

自定义业务 asmdef 引用所需的 Game.Framework.Core、Game.Framework.Unity、Game.Framework.Network、Game.Framework.Network.Unity 或 Game.Framework.UI。Samples 的 Editor 程序集仅用于创建 Demo 场景，运行时不能引用它们。

主包 Runtime/Plugins 中的 DLL、许可证与哈希清单必须一起保留。项目已有 Google.Protobuf、BouncyCastle 或 Unsafe 时统一到一份兼容版本。Grpc.Tools/protoc 不属于 Unity 运行时依赖；修改协议才需要生成工具。

出现重复类型通常是同时安装了包和同份 Assets 源码；找不到 UI/YooAsset 程序集通常是装错可选包或业务 asmdef 引用缺失。首次运行先用离线 Demo 排除导入问题，再测试外部服务。

[基础服务](Foundation.md) · [网络](Network.md) · [Core Demo](../Samples~/Demo/README.md)

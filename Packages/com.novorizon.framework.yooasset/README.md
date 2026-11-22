# YooAsset 资源适配

这是可选包 `com.novorizon.framework.yooasset`，适用于 Unity 2022.3 与官方 YooAsset **2.3.18**。依赖同版本主框架；主框架和 UI 包都不强制使用 YooAsset。

通过根文档的[当前固定发行标签](../../README.md#安装)安装聚合包，或将单包 URL 的目录改为 `Packages/com.novorizon.framework.yooasset`，并显式安装同发行 Framework 与官方 YooAsset 2.3.18。所有自有包使用同一发行标签。

已有项目可改用本地 `package.json` 或对应的 `.tgz`。不要将本包、另一份 Assets 源码和其他版本的 YooAsset 同时重复导入。

## 从哪里开始

| 需要完成的事 | 入口 |
| --- | --- |
| 初始化、加载、场景、图集、Shader、释放规则 | [资源指南](Documentation~/Resources.md) |
| 检查项目的 DefaultPackage 与缓存行为 | [Resource Demo](Samples~/Demo/README.md) |
| 使用其他资源库或纯 Resources 驱动 UI | [UI 资源接口指南](../com.novorizon.framework.ui/Documentation~/Features.md#资源加载接口) |

Resource Demo 演示逐次加载与归还，可从 Package Manager Samples 按需导入，菜单 **Tools > Novorizon > Demos > Create Resource Demo Scene** 创建独立测试场景。Play 后界面先显示配置提示；只有点击按钮才初始化、加载或释放。它需要项目已有的 YooAsset 采集配置与资源，不自带 AssetBundle，也不会在导入时联网。

## 初始化与下载

```csharp
bool started = await ResourceManager.InitializeAsync(resourceMode, token);
if (!started)
{
    return;
}

if (resourceMode == ResourceMode.Online)
{
    var result = await ResourceManager.DownloadAsync(new RemoteDownloadSetting
    {
        MainUrl = cdnUrl,
        PackageVersion = selectedVersion,
        Progress = progress
    }, token);
    if (!result.IsReady)
    {
        // 项目的启动流程根据 result.Status 决定是否重试或切换到内置资源。
        return;
    }
}
```

- `Game.Resource.ResourceMode.Offline` 直接挂载主包内置清单。`EditorSimulation` 使用编辑器模拟文件系统。两种模式初始化成功后 `IsReady` 为 true。
- `Online` 初始化只启动 YooAsset、创建 DefaultPackage。调用 `DownloadAsync` 后才挂载在线文件系统、更新清单并下载缺失资源，成功后 `IsReady` 为 true。
- `DownloadAsync` 支持 `MainUrl`、`FallbackUrl`、YooAsset `Parameters`、精确 `PackageVersion`、并发数、重试数和 `IProgress<ResourceDownloadProgress>`。指定精确版本时跳过最新版本查询；省略版本时由所选文件系统查询最新版本。
- 下载结果区分 `Downloaded`、`UpToDate`、`Failed`、`Cancelled`、`Busy` 和 `ResetRequired`。没有缺失文件时返回 `UpToDate`，同时提供可用版本。读取状态使用 `IsReady`、`PackageVersion` 和 `DownloadProgress`。
- 已就绪时重复请求同一来源和精确版本直接返回 `UpToDate`。切换来源、版本，或开始新一轮最新版本检查前，先释放资源和场景消费者并等待 `ResetAsync`。启动方持有发行选择和回退策略。
- 取消会结束当前等待，下载取消派发到 Unity 主线程，重置仍等待原生操作退出。调用 `ShutdownAsync` 会关闭通过本节模式入口管理的 DefaultPackage。所有入口从 Unity 主线程调用。

## 加载与释放

```csharp
var asset = await ResourceManager.LoadAssetAsync<TextAsset>(location, token);
if (asset == null)
{
    return;
}

try
{
    Debug.Log(asset.text);
}
finally
{
    ResourceManager.Release(asset);
}
```

每次成功加载持有一个 YooAsset 引用。同一对象加载两次，就归还两次。失败或取消返回 `null`。`Release(asset)` 每次归还一个引用，没有记录时返回 `false`。长期使用的资源由使用者持有到生命周期结束。

同步加载和回调重载遵循同一规则。`LoadSpriteAsync` 的返回值是图集里的 Sprite 副本，归还 Sprite 时释放它对应的图集引用。`ReleaseAllAssets()`、`ResetAsync()` 和 `ShutdownAsync()` 用于集中清理。

## 场景协调器

在资源系统就绪后，把 `ResourceManager.CreateSceneLoader()` 传给 `Game.Framework.Scenes.SceneManager.Instance.Initialize(...)`。适配器独立持有每次场景加载的句柄，避免同地址共享缓存影响场景所有权；退出时由场景管理器释放。

ShutdownAsync 会继续清理所有场景并释放资源缓存，即使某次场景卸载失败；最后聚合报告错误。旧 Play 会话的延迟清理不会清空新会话的缓存。

## 现有租约调用迁移

GameStarter 原先通过租约或作用域管理的调用需要迁移到直接加载与归还。UI 应在原生实例销毁完成后归还 Prefab。

## 接口迁移

启动时用 `InitializeAsync(ResourceMode, token)`。在线模式的远端地址、精确版本、进度和可选 YooAsset 参数通过 `RemoteDownloadSetting` 提供给 `DownloadAsync`。文件系统由管理器挂载。切换来源或版本前使用 `ResetAsync`。

业务通过静态 `ResourceManager` 调用。状态与 Handle 由 ResourceManager 统一管理。

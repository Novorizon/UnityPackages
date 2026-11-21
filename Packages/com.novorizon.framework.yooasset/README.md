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

Resource Demo 从 Package Manager Samples 按需导入，菜单 **Tools > Novorizon > Demos > Create Resource Demo Scene** 创建独立测试场景。Play 后界面先显示配置提示；只有点击按钮才初始化、加载或释放。它需要项目已有的 YooAsset 采集配置与资源，不自带 AssetBundle，也不会在导入时联网。

## 初始化与下载

```csharp
var resources = ResourceManager.Instance;
bool started = await resources.InitializeAsync(resourceMode, token);
if (!started)
{
    return;
}

if (resourceMode == ResourceMode.Online)
{
    var result = await resources.DownloadAsync(new RemoteDownloadSetting
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

## 所有权

`ResourceManager` 是按 location 共享的缓存。同地址并发异步加载共享任务和一个资源句柄，重复获取不会自动为每个调用者增加一份独立租约。

- `ReleaseHandle(location)` 影响这个地址的所有消费者，并使仍在等待的旧加载失效。只有功能或场景所有者确认全部消费者都结束后才能调用。
- `ReleaseAllAssets()` 与 `ShutdownAsync()` 是应用级操作，不能放在普通页面的 OnClose 中。
- `TryGetLoaded<T>` 只读已完成缓存，不启动加载、不创建加载 Task，也不改变所有权。
- UI 的 `IUIAssetLoader` 要求每次成功获取都可独立归还；不要把任意 UI 的 ReleasePrefab 直接接到全局 ReleaseHandle。应在项目适配器中持有专用句柄或实现明确的使用者计数。

详细说明见资源指南。官方源码核对基线为 tag `2.3.18` / `4b6a8ca4064d7a59ba129acec8fecf134e86236e`，本仓库未复制或修改 YooAsset 源码。合同编译/回归不等于真实 Bundle、下载、场景、Editor 或 IL2CPP 验证。

## 场景协调器

在资源初始化成功后，把 `ResourceManager.Instance.CreateSceneLoader()` 传给 `Game.Framework.Scenes.SceneManager.Instance.Initialize(...)`。适配器独立持有每次场景加载的句柄，避免同地址共享缓存影响场景所有权；退出时由场景管理器释放。

ShutdownAsync 会继续清理所有场景并释放资源缓存，即使某次场景卸载失败；最后聚合报告错误。旧 Play 会话的延迟清理不会清空新会话的缓存。

## 独立资源租约

AcquireAsync<T>(location, token) 每次取得独立 AssetLease<T>，通过 Asset 访问，结束使用后 Dispose。取消或释放不影响同地址的其他租约和旧共享缓存。取消未结束的原生 I/O 时，底层句柄延迟到加载结束再释放。

CreateScope 管理一组租约；UIView 中可 Track 此作用域到本次打开，CreateSceneScope(scene) 在指定场景卸载时自动取消并释放。ShutdownAsync 使租约失效并等待原生获取释放。GameStarter 已接入独立租约的 UI 加载器与配置文本读取。

## 接口迁移与高级初始化

改用 `InitializeAsync(ResourceMode, token)` 初始化。在线模式的远端地址、精确版本、进度和可选 YooAsset 参数通过 `RemoteDownloadSetting` 提供给 `DownloadAsync`。切换文件系统前使用 `ResetAsync`。

`InitializePackageAsync(parameters, token)` 是文件系统级入口，只在调用方需要单独挂载 DefaultPackage 文件系统时使用。挂载完成后 `PackageInitialized` 为 true，资源仍需可用清单才能读取。日常启动流程使用模式入口。沿用无参 `InitializeAsync()` 或文件系统级入口时，`ShutdownAsync` 释放管理器持有的资源，`ResetAsync` 同时销毁 DefaultPackage。

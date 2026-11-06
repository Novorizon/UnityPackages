# YooAsset 资源适配

这是可选包 `com.novorizon.framework.yooasset`，适用于 Unity 2022.3 与官方 YooAsset **2.3.18**。依赖同版本主框架；主框架和 UI 包都不强制使用 YooAsset。

在项目安装官方 YooAsset 2.3.18 后，再通过 Package Manager 添加：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.yooasset#v0.1.0-preview.25
```

已有项目可改用本地 `package.json` 或对应的 `.tgz`。不要将本包、另一份 Assets 源码和其他版本的 YooAsset 同时重复导入。

## 从哪里开始

| 需要完成的事 | 入口 |
| --- | --- |
| 初始化、加载、场景、图集、Shader、释放规则 | [资源指南](Documentation~/Resources.md) |
| 检查项目的 DefaultPackage 与缓存行为 | [Resource Demo](Samples~/Demo/README.md) |
| 使用其他资源库或纯 Resources 驱动 UI | [UI 资源接口指南](https://github.com/Novorizon/Framework/blob/codex/framework-unity-foundation/Framework/UI/Documentation~/Features.md#资源加载接口) |

Resource Demo 从 Package Manager Samples 按需导入，菜单 **Tools > Novorizon > Demos > Create Resource Demo Scene** 创建独立测试场景。Play 后界面先显示配置提示；只有点击按钮才初始化、加载或释放。它需要项目已有的 YooAsset 采集配置与资源，不自带 AssetBundle，也不会在导入时联网。

## 最小用法

```csharp
using System;
using Game.Resource;
using UnityEngine;

async System.Threading.Tasks.Task ReadTextAsync(string location)
{
    var resources = ResourceManager.Instance;
    if (!await resources.InitializeAsync())
        throw new InvalidOperationException("DefaultPackage initialization failed.");

    TextAsset text = await resources.LoadTextAssetAsync(location);
    Debug.Log(text.text);
}
```

所有调用从 Unity 主线程发起，主框架自动初始化主线程服务。`location` 是项目 YooAsset 采集配置中的地址；框架不会猜扩展名或把普通 Resources 路径自动转换成 YooAsset 地址。

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

## 精确版本初始化与下载

InitializeAsync(ResourceInitializationOptions, token) 支持 Parameters、PackageVersion、DownloadAll、下载并发、重试及 IProgress<ResourceDownloadProgress>。指定 PackageVersion 时直接切换该清单，不查询远端 latest/version 文件；全部下载成功后才进入 Initialized。取消会取消下载器，失败不会发布就绪状态。

已有资源版本需要切换时先 ShutdownAsync；项目应在加载游戏代码及资源消费者之前选定版本。CreateHostPlayModeParameters(mainUrl, fallbackUrl) 可显式提供服务器，不必修改全局默认地址。

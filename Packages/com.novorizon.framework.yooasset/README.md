# YooAsset 资源适配

这是可选包 `com.novorizon.framework.yooasset`，适用于 Unity 2022.3 与官方 YooAsset **2.3.18**。依赖同版本主框架；主框架和 UI 包都不强制使用 YooAsset。

在项目安装官方 YooAsset 2.3.18 后，再通过 Package Manager 添加：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.yooasset#v0.1.0-preview.2
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

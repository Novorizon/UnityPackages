# 资源初始化、加载与释放

本指南对应 `Game.Resource.ResourceManager` 与官方 YooAsset 2.3.18。可选程序集为 `Game.Framework.Resources.YooAsset`，使用项目中已安装的官方 `YooAsset` 程序集；不会向 UI 注入具体加载库。

## 初始化与运行模式

```csharp
using Game.Resource;

bool ready = await ResourceManager.Instance.InitializeAsync(ResourceMode.Offline);
if (!ready)
{
    return;
}
```

`ResourceMode.Offline` 挂载内置清单，`ResourceMode.EditorSimulation` 挂载编辑器模拟清单。项目需要为 **DefaultPackage** 配置采集规则和对应资源。挂载成功后注册一次 SpriteAtlas 请求事件，`IsReady` 为 true。

`ResourceMode.Online` 先启动 YooAsset 并创建包，此时还不能读取资源。随后用 `DownloadAsync(RemoteDownloadSetting, token)` 提供远端地址或自定义文件系统、版本和进度回调。它挂载清单并下载缺失文件，成功后返回可用版本。切换来源或版本前等待 `ResetAsync` 完成。调用代码和结果状态见[包说明](../README.md#初始化与下载)。

旧的无参 `InitializeAsync()` 使用 `YooAssetsSettings.CreateParameters()`。Editor 默认模拟；Player 中 `OFFLINE_PLAY_MODE` 选择内置资源，`HOST_PLAY_MODE` 选择配置了 `DefaultHostServer` 的远端文件系统，默认仍为内置资源。

自管多包时可使用官方 `ResourcePackage`。高级入口 `InitPackageAsync(ResourcePackage, InitializeParameters)` 只挂载传入包的文件系统和清单。管理器的资源读取接口使用它自己管理的 DefaultPackage。

## 加载普通资源

```csharp
using Game.Resource;
using UnityEngine;

TextAsset text = await ResourceManager.Instance.LoadTextAssetAsync(textLocation);
GameObject prefab = await ResourceManager.Instance.LoadGameObjectAsync(prefabLocation);
var material = await ResourceManager.Instance.LoadAssetAsync<Material>(materialLocation);
```

示意变量 `textLocation/prefabLocation/materialLocation` 由项目提供。也有对应同步 LoadAsset/LoadGameObject/LoadTextAsset、SpriteAtlas 以及 callback 重载。业务通常优先异步加载；回调重载在成功完成后调用回调，失败通过返回的 Task 传播，不能丢弃 Task 后期待一定执行回调。

同 location 的并发异步请求共享一次加载与句柄。缓存命中直接返回缓存资源；类型错误会抛出，不会偷偷把另一个类型覆盖进同地址。该地址还在异步加载时，同步加载会拒绝，以免创建第二份不明确所有权的句柄。

```csharp
if (ResourceManager.Instance.TryGetLoaded<TextAsset>(location, out var cached))
    RenderText(cached.text);
```

`RenderText` 是业务方法。TryGetLoaded 是高频刷新用的只读查询，未初始化、未加载、尚在加载、已释放或类型不匹配均返回 false；不会启动 IO，也不会创建加载 Task。只有明确需要资源时才调用 Load。

`IsValid(location)` 同时检查采集地址存在并且当前不需要远端下载；false 不等于“项目中永远没有这个资源”，它也可能表示尚未缓存。

## 实例与 Prefab

```csharp
GameObject instance = await ResourceManager.Instance.LoadInstantiateAsync(prefabLocation);
// 场景或功能结束时，由实例所有者 Destroy(instance)。
```

LoadInstantiateAsync 是加载并 Instantiate 的便捷方法。封装没有自动跟踪每个实例，也不会因 Destroy(instance) 自动 ReleaseHandle。缓存仍属于 location；所有实例及其他消费者真正不再依赖资源后，才由其共同所有者释放缓存。

对象池、UI 缓存和虚拟列表仍持有实例时，不能提前释放 Prefab 及依赖。Object.Destroy 会延迟到帧末，调用 Destroy/Clear/Dispose 的那一刻不等于原生对象已全部销毁。

## 场景

```csharp
using UnityEngine.SceneManagement;

Scene scene = await ResourceManager.Instance.LoadSceneAsync(sceneLocation, LoadSceneMode.Additive);
// ...
await ResourceManager.Instance.UnloadSceneAsync(scene);
```

默认重载是 Single，会切换当前场景；工具/演示优先使用 Additive 并保留明确的卸载所有者。相同地址的加载/卸载共享任务，未完成或已加载时不能以另一个 LoadSceneMode 再占用。通过返回的 Scene.handle 关联原地址，scene.name 不需要与 location 相同。

`LoadSceneHandleAsync` 暴露官方 SceneHandle，属于本封装管理的同一场景记录。不要一边直接 Release/Unload 该句柄，一边又让 ResourceManager 卸载同一场景。需要原生句柄完整控制时，直接使用自管 ResourcePackage，避免两套所有者。

## RawFile

当前 ResourceManager 没有 RawFile 包装接口。项目需要原始文件时，使用已初始化的官方 ResourcePackage，自行持有并释放 RawFileHandle：

```csharp
using System;
using YooAsset;

async System.Threading.Tasks.Task<string> ReadRawTextAsync(ResourcePackage package, string location)
{
    RawFileHandle handle = package.LoadRawFileAsync(location);
    try
    {
        await handle.Task;
        if (handle.Status != EOperationStatus.Succeed)
            throw new InvalidOperationException(handle.LastError);
        return handle.GetRawFileText();
    }
    finally
    {
        handle.Release();
    }
}
```

该地址必须以 RawFile 对应方式采集。另有 GetRawFileData/GetRawFilePath；数据读取、文本解码可能分配内存。此例中的句柄由方法自己拥有，不要再传给共享缓存释放接口。

## 图集与 Shader

```csharp
var sprite = await ResourceManager.Instance.LoadSpriteAsync(atlasLocation, spriteName);
ShaderManager.Instance.Warmup(shaderVariantsLocation);
```

LoadSpriteAsync 通过已缓存的 SpriteAtlas.GetSprite 获取指定名字，不是从任意 Sprite 地址加载。自动 `SpriteAtlasManager.atlasRequested` 接入采用项目约定路径 `Assets/GameData/SpriteAtlas/{atlasName}.spriteatlas`；不使用该目录的项目应主动加载自己的图集，不要误认为名字会自动解析为任意采集地址。自动请求事件在初始化注册，Shutdown 时退订。

ShaderManager.Warmup(string) 会同步加载 ShaderVariantCollection 后调用 WarmUp，也可以传入已加载的 ShaderVariantCollection。该操作可能阻塞主线程，应放在适当加载阶段；代码预热不能证明目标平台的 Shader 变体收集完整。

## 释放与关闭

| 方法 | 影响范围 |
| --- | --- |
| ReleaseHandle(location) | 该地址共享的等待任务与缓存句柄 |
| ReleaseAllAssets() | 本 ResourceManager 的所有普通资源缓存，不卸载其场景记录 |
| TryUnloadAsset(location, loopCount) | 释放地址缓存后请求 YooAsset 尝试卸载未使用资源 |
| TryUnloadUnusedAsset(loopCount) | 请求 YooAsset 与 Unity 清理未使用资产，可能有明显主线程成本 |
| ShutdownAsync() | 等初始化结束、卸载本封装持有的场景、释放普通资源并退订图集事件 |

ShutdownAsync 是应用级动作，不放在每个页面的 OnDestroy 中；它不销毁其他模块可能共用的整个 YooAsset 包。资源初始化失败或卸载异常应由调用者处理返回值/异常后决定重试。

ReleaseHandle 会使该地址的旧等待失效。旧加载迟到完成会释放自己的句柄，不应覆盖后来发起的新请求。新 Play 会话通过内部 RuntimeInitialize hook 重置本封装记录；不能把它当作普通场景切换的自动清理。业务仍需管理场景、功能和实例的生命周期。

## 对接 UI 的接口

UI 包只有 `IUIAssetLoader` 资源边界，默认 Resources。项目可选 YooAsset、Addressables、AssetBundle、内存模板或自己的加载系统，无需让 UI 包引用这些库。

`IUIAssetLoader` 的每次成功加载是一份可独立归还的租约；ResourceManager 的 Load 则返回共享缓存。两者不能简单一对一相接。项目适配器应使用专用官方 AssetHandle，或在共享缓存之外维护明确的 UI 使用者计数，并与其他消费者协调释放。UI 真实销毁前还要保持资源可用。

接口与示例见 [UI Features](https://github.com/Novorizon/Framework/blob/codex/tcp-client-integration/Framework/UI/Documentation~/Features.md#资源加载接口)。未使用 YooAsset 的项目直接安装 UI 包即可。

## 验证与排查

Resource Demo 的加载目标来自项目自己的 DefaultPackage；初次运行只显示配置提示。缺少采集器、地址无效、包构建与平台不一致、尚未初始化和类型错误都会失败，应先看初始化结果与 YooAsset Console 错误。

合同构建和回归可核对 API、共享加载、失效、释放和场景映射，不模拟真实 Bundle、网络、GPU、Editor 文件系统或 IL2CPP。上线前在实际 Unity 2022.3 + YooAsset 2.3.18 工程验证 Editor、Offline、Host 和目标设备。

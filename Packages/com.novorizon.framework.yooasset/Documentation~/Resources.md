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

管理器负责挂载 DefaultPackage 的文件系统。自管多包时，直接使用官方的 `ResourcePackage`。

## 加载普通资源

```csharp
var resources = ResourceManager.Instance;
var text = await resources.LoadAssetAsync<TextAsset>(textLocation, token);
if (text != null)
{
    try
    {
        RenderText(text.text);
    }
    finally
    {
        resources.Release(text);
    }
}
```

地址由项目提供。每次成功调用拥有一份独立引用，YooAsset 共享底层 Provider 和 Bundle。使用结束时按返回对象归还。多个调用收到同一个对象时，仍按成功加载次数归还。调用方应保存自己持有的对象，在刷新 UI 时继续使用它。

同步和异步回调接口同样转交一份引用。回调为空时跳过加载，资源缺失、类型不符或异步取消时返回 `null`。取消会归还该次等待的句柄，共享 Provider 可以继续服务其他加载。

`IsValid(location)` 检查采集地址和资源是否已在本地。所有加载与释放从 Unity 主线程发起。

## 实例与 Prefab

`LoadInstantiateAsync` 和 `LoadInstantiate` 返回的实例各持有一个 Prefab 引用。由实例的所有者销毁实例，并在原生销毁完成后调用 `Release(instance)`。`Object.Destroy` 延迟到帧末，池化或缓存的实例应在真正淘汰后归还引用。

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
using YooAsset;

async System.Threading.Tasks.Task<string> ReadRawTextAsync(ResourcePackage package, string location)
{
    RawFileHandle handle = package.LoadRawFileAsync(location);
    try
    {
        await handle.Task;
        if (handle.Status != EOperationStatus.Succeed)
        {
            UnityEngine.Debug.LogWarning(handle.LastError);
            return null;
        }
        return handle.GetRawFileText();
    }
    finally
    {
        handle.Release();
    }
}
```

该地址必须以 RawFile 对应方式采集。另有 GetRawFileData/GetRawFilePath；数据读取、文本解码可能分配内存。此例由调用方持有并释放原生 RawFileHandle。

## 图集与 Shader

```csharp
var sprite = await ResourceManager.Instance.LoadSpriteAsync(atlasLocation, spriteName, token);
// 使用完并从 UI 移除后，调用 ResourceManager.Instance.Release(sprite)。
```

`LoadSpriteAsync` 为每次调用取得图集 Handle，再从图集创建指定名称的 Sprite 副本。归还 Sprite 会释放该副本和对应的图集引用。Unity 自动绑定图集时按 `Assets/GameData/SpriteAtlas/{atlasName}.spriteatlas` 加载，引用由管理器保留到集中清理。

`ShaderManager.Warmup(string)` 持有临时引用直到预热完成。需要在功能生命周期内保留着色器集合时，先加载集合，再调用接收集合的 `Warmup`。

## 释放与关闭

| 方法 | 影响范围 |
| --- | --- |
| `Release(asset)` | 归还该对象的一次加载引用。没有记录时返回 false。 |
| `ReleaseAllAssets()` | 清理所有普通资源引用、待完成的普通加载和自动绑定的图集。 |
| `TryUnloadAsset(location, loopCount)` | 请 YooAsset 回收该地址的闲置资源。 |
| `TryUnloadUnusedAsset(loopCount)` | 请 YooAsset 与 Unity 清理未使用资源。 |
| `ResetAsync()`、`ShutdownAsync()` | 结束当前包的资源操作并统一清理。 |

窗口关闭和功能结束时逐个归还资源。集中清理前，由项目先关闭资源消费者。旧 Play 会话里的未完成请求无法重新登记资源。

## 对接 UI 的接口

UI 包通过 `IUIAssetLoader` 接入项目的资源系统。用 `LoadAssetAsync<GameObject>` 取得 Prefab，在 UI 工厂确认原生实例销毁后调用 `Release(prefab)`。项目可用 `ICancellableUIAssetLoader` 把关闭或替换窗口的取消令牌传入加载。

详情见 [UI 功能文档](../../com.novorizon.framework.ui/Documentation~/Features.md#资源加载接口)。

## 验证与排查

Resource Demo 的加载目标来自项目自己的 DefaultPackage；初次运行只显示配置提示。缺少采集器、地址无效、包构建与平台不一致、尚未初始化和类型错误都会失败，应先看初始化结果与 YooAsset Console 错误。

合同构建和回归可核对 API、独立引用、取消、释放和场景映射，不模拟真实 Bundle、网络、GPU、Editor 文件系统或 IL2CPP。上线前在实际 Unity 2022.3 + YooAsset 2.3.18 工程验证 Editor、Offline、Host 和目标设备。

启动和资源重置失败时返回 `false`，下载返回状态，场景加载失败返回默认 `Scene`。用 `scene.IsValid()` 检查结果。资源未就绪时 `CreateSceneLoader()` 返回 `null`。Unity API 仍须在已初始化的主线程调用，错用线程时记录错误并结束当前操作。

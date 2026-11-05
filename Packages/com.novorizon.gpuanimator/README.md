# GpuAnimator

3.2.0-preview.1 的[独立包验证与边界](Documentation~/package-validation.md)已补齐；可选 [Framework Pool Sample](Samples~/FrameworkPool/README.md)复用基础池，回收前清除播放状态。

[中文使用文档](Documentation~/index.md) · [API](Documentation~/api.md) · [接入与对象池](Documentation~/integration.md) · [常见问题](Documentation~/troubleshooting.md)

[美术资源规范与交付清单](Documentation~/art-guidelines.md)：模型、骨骼权重、动画、贴图、目录约定和烘焙验收。

[编辑器测试与项目验收](Documentation~/editor-testing.md)：启用包内 EditMode 测试，检查重烘焙引用、导入和失败恢复。当前 Shader 使用 Shader Model 3.5。

## 快速开始

`main` 是独立的 GameObject/MonoBehaviour 版本；原 PureMVCFramework ECS 版本保留在 [`ecs` 分支](https://github.com/Novorizon/GpuAnimator/tree/ecs)。当前示例面向 Unity 2020.2+ 的 **Built-in Render Pipeline**。

3.0 使用完整的数据类型名 `AnimationData` 和 `AnimationClipData`；组件入口仍为 `GpuAnimator`。从 2.x 升级请替换源码中的旧类型名，`AnimationData.FromBytes` 可以读取旧元数据，见[命名迁移说明](Documentation~/integration.md#从-2x-迁移数据类型名称)。

1. 在 Unity Package Manager 通过 Git URL 安装：`https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.gpuanimator#v0.1.0-preview.8`。
2. 按[美术规范](Documentation~/art-guidelines.md)准备模型与动画，在 **GpuSkinning → Baker** 中先 Validate 再 Bake；产物与接入步骤见[入门指南](Documentation~/getting-started.md)。
3. 资源由业务或 Framework 加载，播放器接收已配置的 Renderer 和解析后的数据：

```csharp
// loadedData、instance、renderer、clipName 均由调用方提供。
// 数据解析一次后，可以供同一模型的多个实例共享。
var sharedData = AnimationData.FromBytes(loadedData.bytes);
var player = instance.GetComponent<GpuAnimator>();
if (player == null)
    player = instance.AddComponent<GpuAnimator>();

player.AutomaticUpdate = true;
if (player.Initialize(renderer, sharedData))
    player.TryPlay(clipName);
```

## 可导入示例

在 **Window → Package Manager → Gpu Animator → Samples** 中点击 Import。示例提供脚本和操作说明，需要使用你自己的匹配烘焙资源；仓库未附带角色模型或演示场景。

| 示例 | 内容 | 操作说明 |
| --- | --- | --- |
| Basic Playback | Inspector 注入资源，循环播放、单次动作与完成回调 | [README](Samples~/BasicPlayback/README.md) |
| Manual Crowd | 共享数据和材质，集中调用手动 Tick，管理示例实例 | [README](Samples~/ManualCrowd/README.md) |

导入后按示例 README 配置字段，进入 Play Mode，通过组件右上角菜单执行示例操作。动画名称由 Inspector 或业务代码提供。

## 中文文档

- [文档首页](Documentation~/index.md)
- [安装、烘焙和首次播放](Documentation~/getting-started.md)
- [美术资源规范与交付清单](Documentation~/art-guidelines.md)
- [完整 API 与状态说明](Documentation~/api.md)
- [Framework 接入、资源生命周期、对象池及手动更新](Documentation~/integration.md)
- [故障排查和当前设计约束](Documentation~/troubleshooting.md)
- [更新记录](CHANGELOG.md)

---

`main` provides a standalone GameObject/MonoBehaviour player. It depends only on
Unity and does not load assets or require an ECS framework. The previous
PureMVCFramework entity/component implementation is preserved on the
[`ecs` branch](https://github.com/Novorizon/GpuAnimator/tree/ecs).

Version 2 replaces the static, entity-based API with a `GpuAnimator` component.
Existing ECS integrations can continue using the `ecs` branch.

## Installation

Add this Git URL in Unity Package Manager:

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.gpuanimator#v0.1.0-preview.8
```

The package targets Unity 2020.2 or later. The included shaders use the Built-in
Render Pipeline. Runtime and Editor code compile in separate assemblies; no
external framework packages or assembly GUID references are required.

## Caller-supplied resources

Load the prefab, mesh, material, animation texture and baked `.bytes` metadata
through your own loader. The renderer you pass in must already have its mesh
and GPU animation material configured. The generated material carries the
animation texture, bone count and texture dimensions.

Decode the metadata once and share it across instances:

```csharp
AnimationData sharedData = AnimationData.FromBytes(loadedData.bytes);
```

Then initialize each instance with its renderer and the shared metadata:

```csharp
GpuAnimator player = instance.GetComponent<GpuAnimator>();
if (player == null)
    player = instance.AddComponent<GpuAnimator>();

if (player.Initialize(renderer, sharedData))
    player.TryPlay(clipName);
```

Here `loadedData`, `instance`, `renderer` and `clipName` come from business code.
`Initialize(sharedData)` is also available when the renderer is on the same
GameObject. Newly generated prefabs already include a `GpuAnimator` component.
Initialization does not choose or start an animation.

`FromBytes` only decodes the existing baked format; it does not fetch a path or
load an asset. Keep loaded assets alive while their renderers use them. The
player does not clone materials or unload/destroy caller-owned resources.

## Playback

```csharp
bool started = player.TryPlay(clipName);
bool startedOnce = player.TryPlay(clipName, loop: false);
bool startedStaggered = player.TryPlay(clipName, randomizeStart: true, loop: true);

player.TryPlay(clipName, callback: (sender, userdata) =>
{
    // Called once when a non-looping animation completes.
}, loop: false);
```

`TryPlay` returns `false` before initialization, for unknown names or invalid clip
metadata. It starts at the first frame unless `randomizeStart` is enabled. A
missing loop override uses the baked clip setting without changing shared data.
`Play(clipName, callback, userdata)` is a convenience method with a randomized
starting phase. Select animations when business state changes rather than
restarting them every frame.

By default, the component advances with `Time.deltaTime` in `Update`. To drive
many instances from an external update loop, select manual updates:

```csharp
player.AutomaticUpdate = false;
player.Tick(deltaTime);
```

`Tick` is explicit and always advances valid playback, so disable automatic
updates before using it. `Stop()` stops playback and keeps the current pose.
`Clear()` also releases the injected references for pooling or reinitialization.
Neither invokes the completion callback. `IsInitialized`, `IsPlaying`,
`CurrentClipName` and `CurrentFrame` expose playback state.

Renderers and property blocks are cached. If business code changes the renderer's
property block directly during playback, call `player.SetPropertyBlock()` to
refresh the cached values before the next tick.

## Updating baked assets

GPU animation uses a rotation quaternion, translation and one uniform scale per
bone, followed by four-weight linear blend skinning. Use matching X/Y/Z scale;
independent axis scale and shear are outside this data format.

Rebake animation textures produced before the uniform-scale and decoding fixes.
Uniform scale is now stored
directly instead of clamping the scale vector's magnitude, and byte decoding
matches the encoder's binary fractions. Old textures cannot recover scale
values already lost during baking. Version 3 renames the data classes while
keeping their serialized fields and the runtime assembly name. `AnimationData.FromBytes`
maps the former data type names so existing `.bytes` remain readable. Textures
already produced by the fixed baker do not need rebaking for this naming change.

## Framework integration

Framework adapters only need to load/cache assets, call `Initialize` and select
clips. They may optionally drive manual ticks and call `Clear` when recycling an
instance. Async loading and cancellation stay in the adapter, which can check
instance lifetime before injecting a completed load. The GPU player has no
dependency on Framework, its resource manager or its entity types.

![image](https://github.com/Novorizon/GpuAnimator/raw/main/GPU%20Skinning.png)

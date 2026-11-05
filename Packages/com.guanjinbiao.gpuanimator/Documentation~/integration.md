# 项目接入

`main` 的播放器只依赖 Unity。项目中的 Framework、Addressables、AssetBundle 或自定义资源系统可以通过同一组公开 API 接入，不需要修改播放器，也不需要继承项目中的 Entity 或 Component 类型。

## 资源所有权

| 层 | 负责内容 |
| --- | --- |
| 烘焙工具 | 为同一套角色生成 Mesh、材质、动画 PNG、元数据和 prefab |
| 资源层 | 加载/缓存资源，解码 `.bytes`，保证引用仍在使用时资源有效，并最终释放资源 |
| 业务逻辑或框架适配层 | 实例化对象，绑定配套资源，选择 Clip 名称、循环方式及更新模式，处理回收 |
| GpuAnimator | 维护每个实例的播放状态，计算帧索引，将帧属性写入 Renderer |

在资源层按角色或资源标识缓存一次解码结果：

```csharp
// loadedMetadata 是调用方加载的 TextAsset。
AnimationData sharedData =
    AnimationData.FromBytes(loadedMetadata.bytes);
```

同一套资源的多个实例共享 `sharedData`、Mesh、材质和动画 PNG；每个实例有独立的 `GpuAnimator` 与 PropertyBlock，不必为每个人创建一个 Material。生成材质已经开启 Instancing；共享资源并不保证任意渲染条件下都能合批，请用项目中的 Frame Debugger/Profiler 检查。

播放器不保留原 TextAsset 引用，解码后它的生命周期可由资源层单独管理。Renderer 使用的 Mesh、材质和纹理则必须保持有效；`Clear` 仅释放播放器自己的缓存引用，不会卸载资源，也不会解除 Renderer 对材质/Mesh 的绑定。播放中的共享元数据不要原地修改。

## 绑定与业务状态切换

在实例已有配套 Mesh/材质后绑定一次，业务状态变化时播放：

```csharp
player.AutomaticUpdate = true;
bool bound = player.Initialize(renderer, sharedData);
if (bound)
    player.TryPlay(requestedClipName, loop: true);

// 后续业务状态变化时调用，而不是每帧调用。
bool started = player.TryPlay(nextClipName, callback: (sender, context) =>
{
    // 在这里通知项目自己的状态机。
}, userdata: stateContext, loop: false);
```

这里的名称和上下文均来自调用方，没有内置的 Idle/Run/Attack 状态，也不依赖某个 Framework 的资源 API。若要保留烘焙 Clip 的循环设置，省略 `loop`。

`Initialize` 每次都会先 Clear，包括失败时。重绑之前保留旧绑定并自动回退，不是它的行为；适配层需要的话，应先检查自己的资源是否准备完整，再调用初始化。

## 统一驱动多个实例

每个交给外部管理器驱动的组件都先关闭自动推进：

```csharp
player.AutomaticUpdate = false;
player.Initialize(renderer, sharedData);
player.TryPlay(requestedClipName, randomizeStart: true);

// 在项目自己的统一更新中，对仍有效的每个 player 调用。
player.Tick(deltaTime);
```

`Tick` 即使在 `AutomaticUpdate == false` 时仍会推进，这是手动调度接口的用途。调用方负责时间来源、有效实例列表和每帧调用次数。暂停时可以不调用 Tick，或传入零；使用 `Time.unscaledDeltaTime` 则不受 Unity 的 timeScale 影响。

组件被禁用或对象停用会停止 Unity 自动 `Update`，但外部代码仍可能显式调用 Tick。回收时先从自己的调度列表移除，再 Stop/Clear，避免管理器继续驱动已复用的对象。

参考 [Manual Crowd 示例](../Samples~/ManualCrowd/README.md)；具体自动和手动行为见 [API 参考](api.md#tick-与自动更新)。

## 对象池与异步加载

回收实例时执行 `Clear()`，取消尚未自然结束的回调并释放播放器缓存。再次使用时重新配置 Renderer 的资源，再设置更新模式、初始化并选择动画：

```csharp
// 回收：先移出外部更新列表。
player.Clear();

// 再次取出：Renderer 已配置为本次角色对应的 Mesh/材质。
player.AutomaticUpdate = useAutomaticUpdate;
if (player.Initialize(renderer, sharedData))
    player.TryPlay(requestedClipName);
```

Clear 不重置 `AutomaticUpdate`，因此复用从手动更新组借出的对象时，不要假定它已经恢复自动模式。Clear 也保留 Renderer 上的最后姿态；需要隐藏对象或重置其他渲染状态时由对象池处理。

异步资源加载仍由适配层管理。每次绑定请求记录一次实例代次或取消标记；回收、切换角色或发出新请求时使旧标记失效。加载完成后，只有对象仍存活且请求仍属于当前代次，才调用 Initialize。过期请求的资源按加载器的所有权规则释放。这样可以避免旧请求在对象已复用后覆盖新角色。

本包没有取消令牌或请求 ID API，不应把异步加载的等待逻辑放入 `GpuAnimator`。同样，播放器不会在 `OnDisable` 中自动 Clear；复用生命周期由适配层明确决定。

## PropertyBlock 协作

业务代码可用 PropertyBlock 修改实例颜色等属性。若直接写 Renderer，应先保留已有内容，写完后刷新播放器的缓存：

```csharp
MaterialPropertyBlock block = new MaterialPropertyBlock();
renderer.GetPropertyBlock(block);
block.SetColor("_Color", instanceColor);
renderer.SetPropertyBlock(block);
player.SetPropertyBlock();
```

此片段用于演示缓存协作，当前 Shader 的 `_Color` 未声明为实例化属性，逐实例设置可能破坏自动 GPU Instancing；若需保留合批，请只设置已声明的实例化属性，或扩展 Shader 以支持实例颜色。

播放器下一次应用帧时会保留缓存中的其他属性，并覆盖 `_FrameIndex` 和 `_BlendFrameIndex`。请把这两个属性交给播放器管理。若其他系统之后再次改写 Renderer 的块，也需要再次刷新；反复覆盖同一个 Renderer 的多套播放器没有独立的动画状态输出。

## 从 2.x 迁移数据类型名称

| 旧源码类型 | 新源码类型 |
| --- | --- |
| `GpuSkinningAnimData` | `AnimationData` |
| `GpuSkinningAnimClip` | `AnimationClipData` |

替换字段、参数和泛型中的类型引用，解码入口改为 `AnimationData.FromBytes(loadedMetadata.bytes)`。播放器仍是 `GpuAnimator`，资源注入、播放和 Tick 接口的行为保持一致；新数据类型文件为 `GpuAnimator/Runtime/AnimationData.cs`。

`FromBytes` 的兼容 Binder 能读取旧 `.bytes` 的根对象、Clip 及 Clip 数组类型，原序列化字段名仍保留。仅为本次命名迁移不需要重新烘焙现有修复版动画 PNG。新类型名是源码 API 变更，因此包版本升级为 3.0.0；调用方应同步更新类型引用。

已通过 Package Manager 导入到 `Assets/Samples/` 的旧示例是项目内的副本，不会随包升级自动改写。请更新其中的旧类型引用，或移除旧版示例目录后重新导入当前 Samples。

兼容读取通过 `AnimationData.FromBytes` 提供；自行直接调用未配置 Binder 的 BinaryFormatter 不会获得这项映射。新烘焙元数据使用新类型名，因此旧 `ecs` 分支应继续使用对应版本生成的元数据，不能直接读取 3.x 新生成的 `.bytes`。

## 从 ecs 分支迁移

原来的 Entity 由项目自己继续管理；在它所对应的 GameObject 上保留一个 `GpuAnimator` 组件即可。资源加载和缓存逻辑从旧的静态 `AddAnimationComponent` 入口移到资源层，初始化后改为调用实例的 `TryPlay`。旧的 ComponentSystem 更新则由组件自动 Update 或项目统一 Tick 替代，二者选择一种。

回调类型从 `Action<Entity, object>` 改为 `Action<GpuAnimator, object>`；需要回到业务 Entity 时，可通过 userdata 或适配层持有的映射处理，不必让播放器反向依赖框架。

现有 ECS 项目若暂不迁移，可继续使用 [`ecs` 分支](https://github.com/Novorizon/GpuAnimator/tree/ecs)。它保留旧框架结构，不能和 `main` 的组件代码同时安装。

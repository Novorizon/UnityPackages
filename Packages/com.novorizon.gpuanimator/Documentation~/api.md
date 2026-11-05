# API 参考

本文对应 `main` 分支的 3.x 组件 API。类型没有命名空间，位于 `GpuAnimator` 程序集。

## GpuAnimator

```csharp
public sealed class GpuAnimator : MonoBehaviour
```

### 状态属性

| 属性 | 语义 |
| --- | --- |
| `bool IsInitialized { get; }` | 已绑定的 Renderer 仍有效且元数据引用不为空；不表示材质/贴图配对已经验证 |
| `bool IsPlaying { get; }` | 正在播放且仍处于已初始化状态 |
| `string CurrentClipName { get; }` | 当前/最近播放的 Clip 名称；尚未播放或 Clear 后为 `null` |
| `int CurrentFrame { get; }` | 动画纹理中的全局帧索引；尚未播放或 Clear 后为 `-1` |
| `bool AutomaticUpdate { get; set; }` | 默认 `true`；控制组件的 `Update` 是否调用 `Tick(Time.deltaTime)` |

`AutomaticUpdate` 是代码属性，不是序列化 Inspector 字段。它不会被 `Initialize`、`Stop` 或 `Clear` 重置，对象池复用时应显式设置。

### Initialize

```csharp
public bool Initialize(Renderer targetRenderer, AnimationData animationData)
public bool Initialize(AnimationData animationData)
```

绑定调用方提供的 Renderer 和元数据。第二个重载使用本组件同一 GameObject 上的 `GetComponent<Renderer>()`，不会搜索子对象。

每次调用都**先执行 Clear**，因此原播放状态和待执行回调会被取消。Renderer、元数据或 `clips` 为 `null`，或 `totalFrame <= 0` 时返回 `false`，组件保留清空后的状态。成功返回 `true`，缓存 Renderer 的 PropertyBlock，但不选择 Clip、不显示某个新帧，也不自动开始播放。

初始化不会加载资源、查找或更换材质、绑定纹理，也不会验证骨骼数、纹理尺寸或每一个 Clip。调用方应先配置 Renderer，并保持元数据及渲染资源在使用期间有效。

### TryPlay

```csharp
public bool TryPlay(
    string clipName,
    Action<GpuAnimator, object> callback = null,
    object userdata = null,
    bool randomizeStart = false,
    bool? loop = null)
```

按 `StringComparison.Ordinal` 精确匹配名称，区分大小写。成功时替换当前播放，清零计时余量，立即写入首个显示帧，并返回 `true`。再次播放同名动画也会重新开始，因此通常只在业务状态改变时调用，不要每帧重复调用。

| 参数 | 行为 |
| --- | --- |
| `clipName` | 要播放的 `AnimationClipData.name` |
| `callback` / `userdata` | 非循环动画自然结束后回调一次，并原样传回 userdata |
| `randomizeStart` | 默认 `false`；为 `true` 时在动画前段随机选择起始偏移，用于错开群体相位 |
| `loop` | `null` 使用烘焙的 `clip.loop`；`true`/`false` 仅覆盖当前实例的此次播放，不修改共享元数据 |

随机偏移沿用原实现：`Random.Range(0, (endFrame - startFrame) / 3)` 的整数结果，上界不包含；很短的 Clip 可能始终从第一帧开始。非循环动画也可以随机起点，此时只播放该偏移之后的部分。

以下情况返回 `false`，不会停止或替换已经存在的播放：

- 尚未初始化、名称为空、没有找到匹配名称，或 `animationData.clips` 已被改为 `null`。
- 匹配 Clip 的 `startFrame < 0`、`endFrame < startFrame` 或 `endFrame >= totalFrame`。
- 匹配 Clip 的 `frameRate <= 0`，或为 NaN/Infinity。

空 Clip 项会被跳过；若名称重复，使用第一个匹配项，第一个匹配项无效时直接失败。组件不会检查 PNG 的实际像素数和 Renderer 的材质设置。

### Play

```csharp
public void Play(
    string clipName,
    Action<GpuAnimator, object> callback = null,
    object userdata = null)
```

兼容原有随机起始相位习惯的便捷入口，等价于 `TryPlay(clipName, callback, userdata, randomizeStart: true)`。它没有返回值，循环方式采用烘焙设置。需要确定性起点、失败结果或循环覆盖时使用 `TryPlay`。

### Tick 与自动更新

```csharp
public void Tick(float deltaTime)
```

显式推进播放，单位为秒。未在播放时直接返回；`deltaTime <= 0`、NaN 或 Infinity 被忽略。它累积不足一帧的时间，并能一次跨过多个烘焙帧；循环动画按帧数取模，一次性动画停在 `endFrame`。

`Tick` 不检查 `AutomaticUpdate`。由外部管理器调用时，应先设置 `AutomaticUpdate = false`，否则同一时间既有自动更新又有手动 Tick，会重复推进。自动模式使用 `Time.deltaTime`；手动模式的时间缩放、暂停和使用 scaled/unscaled 时间由调用方决定。

`startFrame` 与 `endFrame` 都包含在 Clip 内。长度为 N 的一次性动画从第一帧开始，其逻辑播放时长为 N 个采样帧间隔。正常逐采样推进时，末帧也占一个间隔；一次 Tick 跨越多帧或完成边界时，被跳过的采样帧不保证实际显示。

自然结束时，在同一次 Tick 中先应用末帧、设 `IsPlaying = false` 并清除待执行回调，再调用捕获的回调。回调中可以开始下一段动画或 Clear；若调用 TryPlay，新姿态可能在任何屏幕渲染发生前覆盖刚提交的末帧，因此不保证末帧实际显示一个完整间隔。

### Stop 与 Clear

```csharp
public void Stop()
public void Clear()
```

`Stop` 结束当前播放，保留绑定、Clip 名称、帧索引和已显示姿态；它不提供继续播放接口，后续 `TryPlay` 会重新开始。`Clear` 先 Stop，再释放组件缓存的 Renderer、元数据和 PropertyBlock 引用，并清空 Clip/帧状态。

| 操作之后 | 初始化绑定 | `IsPlaying` | Clip/帧状态 | Renderer 当前姿态 | 原待执行回调 |
| --- | --- | --- | --- | --- | --- |
| `Stop()` | 保留 | `false` | 保留 | 保留 | 取消，不调用 |
| `Clear()` | 清除 | `false` | `null` / `-1` | 保留 | 取消，不调用 |
| `Initialize(...)` 成功 | 替换为新绑定 | `false` | `null` / `-1` | 不主动改变 | 取消，不调用 |
| `Initialize(...)` 失败 | 清除 | `false` | `null` / `-1` | 不主动改变 | 取消，不调用 |
| `TryPlay(...)` 失败 | 保留 | 保留原状态 | 保留原状态 | 保留 | 保留原回调 |
| 非循环动画自然结束 | 保留 | `false` | 保留末帧 | 提交末帧 | 先清除，再调用捕获的委托一次 |

自然结束行描述的是进入完成回调前的状态；回调本身可以启动新动画或清空组件，从而再次改变这些状态。

这些操作都不重置 `AutomaticUpdate`。Clear 不会清空 Renderer 上已经设置的 PropertyBlock，也不销毁、卸载或还原材质和纹理。循环动画不会触发完成回调；新一次成功播放也会替换旧回调，而不会先调用旧回调。

### SetPropertyBlock

```csharp
public void SetPropertyBlock()
```

从绑定 Renderer 重新读取 PropertyBlock，刷新组件缓存。播放器随后写入 `_FrameIndex`、`_BlendFrameIndex`，并把整份缓存块应用到 Renderer。

初始化和成功的 TryPlay 会自动刷新缓存。如果其他代码在播放期间直接改写 Renderer 的 PropertyBlock，修改完成后调用此方法，避免下一次 Tick 用旧缓存覆盖那些属性。具体写法见[接入文档](integration.md#propertyblock-协作)。

## 元数据类型

```csharp
[Serializable]
public class AnimationData
{
    public int texWidth;
    public int texHeight;
    public byte[] texBytes;
    public AnimationClipData[] clips;
    public int totalFrame;
    public int totalBoneNum;

    public static AnimationData FromBytes(byte[] bytes);
}
```

`FromBytes` 只解码调用方已经加载的字节，不接受资源路径、不执行加载或缓存。它沿用烘焙工具的 BinaryFormatter 格式：传 `null` 抛出 `ArgumentNullException`，反序列化结果不是该类型时抛出 `SerializationException`；损坏或不兼容的内容也可能抛出反序列化异常。使用本工具生成的 `.bytes`，并在资源加载边界处理解码失败。

`FromBytes` 不验证各字段的业务正确性。它成功返回也不表示可以播放任意 Clip。当前 PNG 导出流程没有用 `texBytes` 内嵌纹理；实际骨骼数据仍由材质绑定的动画 PNG 提供。

兼容读取旧版 `GpuSkinningAnimData` / `GpuSkinningAnimClip` 保存的元数据：内部 Binder 将旧类型名映射到新类型，字段名称与数据含义保持不变。新烘焙使用 `AnimationData` / `AnimationClipData` 类型名；旧公开类型名不再作为源码 API 提供。

```csharp
[Serializable]
public class AnimationClipData
{
    public string name;
    public int startFrame;
    public int endFrame;
    public bool loop;
    public float frameRate;

    public AnimationClipData(
        string name, int startFrame, int endFrame, bool loop, float frameRate);
    public int Length();
}
```

`Length()` 返回 `endFrame - startFrame + 1`，不额外做合法性检查。帧索引属于整张动画纹理，不是每个 Clip 独立从零编号。

播放器直接持有元数据和 Clip 对象引用，没有深拷贝。可在多个实例之间共享同一个解码结果；播放期间请保持它及 `clips` 数组、Clip 字段稳定。实例的循环覆盖和当前帧不会修改这些共享字段。

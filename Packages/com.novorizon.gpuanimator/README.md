# GpuAnimator

Unity 2022.3 的 GPU 骨骼动画包。烘焙器将动画写入纹理；每个 GpuAnimator 负责一个 Renderer 的播放，GpuAnimatorManager 使用 Framework 单例统一推进自动模式的播放器。

## 安装与首次播放

先安装同一发行标签的 Framework，或直接使用 framework.all。独立包地址：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.gpuanimator#v0.1.0-preview.25
```

聚合安装默认包含 [URP 适配](../com.novorizon.gpuanimator.urp/README.md)，目标 Unity 2022.3.62f1 / URP 14.0.12，Baker 默认 URPUnlit。单独安装基础包并使用 Built-in 时，显式选择 Unlit/PBR。

在 GpuSkinning → Baker 执行 Validate 再 Bake。加载同一套 prefab、Mesh、材质、动画纹理和 bytes，解析数据后注入：

```csharp
var data = AnimationData.FromBytes(bytes);
var player = instance.GetComponent<GpuAnimator>();
if (player == null)
{
    player = instance.AddComponent<GpuAnimator>();
}

player.AutomaticUpdate = true;
if (player.Initialize(renderer, data))
{
    player.TryPlay(clipName);
}
```

同一套数据可在多个实例间共享。资源加载、持有和释放由项目负责，播放器不内置资源路径或业务动画状态机。

## 调度与通知

```csharp
GpuAnimatorManager.Instance.Initialize();
GpuAnimatorManager.Instance.AnimationChanged += OnAnimationChanged;
```

Manager 和播放器提供 Started、Changed、Completed、Stopped 通知；播放同一 Clip 触发 Started，循环不会触发 Completed。退出时取消项目自己的订阅。

自动模式由 Manager 统一 Tick，播放器没有 Update。手动推进时设置 `AutomaticUpdate = false`，不要重复推进。回收实例前调用 Clear，再释放它依赖的资源。

## 文档和示例

- [使用说明](Documentation~/index.md) · [API](Documentation~/api.md)
- [美术与烘焙](Documentation~/art-guidelines.md) · [项目接入](Documentation~/integration.md)
- [编辑器测试](Documentation~/editor-testing.md) · [排错](Documentation~/troubleshooting.md)
- [Basic Playback](Samples~/BasicPlayback/README.md)：单个播放器。
- [Manual Crowd](Samples~/ManualCrowd/README.md)：共享资源与手动推进。
- [Framework Pool](Samples~/FrameworkPool/README.md)：池化与回收。

示例需项目提供匹配的烘焙资源。基础 Shader 使用 Built-in、Shader Model 3.5；URP 由独立适配包提供，HDRP 未适配。真实烘焙、渲染和设备结果以 Unity 验收为准。

# 快速开始

## 安装

先安装同一发行标签的 Framework，或使用 Framework All。使用 Unity 2022.3 或更高版本，在 **Window > Package Manager** 中选择 **+ > Add package from git URL**，输入：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.gpuanimator#v0.1.0-preview.35
```

GPU 动画包包含 URP 14 和 Built-in Shader；Built-in 工程可显式选择 Unlit/PBR。包内 Shader 使用 `#pragma target 3.5`，目标图形 API 和 GPU 需支持相应的整数运算和实例化能力。包内运行时和 Editor 代码已有独立程序集；`main` 不需要 PureMVCFramework。若调用代码自身使用 asmdef，请为它添加对 `GpuAnimator` 程序集的引用。

ECS 项目也可复用此播放组件，由业务适配实体与 GameObject 的关系；不需要切换到另一条源码分支。

## 准备原始资源

打开 **GpuSkinning > Baker**，可以配置源目录和输出目录。默认源目录为 `Assets/Artworks/GpuRole/`，工具扫描它的每个直接角色子目录。以角色 `Hero` 为例：

```text
Assets/Artworks/GpuRole/
└── Hero/
    ├── Character.fbx           # 恰好一个 FBX，文件名不限
    └── res/
        ├── Hero.png
        ├── Hero_mask.png       # 可选
        ├── Idle.anim
        └── Walk.anim
```

这里的 `Idle`、`Walk` 仅为文件示例，播放器没有内置这些名称。运行时使用的是 `AnimationClip.name`，请按项目实际 Clip 名称播放。

输入必须包含 `res/<Role>.png` 和至少一个独立 `.anim`，不再要求输入 `.mat`。角色中的 FBX 文件名、SMR 节点名不必包含角色名；FBX 中必须恰好一个 SMR 和一个 submesh，并具备有效的骨骼、bindposes、四权重、法线和 UV0。模型变换还需满足统一缩放数据格式。详细的制作要求与交付验收见[美术资源规范](art-guidelines.md)。

## 烘焙

建议通过 **GpuSkinning > Baker** 执行：

1. 设置位于本项目 `Assets/` 内的源目录和输出目录，二者不能相同或互相包含；源目录需存在，输出目录可以尚未创建。默认选择 URPUnlit；需要光照和实时阴影时选择 URPLit，Built-in 工程显式选择 Unlit/PBR。最大动画纹理尺寸默认 `2048`，可选 `256` 到 `16384` 的二次幂；需结合目标平台能力设置。
2. 退出 Play 模式，等待资源导入和脚本编译结束，再执行 **Validate all roles**。工具会扫描全部角色、完整采样并预检输出文件、PNG、Shader 和材质属性，但不保存烘焙输出；逐角色查看结果，修正输入错误。
3. 执行 **Bake all roles**。角色失败时报告原因并继续其他角色；取消批处理会保留已成功保存的角色。窗口显示逐角色结果与汇总，并同步输出到 Console；不会另外生成报告文件。

原快捷菜单继续使用默认源目录和 `Assets/Main/Prefabs/GpuRole/` 输出目录，直接开始批量烘焙：

| 菜单 | 输出材质使用的 Shader |
| --- | --- |
| **GpuSkinning > No PBR** | `Custom/GpuSkinningAnimation` |
| **GpuSkinning > PBR** | `Custom/GpuSkinningAnimationPBR` |

上述操作处理扫描到的全部角色，不使用 Project 窗口当前选择。先用一个角色验证流程，再放入完整角色集合。Validate 也需要逐帧采样，大量角色或长动画可能耗时。

以 `Hero` 为例，输出为：

```text
Assets/Main/Prefabs/GpuRole/Hero/
├── Hero_DynPre.prefab
├── Hero_DynMat.mat
├── Hero_Data.bytes
├── Hero_Data.png
├── Hero_Mesh.asset
├── Hero.png
└── Hero_mask.png               # 输入存在时复制
```

| 文件 | 用途 |
| --- | --- |
| `Hero_DynPre.prefab` | 含 MeshFilter、MeshRenderer 和 GpuAnimator 的生成对象 |
| `Hero_DynMat.mat` | 已配置动画 PNG、`_BoneNum`、`_AnimationTexSize`，并开启 Instancing |
| `Hero_Data.png` | GPU 读取的逐帧骨骼数据纹理；不是角色主贴图 |
| `Hero_Data.bytes` | 可按 TextAsset 加载的序列化元数据；由 `FromBytes` 解码 |
| `Hero_Mesh.asset` | 保存网格、覆盖动画的 bounds，以及 UV 通道中的骨骼索引和权重 |
| `Hero.png` / `Hero_mask.png` | 复制的主贴图及可选遮罩 |

重新烘焙不再删除整个角色输出目录。工具会准备好单角色数据后再写入，失败时回滚该角色本次写入，并保留已有资源的 GUID。已有输出 prefab 引用输出目录内独立的 Mesh `.asset` 时，沿用该 Mesh 路径；因此升级后的 Mesh 文件名可能仍为旧名称。其余输出名不变。

已有输出材质会更新工具负责的 Shader、贴图和动画尺寸等字段，并保留其他材质属性。生成 prefab 不会自动加载 `.bytes` 或开始播放。PBR 所需的 LUT 等项目设置由调用方配置；自定义 prefab 行为建议放在包装对象或 Variant 中。

升级到 3.1 不会自动改写已有资源。要获得新的非循环末帧采样和动画 bounds，需要重新烘焙整套角色资源；运行时公开 API 与数据字段保持不变。

## 首次播放

先通过项目自己的加载方式取得生成 prefab 和对应 `.bytes` TextAsset。以下代码假设 `loadedData`、`instance` 和 `clipName` 已由业务代码提供：

```csharp
// 同一角色解码一次，缓存结果，供后续实例共享。
AnimationData sharedData = AnimationData.FromBytes(loadedData.bytes);

GpuAnimator player = instance.GetComponent<GpuAnimator>();
if (player == null)
    player = instance.AddComponent<GpuAnimator>();

Renderer renderer = instance.GetComponent<Renderer>();
player.AutomaticUpdate = true;
if (player.Initialize(renderer, sharedData))
{
    bool started = player.TryPlay(clipName);
    // started == false 时按业务需要处理缺失或无效 Clip。
}
```

若 Renderer 就在组件所在对象上，可以调用 `player.Initialize(sharedData)`。Renderer 必须已经使用同一套烘焙资源的 Mesh 和材质；初始化只绑定引用，不配置材质或修正资源配对。

`TryPlay` 成功后立即显示起始帧，默认从 Clip 第一帧开始，循环方式采用烘焙设置。一次性动作可使用 `loop: false`，群体动画可使用 `randomizeStart: true`。

要检查实际可用的动画名称，可以遍历元数据，而不用猜测文件名：

```csharp
foreach (AnimationClipData clip in sharedData.clips)
{
    if (clip != null)
        Debug.Log(clip.name);
}
```

完整操作示例见 [Basic Playback](../Samples~/BasicPlayback/README.md)，统一调度见 [Manual Crowd](../Samples~/ManualCrowd/README.md)。API 的失败和回调语义见 [API 参考](api.md)。

## 导入示例

在 Package Manager 的本包详情页 **Samples** 中导入所需示例。示例使用 Inspector 引用已有烘焙资源，不附带模型或预烘焙角色，也不会进入 Play 模式后自动选择动画。

Basic Playback 脚本挂到场景中的烘焙 prefab 实例，指定 Renderer、元数据 TextAsset 和项目自己的 Clip 名称；进入 Play 模式后，通过组件右键菜单 **Initialize Sample** 初始化，再执行 **Play Loop** 或 **Play Action**。

Manual Crowd 脚本挂到空 GameObject，指定根节点带 Renderer 的烘焙 prefab、元数据和 Clip 名称；进入 Play 模式后执行组件右键菜单 **Spawn Crowd**。它共享一次解码结果，关闭实例的自动更新并统一 Tick。字段和其余操作见各示例 README。

# GpuAnimator 使用文档

GpuAnimator 将骨骼动画离线写入纹理，在顶点 Shader 中执行蒙皮。`main` 分支提供独立的 `GpuAnimator : MonoBehaviour`：业务代码传入 Renderer 和动画元数据，组件负责选择帧并更新材质属性。

包声明的最低 Unity 版本为 **2020.2**，附带 Shader 面向 **Built-in Render Pipeline**，使用 `#pragma target 3.5`，需要目标图形 API/GPU 支持相应的整数运算和实例化能力。URP/HDRP 项目需要移植 Shader；仅导入本包不会完成管线适配。

## 从哪里开始

| 文档 | 内容 |
| --- | --- |
| [快速开始](getting-started.md) | 安装、Baker 验证与烘焙、生成文件与首次播放 |
| [美术资源规范](art-guidelines.md) | 交付目录、骨骼与权重约束、制作建议和单角色验收 |
| [API 参考](api.md) | 初始化、播放、回调、手动推进和状态语义 |
| [编辑器测试与项目验收](editor-testing.md) | 启用 EditMode 测试、验证 GUID 与失败恢复 |
| [项目接入](integration.md) | 资源缓存、框架适配、批量更新、对象池和 PropertyBlock |
| [问题排查](troubleshooting.md) | 烘焙失败、无动画、播放状态、贴图及渲染限制 |
| [Basic Playback 示例](../Samples~/BasicPlayback/README.md) | 单个对象的播放接入 |
| [Manual Crowd 示例](../Samples~/ManualCrowd/README.md) | 共享数据与外部统一推进 |

## 分支与版本

| 分支 | 播放方式 | 依赖与用途 |
| --- | --- | --- |
| [`main`](https://github.com/Novorizon/GpuAnimator/tree/main) | GameObject / MonoBehaviour，本文档对应的 3.x API | 依赖 Unity，由调用方加载资源；不依赖 ECS 或业务 Framework |
| [`ecs`](https://github.com/Novorizon/GpuAnimator/tree/ecs) | 原有 Entity / Component / System 实现 | 保留 PureMVCFramework 接入方式，继续维护已有 ECS 项目时使用 |

2.x 将旧的静态 Entity API 改为组件实例 API。旧代码中的 `GpuAnimator.Play(entity, ...)` 不能直接套用到 `main`；请按[接入文档](integration.md)迁移，或使用 `ecs` 分支。两个分支是替代版本，不要把它们同时导入同一个 Unity 项目。

3.x 将数据类型改为 `AnimationData` 和 `AnimationClipData`，保留 `GpuAnimator` 组件名。旧元数据仍可通过 `AnimationData.FromBytes` 读取；源码改名步骤见[命名迁移说明](integration.md#从-2x-迁移数据类型名称)。

## 数据与职责

**GpuSkinning > Baker** 提供可配置的源目录、输出目录、Shader 和最大动画纹理尺寸。Validate 完整采样并验证数据，不写入烘焙输出；Bake 按角色保存结果，不再删除整个输出目录。原 No PBR/PBR 菜单保留为使用默认路径的快捷方式。

烘焙工具生成 Mesh、GPU 动画材质、动画 PNG、`.bytes` 元数据和 prefab。PNG 保存逐帧骨骼变换；`.bytes` 保存 Clip 名称、帧区间、帧率等元数据，不包含可直接替代 PNG 的动画纹理。

资源加载器负责加载和保持资源有效，业务逻辑负责决定动画名称。`GpuAnimator` 不持有资源路径，不调用资源管理器，不自动选择动画，也不创建或销毁调用方的材质。

同一套烘焙资源的 `.bytes` 可以解码一次，在多个实例之间共享；每个实例通过自己的 `MaterialPropertyBlock` 保存当前帧。生成的材质已开启 GPU Instancing，但实际批处理结果仍取决于 Unity 的渲染条件，请在目标项目中检查。

## 格式边界

当前格式对每根骨骼保存旋转四元数、位移和一个带符号的统一缩放量，再执行最多四权重的线性混合蒙皮（LBS）。最终蒙皮矩阵需能由旋转与统一缩放表示；独立轴缩放、剪切不属于此格式。函数名中的 `DualQuaternionToMatrix` 不代表执行了双四元数混合蒙皮（DQS）。

播放按烘焙帧率离散采样，没有帧间插值或动画过渡混合。烘焙会生成覆盖动作的保守 Mesh 包围盒，但输入仍限单 SMR、单 submesh，不转换 BlendShape 或驱动 GameObject 根位移。采样时长和其他能力边界见[美术资源规范](art-guidelines.md#采样与当前能力边界)与[问题排查](troubleshooting.md)。

从统一缩放与解码修复前的版本升级时应重新烘焙动画 PNG：旧 PNG 中已经丢失的缩放信息无法在运行时恢复。仅从 2.x 升级到本次数据类型改名不需要重新烘焙已修复的动画纹理；`FromBytes` 兼容旧元数据中的类型名与既有序列化字段。

3.1 保持运行时公开 API 和数据字段不变，但新的非循环末帧采样与动画 bounds 仅在重新烘焙后生效。升级包不会自动修改已有角色资源；要应用这些修复，请成套重烘焙 Mesh、动画 PNG 和 `.bytes`。

源码入口：[播放器](../Runtime/GpuAnimator.cs)、[元数据](../Runtime/AnimationData.cs)、[烘焙工具](../Editor/GpuSkinningTools.cs)。

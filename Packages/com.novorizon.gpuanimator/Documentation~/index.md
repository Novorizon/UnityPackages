# GpuAnimator 使用说明

面向 Unity 2022.3，依赖 Framework。基础包提供 Built-in Shader；聚合安装默认包含 [URP 14 适配](../../com.novorizon.gpuanimator.urp/README.md)，支持 Unlit 与 Lit/实时阴影。HDRP 尚未适配，目标设备仍需支持 Shader Model 3.5 对应能力。

| 文档 | 内容 |
| --- | --- |
| [快速开始](getting-started.md) | 安装、验证、烘焙与首次播放 |
| [资源规范](art-guidelines.md) | 模型、骨骼、权重与交付 |
| [API](api.md) | 注入、Clip、Tick、通知和状态 |
| [项目接入](integration.md) | 加载、共享资源、对象池和 PropertyBlock |
| [编辑器测试](editor-testing.md) | 原生烘焙、GUID、失败恢复与 Player 验收 |
| [排错](troubleshooting.md) | 输入限制与播放/渲染问题 |

GpuAnimator 是每个 Renderer 的播放组件，GpuAnimatorManager 统一调度 AutomaticUpdate 播放器。Manual Crowd 自行 Tick，必须关闭自动推进。

烘焙生成 Mesh、材质、动画 PNG、bytes 和 prefab。PNG 保存骨骼变换；bytes 保存 Clip/帧区间等元数据，不能替代动画纹理。同套 bytes 可解析一次并共享，实例的帧状态通过独立 MaterialPropertyBlock 提交。

资源加载器保持整套资源有效，业务决定播放名称；组件不加载资源、不创建或销毁项目的材质。回收前 Clear，释放实例后再归还依赖资源。

当前格式采用四权重 LBS，骨骼变换包含旋转、位移和带符号统一缩放；不支持独立轴缩放、剪切、BlendShape、根位移或帧间混合。实际 Instancing、法线、包围盒和设备纹理能力需要检查。

入口：[播放器](../Runtime/GpuAnimator.cs)、[调度器](../Runtime/GpuAnimatorManager.cs)、[烘焙器](../Editor/GpuSkinningTools.cs)。

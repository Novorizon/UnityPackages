# Novorizon GPU Animator URP

默认面向 Unity 2022.3.62f1 / URP 14.0.12，随 framework.all 安装。提供 URP Unlit 与相同骨骼变形的 DepthOnly Pass，复用 GpuAnimator 现有 Mesh、RGBA32 动画纹理、bytes 和播放器。

## 工程和烘焙

聚合安装完成后，新工程默认创建 `Assets/Settings/NovorizonURP` 下的 UniversalRenderer 和 Render Pipeline Asset。已有 Graphics/当前 Quality 管线时保持其配置；单独安装本包可使用 `Tools/Novorizon/URP/Configure Default Pipeline`。已有不同类型资产占用默认输出路径会明确报错，不覆盖。

GpuSkinning/Baker 默认 URPUnlit；也可直接使用 GpuSkinning/URP Unlit。Built-in Unlit/PBR 仍是显式选项。已经烘焙的兼容资源可替换材质 Shader 为 `Novorizon/GpuSkinning/URP Unlit`；需要保留动画纹理、骨骼数、纹理尺寸、主贴图和遮罩等属性。不要只使用 Unity 的普通 URP/Unlit 替换，它没有 GPU 骨骼变形。

本版为 Universal Renderer 的不透明 Unlit，支持队伍颜色遮罩、雾和 GPU Instancing 的逐实例帧索引。动画纹理必须保持 linear、无压缩、无 mip。Forward 和 DepthOnly 使用相同的四权重解码；烘焙 bounds 继续覆盖动画范围。

Lit、投射/接收阴影、DepthNormals、透明材质、MotionVectors 和 2D Renderer 不包含在本版。烘焙器继续关闭投射/接收阴影。Blend 属性保留名称兼容，不表示已经实现动画混合。MaterialPropertyBlock 与 SRP Batcher 的实际表现需通过 Frame Debugger 测量，不承诺同时获得所有批处理收益。

## 原生验收

在独立 Unity 验收工程的 manifest 添加：

```json
"testables": ["com.novorizon.gpuanimator", "com.novorizon.gpuanimator.urp"]
```

用 Unity Test Runner 的 EditMode 测试运行本包 Tests，检查 Shader 导入、动画属性、Forward/Depth Pass 和保留已有管线。再用 Basic Playback/Manual Crowd 验证首尾帧、多个实例独立播放、深度纹理与目标设备 Frame Debugger。

当前云环境没有有效 Editor 许可证，尚未执行 Shader 原生导入或画面验收。C# 编辑器代码已与安装的 Unity/URP 官方 DLL 编译核对；这不等于 Shader 或渲染通过。


## URP 版本解析

适配包声明的 URP 依赖下限为 14.0.1，稳定的 14.x 通过版本预检；推荐/已核对 C# API 的版本为 Unity 2022.3.62f1 自带的 14.0.12。其他补丁版本的 Shader 与设备行为仍需工程验收，不把版本号预检当作渲染验证。

项目 manifest 直接指定的 URP 版本优先于适配包的间接依赖。例如工程直接选择 14.0.5、GPU 包声明 14.0.1 时，UPM 保留工程选择，不并排安装两份 URP。只有缓存或旧 lock 记录时，仍按当前依赖图重新解析。all 为新工程推荐 14.0.12；已有工程如需固定其他 14.x，应在自己的 manifest 明确指定。

初始化入口只检查已解析的 URP，不写 manifest，不主动升级/降级包。低于 14.0.1、预发行版本或其他主版本会明确报错，保留项目的选择，由项目决定升级或使用匹配适配包。UPM 不支持在 package.json 里用 npm 的 ^14 或 >=14 等范围代替实际版本。

# 独立包验证

版本 3.2.0-preview.1。本轮保留 Built-in Render Pipeline、Shader Model 3.5 和现有烘焙格式，不扩展 URP/HDRP。4.0 的统一调度器依赖 Framework 0.11；可选 Framework Pool Sample 使用 Framework 0.10.0-preview.1 的真实 GameObjectPool，不另实现对象池或单例。

`dotnet run --project Verification~/GpuChecks.csproj -c Release` 运行 17 项源码检查：采样边界、纹理容量、编码、带符号统一缩放、非法矩阵、包围盒、烘焙元数据解码、共享注入、循环/单次播放、回调重入、非法 delta、清理和真实 Framework 池回收/释放。Engine、Framework、GpuAnimator 分别编译，外部消费者通过公开 API 使用它们。Unity 类型是明确的测试模型，未模拟烘焙采样、PNG、Shader 或 AssetDatabase。

已有 Editor 测试继续覆盖原生采样、重烘焙 GUID、实际资源保存/导入与失败恢复。新增 BuiltinBakeSaveDecodeInjectPlayAndClear fixture 连接合成模型烘焙、保存 prefab、读取 .bytes、资源注入、播放终点、回调、Clear 与重用。

当前环境没有 Unity Editor：17 项源码检查通过不代表原生烘焙、资源导入、Shader 编译/渲染或设备通过。新增 Editor fixture 尚未在真实 Unity 执行。请按 editor-testing.md 启用 testables 和 Unity Test Framework，在 Built-in 工程运行全部 GpuAnimatorTests；Tools~/run-unity-tests.py 可执行原生测试并检查非空的完整 XML 通过结果。最后仍需检查实际渲染、法线、剔除、Instancing 和目标设备资源生命周期。

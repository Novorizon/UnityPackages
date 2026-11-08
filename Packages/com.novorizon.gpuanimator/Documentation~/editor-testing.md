# 编辑器验证与测试

烘焙器会修改生成资源。先在目标 Unity 版本和项目中完成单角色验收，再推广到整批资源。Baker 的 **Validate all roles** 会完整采样并做输出预检，不保存生成资产；它不能代替实际导入、材质和目标设备的渲染检查。

## 启用包测试

1. 在项目中通过 Package Manager 安装与当前 Unity 版本匹配的 **Unity Test Framework**。
2. 在项目的 `Packages/manifest.json` 顶层 `testables` 数组中加入本包，保留原有其他项目配置：

```json
"testables": [
  "com.novorizon.gpuanimator"
]
```

3. 打开 **Window > General > Test Runner**，选择 **EditMode**，运行 `GpuAnimator.Editor.Tests` 和 `Game.Framework.GpuAnimation.URP.Tests`。

测试程序集仅用于 Editor，并以 `UNITY_INCLUDE_TESTS` 和 `TestAssemblies` 标记隔离；正常运行时不需要 NUnit 或测试框架。测试会生成自己的临时模型及唯一的 `Assets/__GpuAnimatorWriterTests_*` 目录，并在结束时清理自己创建的资产，不使用项目中的角色输出目录。

## 测试覆盖

- 帧计数、循环边界、非循环终点与短动画；Builtin/URP Unlit/URP Lit 的烘焙保存、注入和播放流程。
- 定点数编码范围、纹理容量、矩阵分解及保守包围盒。
- 独立 Clip 采样、缺失轨道、源模型保持不变和取消清理。
- 实际四骨骼影响限制、缺失 UV 和非法输入。
- 连续重烘焙的资源 GUID、导入尺寸与格式；Lit 投射/接收阴影标记和缺失切线重建。
- 在保存中途注入异常，检查原文件和 `.meta` 恢复及首次保存失败的清理。

这些测试包含 Unity 原生对象、预览场景和 AssetDatabase 行为，必须在真实 Editor 中运行才算完成这部分验证。独立 C# 编译和数学回归不能证明这些原生行为已经通过。

## 项目验收

按[美术交付清单](art-guidelines.md#单角色验收流程)准备单个角色，验证后执行一次实际 Bake，逐个播放动作并检查末帧、回调时机、法线、镜头边缘剔除和材质。随后重烘焙，确认场景及包装 prefab 的引用仍有效。

本包包含 Built-in Shader 与 URP Unlit/Lit Shader；目标设备需支持相应的 Shader 能力。对目标设备检查纹理尺寸支持，并用 Frame Debugger/Profiler 验证 Instancing 与开销；Humanoid、重定向及 IK 需要项目内单独验收。

保存器只对本次生成的 Mesh 和材质调用 `SaveAssetIfDirty`，避免保存项目中其他未提交的 dirty 资产。生成文件由烘焙器管理，自定义组件和结构应放在单独的包装 prefab 或 Variant 中。

如磁盘或导入错误同时导致自动恢复失败，错误报告会保留备份路径；备份位于项目 `Library/GpuAnimatorBakeBackup/` 下。按其中 `manifest.json` 的路径对应关系恢复本次涉及的文件与 `.meta`，再重新导入和验证；恢复前保留这份备份。

## 自动化执行

在安装 Unity Test Framework 并配置 testables 的工程中运行。资产/播放用例可在 Built-in 或 URP 工程运行。URP Shader 合同测试与基础测试都随包安装：

```sh
Unity -batchmode -projectPath /path/to/GameStarter -runTests -testPlatform EditMode -assemblyNames "GpuAnimator.Editor.Tests;Game.Framework.GpuAnimation.URP.Tests" -testResults /path/to/results/gpu.xml -logFile /path/to/results/gpu.log
```

报告必须包含非零且全部通过的测试；保留 XML 与完整日志。原生测试和图形/设备验收分开记录。云端编译步骤见 [GameStarter 验收说明](https://github.com/Novorizon/GameStarter/blob/main/Documentation/UnityValidation.zh-CN.md)。

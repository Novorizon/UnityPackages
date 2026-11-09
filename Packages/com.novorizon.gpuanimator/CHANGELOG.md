# Changelog

## 4.0.0-preview.19

- 将 GPU 动画 EditMode 测试、夹具、测试 Shader 和验收脚本移到 SDK 仓库的独立验收工具目录。

## 4.0.0-preview.1 — 2026-11-05

- 使用 Framework 单例提供 GpuAnimatorManager，自动模式由一个 Update 集中推进，播放器删除单独 Update。
- 增加播放器和管理器的开始、切换、完成、停止通知，以及集中暂停和速度控制。
- 回调中注册、回收、重播按注册版本处理，本轮新增注册在下一次 Tick 生效；Clear 阻止清理期间重入播放。
- 新增 Framework 依赖，最低 Unity 2022.3；手动 Tick、数据格式和 Built-in Shader 保留。

## 3.2.0-preview.4 — 2026-11-05

- 补齐 Tests 目录的 .meta，修复只读 UPM 包忽略该目录的警告。
- 独立打包脚本纳入 Tests.meta，并检查可导入文件与目录的元数据完整性。

## 3.2.0-preview.3 — 2026-11-05

- 包名和安装目录统一为 `com.novorizon.gpuanimator`；聚合安装器支持替换旧包。
- 同步 Git 地址、文档与 `testables` 配置；源码、程序集名和资源 GUID 保留。

## 3.1.0 — 2026-11-05

- 添加可配置的烘焙窗口、完整校验、进度取消和逐角色结果报告。
- 在隔离预览场景中采样，恢复基准姿态；补齐非循环动画终点并计算动画包围盒。
- 重烘焙保留已有资源 GUID，写入失败时恢复本次涉及的资源。
- 严格校验四骨骼权重、可表达的统一缩放变换、数值范围与动画纹理容量，导入后核对动画纹理尺寸和格式。
- 动画纹理使用整数像素寻址，修复大索引精度问题，Shader 明确要求 Shader Model 3.5。
- 去掉输入 `.mat` 及 SMR 名称包含角色名的旧要求，补充美术交付规范和 Editor 测试。

## 3.0.0 — 2026-11-05

- 公开数据类型 `GpuSkinningAnimData` 改为 `AnimationData`，`GpuSkinningAnimClip` 改为 `AnimationClipData`。
- 构造参数、相关 Editor 标识符、示例及文档统一使用完整的 Animation 名称。
- `AnimationData.FromBytes` 兼容旧 `.bytes` 中的类型名称，序列化字段、动画纹理格式及播放器组件名称保持不变。
- 这是源码 API 改名：升级时更新类型引用；已有修复版动画纹理不因本次改名而需要重新烘焙。

## 2.0.1 — 2026-11-05

- 添加可从 Unity Package Manager 导入的 Basic Playback 和 Manual Crowd 示例。
- 补充中文安装、烘焙、资源注入、API、对象池及故障排查文档。
- 播放器、Shader 和烘焙数据格式保持 2.0.0 的实现。

## 2.0.0

- `main` 使用独立的 GameObject/MonoBehaviour 播放器，资源由调用方传入。
- 原 PureMVCFramework ECS 版本保留在 `ecs` 分支。
- Runtime 与 Editor 分开编译，移除外部程序集引用。
- 保留旧 `.bytes` 元数据格式，并提供显式 `FromBytes` 解码接口。

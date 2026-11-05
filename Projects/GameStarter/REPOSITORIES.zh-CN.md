# 仓库布局

当前采用一个 UnityPackages 仓库维护各独立包。Framework 保留名称，UI、ECS、Ability、HybridCLR 等仍按独立 UPM 包及程序集组织。GameStarter 与 PackageValidation 为同仓库下的 Unity 工程。

详细边界见 [统一仓库架构](../../Documentation/Architecture.zh-CN.md)。原独立仓库保留来源记录，本轮没有删除或归档它们。

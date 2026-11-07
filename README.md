# UnityPackages

Novorizon 的 Unity 2022.3 多包仓库。基础组件库保留 **Framework** 名称，各功能独立为 UPM package，共用代码仓库和发行标签。当前发行：`v0.1.0-preview.34`。

## 包与职责

| 包 | 职责 |
| --- | --- |
| com.novorizon.framework | 单例、线程调度、事件、定时器、池、网络、场景、本地存储 |
| com.novorizon.framework.ui | uGUI 窗口、页面栈、弹窗、面板、Toast、生命周期 |
| com.novorizon.framework.yooasset | YooAsset 加载、独立租约、作用域、精确资源版本与下载 |
| com.novorizon.framework.hybridclr | HybridCLR 配置、原生安装、基线、补丁与加载封装 |
| com.novorizon.ability | 技能运行时与 Odin 编辑器，同一个包 |
| com.novorizon.ecs | 实体、组件、系统、ECS 专用池与生命周期 |
| com.novorizon.rtree | 六种空间树，各自一个 Runtime 目录 |
| com.novorizon.gpuanimator | GPU 动画烘焙、注入、统一调度与回收 |
| com.novorizon.gpuanimator.urp | 默认 URP 14 Unlit/Lit、阴影/深度/法线 Pass 与工程配置 |
| com.novorizon.localization | 本地语言表、CSV/TSV 导入、Inspector Key、TMP 字体与语言切换 |
| com.novorizon.story | 独立剧情会话、TMP 对话/选项/立绘、可选视频与业务命令 |
| com.novorizon.diagnostics | 只读计数、资源租约归属、快照对比与导出 |
| com.novorizon.input | Input System、Map、设备、屏蔽与重绑定 |
| com.novorizon.audio | 音源池、分组、渐变、音乐交叉渐变与 Mixer 路由 |
| com.novorizon.data | Luban 生成、二进制表加载与查询 |
| com.novorizon.framework.all | 全部包的聚合安装入口 |

官方 HybridCLR、YooAsset、Luban 通过依赖声明和官方 Git 来源解析，不保存在本包仓库。Framework 不反向依赖这些功能包；ECS 与 Ability 独立。详见[架构](Documentation/Architecture.zh-CN.md)。

## 安装

在自己的 Unity 工程中通过 Package Manager 安装聚合包：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework.all#v0.1.0-preview.34
```

单独安装时将路径替换为上表对应的包名。UPM 不支持 Git 传递依赖，单包安装需在工程 manifest 中同时声明其依赖来源；聚合安装器会处理完整集合。所有自有包使用同一发行标签。固定标签不会自动升级，已发布标签不移动。

需要可运行工程时使用独立的 [GameStarter](https://github.com/Novorizon/GameStarter)。本仓库不含 Unity 工程，不要求消费者使用本地 `file:` 路径。

## 文档与示例

- [Demo 索引](Documentation/Samples.zh-CN.md)：每个示例的入口、前置资源和验收目标。
- [游戏基础服务](Documentation/GameServices.zh-CN.md)：场景、音频、输入、Luban 和存储。
- [生命周期与资源所有权](Documentation/LifecycleAndOwnership.zh-CN.md)。
- [静态字段恢复](Packages/com.novorizon.framework/Documentation~/StaticReset.md)。
- [Unity 云端编译与验收](https://github.com/Novorizon/GameStarter/blob/main/Documentation/UnityValidation.zh-CN.md)：从 GitHub 获取工程、编译全部 Demo、Play、IL2CPP 和补丁验收。

## 维护

需要 Python 3.12 和 .NET SDK 8：

```sh
python3 tools/workspace.py check
python3 tools/check-docs.py
python3 tools/workspace.py verify --dotnet /path/to/dotnet
```

修改包内容或版本后执行 `python3 tools/workspace.py sync`。分发与版本规则见[发布说明](Documentation/Publication.md)，模型回归和原生验收的边界见[验证范围](Documentation/Validation.md)。

[ORIGINS.json](ORIGINS.json) 和导入基线记录来源；各包保留原许可证，RTree 使用 LGPL-2.1-or-later。


## v0.1.0-preview.34

- 多语言提供本地表资产、CSV/TSV 导入、统一 Settings/Bootstrap，以及 TMP Inspector 的 Key 搜索、预览和字形检查；静态界面无需逐个编写 DataManager 接线。
- 新增独立 [Story](Packages/com.novorizon.story/README.md) 包及 Basic Story 示例：TMP 对话、选项、立绘/背景、语音、视频、取消和检查点；业务只处理触发、命令和持久化。
- 诊断增加资源租约的 owner/scope/scene、可选开发栈、基线差异和 JSON 导出；原有 AcquireAsync 调用保持兼容。
- Linux 原生验收 61 项 EditMode、17 项 PlayMode 全通过，包含完整剧情示例和真实视频像素回读；iPhone/Metal 和 IL2CPP 仍待目标环境验收。

## v0.1.0-preview.33

- 运行时 UI Demo 与多语言绑定统一使用 TextMeshPro；UI/all 默认安装 TMP 3.0.6。字体覆盖使用 TMP_FontAsset，旧 Text/Font Prefab 需要显式迁移。
- 原生验收包含官方 TMP 字体资源准备、真实多语言 TMP 绑定和 UI Demo/虚拟行字体网格检查；原有 GPU 回读和安全区用例继续运行。
- 完成 [Cube 剧情审查与模块规划](Documentation/NarrativeArchitecture.zh-CN.md)：建议独立可选剧情包，UI 负责表现，业务负责触发/任务/存档；本版未新增剧情运行时。

## v0.1.0-preview.32

- 修复多语言可选程序集在 Unity 2022 的版本条件，使 uGUI 绑定和 Luban 菜单实际启用。
- 修复 GPU 重烘焙 Mesh/Material 的定向保存，保留资产 GUID、失败恢复与无关脏资源。
- Linux Unity 2022.3.62f1 原生验收通过：58 个 EditMode、4 个 PlayMode 用例，含真实 GPU 位置/法线像素回读。复现方法与范围见 [原生验收](tools/NativeAcceptance/README.md)。

## v0.1.0-preview.31

- UI 新增动态高度虚拟列表：稳定 ID、估计/实测高度、插删锚点、异步测量版本校验、宽度变化失效和有界单元格复用，详见 [业务功能用法](Packages/com.novorizon.framework.ui/Documentation~/Features.md)。
- 新增 URP Lit 烘焙选项，支持 PBR 参数、法线贴图、实时阴影及共用骨骼变形的深度/法线 Pass；默认烘焙仍为 URP Unlit。
- 托管回归、官方 Unity/URP C# API 编译与独立 DXC HLSL 变体编译分别验证；原生 Shader 导入、排版/拖拽和画面验收仍需要有效 Editor 许可证。

## v0.1.0-preview.30

- 默认 Unity 2022.3.62f1 / URP 14.0.12，all 安装 URP 适配并为没有现有管线的工程创建默认资产；GPU 烘焙默认 URP Unlit。
- 新增 [Localization](Packages/com.novorizon.localization/README.md)：Luban Excel 模板、语言回退、占位符校验、可取消切换和可选 uGUI 绑定。
- 新增 [Diagnostics](Packages/com.novorizon.diagnostics/README.md)：只读 Editor 计数、过滤和快照导出，不创建单例。
- UI 增加固定列虚拟网格；动态图文列表和更详细的诊断归属在后续阶段实现。
- [URP 适配](Packages/com.novorizon.gpuanimator.urp/README.md)首版为 Unlit + DepthOnly；Lit 与阴影不在本版。原生 Shader/渲染验收尚受当前 Editor 许可证缺失阻塞。

# 验证范围

导入使用前一轮校验后的 UPM 归档，共 11 个库包，加独立聚合安装包。这是首次导入时的历史基线。`ImportBaseline.json` 记录 2642 个导入文件的 SHA-256；检查要求保留全部可执行源码、程序集、资源和 meta，允许自有包 Markdown 调整及 PackageMoves.json 中明确记录的目录移动、包元数据变更和退役外壳元数据。官方 HybridCLR、YooAsset 的初次导入记录仅作为历史来源；当前源码已外部化，检查器要求这些包目录不再存在。

Framework、UI、ECS 和 HybridCLR 的单例启动回调已从 OnSingletonAccessed 统一改名为 OnStart。PackageMoves.json 逐文件记录这四处符号改名；检查器反向替换后必须恢复导入基线的完整 SHA-256，确保没有混入其他运行时修改。

工作区检查覆盖：12 个自有包及 3 个外部依赖引用的名称、版本、自有依赖闭包与环路、Framework 依赖方向、ECS/Ability 独立性、第三方上游来源和无 Payload 约束、同仓库版本标签、可导入文件及目录的 .meta 完整性、主动导入的程序集名称和 GUID 唯一性，以及包仓库不含 Unity 工程的结构约束。

迁入的可执行验证包括：

- 聚合安装器：20 组模型场景，包含同仓库相邻目录安装以及旧 Editor/ECS/GpuAnimator 包的原子替换与失败保留；使用 Unity/UPM API 模型验证请求恢复、失败、重试、批量依赖与源码安装路径。
- RTree：保留原正确性与空间查询回归程序，直接编译统一仓库的 canonical Runtime 源码；覆盖树变更、查询及空间索引一致性。

使用 .NET SDK 8 运行 `python3 tools/workspace.py verify`。这些检查不执行真实 Unity API、UPM 服务或渲染。历史仓库中其他验证结果属于原提交的记录，本次不据此声称重新运行了全部旧仓库测试。

## 尚未执行的 Unity 验收

当前环境没有 Unity Editor、IL2CPP 平台模块、GPU 或设备。

1. 使用 tools/workspace.py unity-project 在仓库外生成验收工程，在 Unity 2022.3.62f1 检查 Git 依赖、asmdef、Native 插件及资源导入。
2. GameStarter 的启动与生命周期验证在独立工程中运行，包仓库 CI 不再依赖该工程。
3. 从本地 package.json 和分发 tarball 安装聚合包，检查所有包版本、第三方来源解析和跨重编译恢复；确认工程仅通过 Git/UPM 解析包，不依赖本仓库的本地相对目录。
4. 验证官方 HybridCLR Embedded 来源、Data~、原 importer meta、默认配置和热更入口；执行 IL2CPP Player 构建与补丁加载。
5. 按各包原生文档验收 UI 输入和资源、Ability Odin 编辑器、ECS 生命周期、空间查询、GPU 烘焙/播放/回收与 YooAsset Bundle 加载。
6. 在 Unity 中单独验证同仓库标签下的 Git 子目录安装；GitHub 提交、标签及源码一致性由发布流程单独核对。

新增 Ability 校验使用同一个包中的实际运行时和编辑器源码：1083 条公开 API 快照、91 项编辑器 Codec/Workspace/Sandbox 回归及缺少 Odin 时的 Editor 编译。数据样例仅用于 tools 下的测试夹具，不进入运行时包。

本次通用服务验证编译实际 Framework Core/Network、UIManager 和 GPU 播放器/调度器源码。24 组单例/网络/动画调度场景覆盖重置、退出保护、手动模式、重入通知和回收；174 项 UI 回归使用 Unity API 模型。历史基线保持不变，本次运行时代码修改逐文件记录 sourceChanges 的原因和 SHA-256。

新增 GameModulesVerification 直接编译实际 Framework Core/Unity、音频、配置与 YooAsset 适配源码，关闭 .NET 传递项目引用；覆盖数据清理、真实临时目录存储、配置重载、场景排队、音源复用与资源退出回归。模型未执行原生音频、场景加载和 JsonUtility；平台文件系统验收范围见 GameServices.zh-CN.md。

本轮新增真实 loopback TCP 的传输/会话验证，覆盖并发连接等待取消、状态事件、超时、断线与重连、取消回调异常和释放失败；不依赖服务器业务协议。UI 验证新增重复打开、跨层单例、迟到加载、原生销毁等待及旧工厂关闭场景。资源验证覆盖独立租约、共享对象、逐请求取消、场景/窗口作用域和退出等待。

本轮输入/Luban/音频校验使用 Unity Input System 1.11.2 的 API 边界模型、官方 Luban 4.12.0 CLI 与 1.2.0 ByteBuf 源码。验证真实 Excel → 生成 C# → 二进制 → Tables，以及失败保留旧输出、meta 保留、并发/取消不发布过期快照。输入覆盖 Map/设备/控制方案、嵌套屏蔽、订阅与重绑定清理；音频覆盖交叉渐变、路由、暂停、后台数据等待及预热后托管分配。原生输入事件、音频输出、Unity 导入和 IL2CPP 尚未执行。第三方测试依赖只下载到临时缓存，不进入包仓库。

本轮验证配对 OnStart/OnExit、重复访问只启动一次、Release 不创建实例、不解除退出保护、原生延迟销毁和重复退出只清理一次，以及 ECS 的实际 SingletonBehaviour 通过 OnExit 清理。资源验证覆盖 Release 后内部等待关闭再初始化。GameStarter 验证入口直接初始化与 OnExit 清理，不再要求业务层所有权登记和失败回滚。

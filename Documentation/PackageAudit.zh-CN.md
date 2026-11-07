# Package 正确性与性能审查

2026-11-08（北京时间）。基线 `v0.1.0-preview.34`，本轮发行 `v0.1.0-preview.35`。对全部 **16 个自有包**分组审读，执行全量托管回归，并在真实 Unity 2022.3.62f1 中以全部 16 个本地包覆盖、导入全部 9 个 Samples。数值、源码摘要与验证边界见 [PackageAudit.json](PackageAudit.json)。

这不是“所有 API 都无缺陷”或“整个框架零 GC”的证明。下表列出实际覆盖、已确认并修复的问题；性能结论限定到具体输入和操作。

## 正确性结果

| 包 | 覆盖与结果 |
| --- | --- |
| Framework | 审查单例、线程派发、事件、计时器、池、场景、存储和网络。修复派发器抛异常后连接任务永久等待，以及断线清理期间复用旧成功 readiness 的窗口；补充差分计时器及线程/订阅回归。 |
| UI | 审查工厂、缓存、guard、三类虚拟列表与安全区。修复 Grid 的 Unbind 回调重入 Refresh/Clear 后，旧流程继续释放新绑定；模型多组合与原生回归通过。 |
| YooAsset adapter | 对初始化选项做快照，拒绝不同精确版本/下载模式误共享成功；取消/关停等待实际下载结束，下载器先入原生调度再注册取消；旧 Play 代际等待者不能复活旧配置。真实上游空下载器验证完成/取消时序。 |
| HybridCLR adapter | 保留完整尺寸/哈希校验，去掉同一字节的重复哈希，纯 CPU 校验移至 worker；校验结束、Prepare 发布与 BeginBoot 前复查取消。损坏内容与取消边界回归通过。 |
| All | list/add/embed 三条 UPM 路径均执行期限；超时保留活跃请求句柄，Retry 等待原操作结束，不能并发启动第二个请求。 |
| Ability | 修复 float 平方溢出/下溢造成的射程与光环误判，以及长距离扫掠消减误差和追踪到达问题；使用 double 中间计算。补充嵌套快照、归还与容量预算回归。 |
| ECS | 未确认需要改动的运行时缺陷；增加真实 World 结构变更屏障、Owned/Borrowed 对象释放隔离、1024 实体更新回归。版本仅随 Framework 依赖更新。 |
| RTree | 保留六种树实现。执行已有随机/差分查询、变更及节点不变量检查；另外测量预分配上下文/输出的查询分配。未确认需改动的算法缺陷。 |
| GPU Animator | 批量注销不再逐次扫描全表，保留通知代际/顺序并限制暂停模式的缓存增长；元数据只接受烘焙契约的当前/旧类型名，拒绝陌生类型回调；Built-in PBR 法线改为加权矩阵逆转置。 |
| GPU Animator URP | 补齐播放器写入的实例属性声明；执行 64 个 DXC 入口/变体编译与真实 Shader 导入、法线/GPU 像素回读。实例属性修复没有被当成已测 Draw Call 收益。 |
| Localization | 保留格式/占位符、CSV/TSV、语言事务、快照与 TMP 测试；格式校验改为 64 位参数掩码和共享无状态探针。 |
| Diagnostics | 修复反射可以访问 public 属性的 private getter 的边界，复用租约类型访问器；真实 Editor 验证只读访问和 JSON。大量记录的显示按页限制，导出仍为全量数据。 |
| Input | 在 Disable 触发业务 canceled 回调前预留重绑定所有权，避免重叠 rebind 或替换 overrides；真实 Input System 虚拟键盘回归通过，测试临时焦点设置完整恢复。 |
| Audio | 渐变曲线验证保留合法值域且移除枚举装箱；检查语音容量、暂停、音乐、池与原生渐变配置。 |
| Data | 审查快照、后发请求、事务发布与缓存；官方 Luban CLI/生成代码/二进制加载回归通过，未确认新阻塞问题。版本仅随依赖更新。 |
| Story | 复核图、会话、Busy、取消、步骤代际、语音及视频生命周期；原有真实 TMP、多语言、立绘/选项、VP8 像素、超时与迟到资源回归继续通过。版本仅随依赖更新。 |

## 可复现的性能变化

下面的前后比较使用实际 C# 源码，在 .NET 8 上以有限 Unity/API 模型测量；它们不能替代真机预算。

| 操作/输入 | 修改前 | 修改后 |
| --- | ---: | ---: |
| 结构体事件主线程发布 | 24 B/次 | 0 B/次 |
| Audio FadeTo 曲线验证/配置 | 24 B/次 | 0 B/次 |
| 10000 行 × 2 语言表构建，含参数 0/63 | 49,902,184 B | 5,102,184 B（约减少 89.8%） |
| Ability 稳定 Update，4097 技能 | 393,464 B/次 | 0 B/次 |
| Ability 稳定 Update，10000 技能 | 786,704 B/次 | 0 B/次 |
| 10000 条资源诊断捕获 | 2,115,024 B | 1,932,752 B |
| GPU 批量 Stop，8000 播放器、5 次样本的中位时间 | 925.049 ms | 3.045 ms |

GPU Stop 的主要结论是移除逐次全表扫描；共享 VM 的绝对耗时和倍率不应作为设备性能承诺。Ability 快照池仍最多保留每线程/类型 8 个列表、32768 个元素，按 Capacity 计费，没有提高原有总元素预算。超预算、冷增长、业务回调和事件仍可能分配。

8 MiB 热更代码的校验从两次 caller 线程哈希改为一次 worker 哈希。CPU 总工作量减少，调用线程更快让出；worker 运行时间、Unity 资源读取和程序集加载仍需等待，不等于整个准备阶段不占成本。

### 真实 Unity Mono 热路径

实际 Editor 中，下列指定操作预热后均测到 **0 B 托管分配**：无载荷/结构体事件派发、10000 Key 语言查询、10000 个未来计时器的空闲 Update、10000 条固定列表/网格/动态列表 ScrollTo、音频渐变配置、GPU 调度，以及 ECS 1024 实体和 Ability 4097 技能的稳定更新。

计时与工作量记录在 JSON 中。31 批次的 p50/p95 是**批次平均每操作时间的分布**，不是单次调用的尾延迟。列表使用空 RectTransform 单元格，没有把 TMP 文字绑定、排版、Canvas 重建或完整渲染帧算进零分配结论。GPU 调度测试也不代表 GPU 绘制性能。

Story 的立即完成 1000 节点测试约分配 617 KB，属于跨节点会话、取消与检查点开销，不应描述为零分配。诊断捕获/差异本来就产生快照对象，也不在运行时零 GC 承诺内。

## 验证与复现

- 全量 `python3 tools/workspace.py verify --dotnet /path/to/dotnet`，包含新增的 8 项 Foundation、21 项 UI/Audio/Input、27 项算法、9 项资源、8 项发布、3 项安装器、7 项 GPU 审查，以及 62 个内容校验断言。
- 真实 Editor **65/65 EditMode、33/33 PlayMode**，无跳过；[原生验收记录](NativeAcceptance.json)保留源码及 XML/日志摘要。
- URP 的 **64 个 DXC HLSL 入口/变体**语法编译。DXC/SM6 检查不能代替 Unity 的全部平台编译与剥离。
- 额外性能测量入口见 [Foundation](../tools/FoundationAuditVerification/README.md)、[算法](../tools/AlgorithmsAuditVerification/README.md)、[集成](../tools/IntegrationAuditVerification/README.md)、[内容](../tools/ContentAuditVerification/README.md)与[UI/Audio/Input](../tools/UiAudioInputAuditVerification/README.md)。

原生复现方法见 [NativeAcceptance](../tools/NativeAcceptance/README.md)。本轮运行环境为 Linux x86_64、Unity 2022.3.62f1、URP 14.0.12、TMP 3.0.6、Input System 1.11.2、YooAsset 2.3.18，图形后端为 Mesa llvmpipe OpenGL 4.5；CPU 是共享 VM 的 Xeon Platinum 8573C。

仍需目标环境验收：iPhone/iPad/Android 的帧时间与峰值内存、Metal/IL2CPP/AOT/裁剪、真实 Odin UI、完整 Forward 光照/阴影、实际实例批次/Draw Call 收益、Bundle 吞吐/内存引用计数、物理音频与设备音画同步。原生空下载器测试验证时序，不代表真实 Bundle 下载/恢复场景已全面验证。

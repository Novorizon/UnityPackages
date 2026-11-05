# 正确性复核

本轮验证对象是**移除 MVC/PureMVC 依赖后的实际 ECS 源码**，包括 ECS 自有池、生命周期接口、Framework 单例的 ECS 适配与 GameObject 资源租约。测试将 Unity 外围替换为托管模型，不为 MVC/PureMVC 提供同名桩。当前实现依赖 Framework、UnityEngine/Mathematics，不是无 Unity 的纯 C# Core。

验证环境：Linux x64、.NET 8、Release。当前结果见各项目输出与 [verification-current.json](../Tests~/verification-current.json)；该记录的源码散列用于标识本次实际测试输入。优化前 `sourceBaseCommit` 或历史基准结果不能标识本次源码，也不能证明新实现性能。

当前统一脚本实际通过 **171/171**：核心 92、外部消费 15、QuickStart 10、真实单例 14、自有池 40。另有 3000 次随机存储变更、100000 次预热重复注入 0 B 和两种 Hybrid 检查；这些专项不并入上述计数。

## 验证项目与证据

| 验证 | 检查内容 | 源码与结果 |
| --- | --- | --- |
| 核心集成回归 | 成员匹配、帧事务、池复用、异常清理、初始化重入、资源请求与租约 | [CoreHarness](../Tests~/CoreHarness/)、[JSON](../Tests~/correctness-results.after.json)、[运行输出](../Tests~/correctness-run.after.log) |
| 外部用户程序集 | ECS/消费代码分 DLL；显式动态类型注册、跨程序集同完整类型名、生命周期和手册例子 | [ExternalConsumer](../Tests~/ExternalConsumer/)、[结果](../Tests~/ExternalConsumer/results.json) |
| 系统存储专项 | 固定种子 3000 次随机变更，1～5 组件系统、Hybrid、预热重复双组件注入分配 | [SystemValidation](../Tests~/SystemValidation/)、[输出](../Tests~/SystemValidation/results.txt) |
| ECS 自有池 | 40 项通过；无程序集扫描、类型身份、双/跨池回收、工厂/生命周期重入、弱引用和销毁会话隔离 | [PoolValidation](../Tests~/PoolValidation/)、[结果](../Tests~/PoolValidation/results.json) |
| ECS 单例生命周期 | 直接编译实际 ECS.SingletonBehaviour，托管模型覆盖发现/创建、幂等初始化、重入与退出 | [SingletonLifecycle](../Tests~/SingletonLifecycle/)、[结果](../Tests~/SingletonLifecycle/results.json) |
| 实际 QuickStart 示例 | 直接编译示例源；公共 API、ECS AssemblyRef 无 MVC/PureMVC、移动、多实例、暂停/继续、重建和清理 | [DemoSmoke](../Tests~/DemoSmoke/)、[结果](../Tests~/DemoSmoke/results.json) |

旧 MVC 的 ApplicationEntryStartup 结果仅是历史兼容检查，不再作为基础 ECS 的启动条件或当前默认回归项目。真实 Unity 安装和 Player 验证不包含在上表中。

## 关键不变量

- **身份与复用**：对象可复用，GUID 不复用；旧代际排队的 Remove/加载回调不能影响新身份。长期引用必须保存 GUID 后重新查询。
- **匹配与可见性**：1～5 组件系统的实体、索引与组件引用对齐；swap-remove 后仍正确。多个 World 共享全局实体表，但各自系统缓存独立。
- **结构提交**：World 帧中 Remove/Destroy/系统增删延迟到边界；Add 可立即 Get，匹配在边界刷新。通知与回收重入的后续操作会继续排空。
- **初始化失败与重入**：组件初始化钩子若销毁/复用实体、插入同类型组件，外层 Add/Archetype 复验 GUID 与重复类型，拒绝错误提交并清理自身候选。Archetype 失败清理已成功创建的候选。
- **系统初始化**：World 已赋值后初始化一次；失败不留下部分注册。回收时剔除缓存与 World 引用，后续注册仍可用。
- **批处理**：作用域支持嵌套和幂等 Dispose。用户异常或通知异常后范围关闭，完成必要剔除与清理，后续帧仍可运行；它不回滚任意业务副作用。
- **自有池**：泛型直接构造并首次自动登记；Type/string/Archetype 仅能创建已登记类型。CLR Type 分桶，歧义名称拒绝；不能重复回收同一对象或接收错误类型工厂结果。
- **资源所有权**：借用对象不销毁；拥有租约按原来源释放。实体销毁、绑定替换、乱序请求及晚到结果不能重复释放或错误绑定；延迟只影响租约释放时间。

核心随机参考模型使用固定种子，对比实体 GUID 集合、单/双组件成员、缓存引用与注册表，覆盖多个 World 和池复用。系统存储专项另执行 3000 次变更；二者是不同验证口径，不合并成同一计数。

## 使用契约与尚未验证的边界

- ECS API 在主线程使用。World 是执行组，本轮没有独立 World/Runtime 实体隔离。
- 直接调用单个系统 Update 不具备 World 帧事务保证。Batch 必须在更新 World 前结束，不跨帧或 await 保持打开。
- 组件业务字段通过 ECS.IInitializeable/IRecycleable 或每次完整赋值重置。系统覆盖 OnRecycle 时调用基类；初始化回滚仅覆盖本次候选，不回滚用户回调的独立副作用。
- swap-remove 不保证顺序或 index 稳定。ComponentType ID 仅供进程内匹配，不持久化。
- Unity GameObject/Component、自动帧时序与同步上下文为受控托管模型；不等于原生延迟 Destroy、fake-null、场景卸载、域重载或真实退出顺序。
- Provider 模型允许控制完成顺序并观察租约释放，不代表 Addressables 已集成验证；业务 provider 仍需主线程、至多一次完成和正确来源释放。
- 未执行 Unity Editor/Player、Mono/IL2CPP、内置 Transform、Burst、Jobs 或实际 JobHandle 集成测试。默认包解析与目标平台构建仍需真实 Unity 工程验证。
- 历史性能样本只描述此前记录的源码和 .NET 环境。本轮没有重新采集完整同口径基准；专项零分配检查仅反映其明确的预热重复注入场景。

## 复现

在仓库根目录安装 .NET 8 SDK 后执行：

```sh
./Tests~/verify.sh
```

各项目的 NuGet.Config 不依赖第三方包源。容器 SDK 与可写 DOTNET_CLI_HOME 配置见 [CoreHarness](../Tests~/CoreHarness/README.md)。任一失败返回非零退出码，日志保存在 `Tests~/verification-logs`。消费工程还核对手册首个完整 C# 代码块，避免文档与编译示例偏离。

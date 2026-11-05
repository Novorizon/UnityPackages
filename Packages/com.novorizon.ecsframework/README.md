# ECS-Framework

Unity 2022.3 托管组件框架。ECS 单向依赖 Framework 与 Mathematics，复用 Framework 的 Unity 单例基础能力，自行管理类型工厂、组件/系统池和生命周期时序，不依赖 MVC/PureMVC。数据组件使用 `class : IComponent`，系统通过 World 驱动。

- [Framework 依赖与生命周期迁移](Documentation~/FRAMEWORK-INTEGRATION.zh-CN.md)
- [5 分钟上手：安装与运行 Demo](Documentation~/QUICKSTART.zh-CN.md)
- [使用手册与完整可复制示例](Documentation~/USER-GUIDE.zh-CN.md)
- [正确性验证与测试边界](Documentation~/CORRECTNESS.zh-CN.md)
- [API 易用性评估](Documentation~/USABILITY.zh-CN.md)
- [优化、使用约定与兼容迁移](Documentation~/OPTIMIZATION.zh-CN.md)
- [连续组件存储规划（尚未实现）](Documentation~/STORAGE-ROADMAP.zh-CN.md)
- [ECS 自有基础设施与去 MVC 迁移](Documentation~/MVC-DECOUPLING.zh-CN.md)
- [.NET 核心回归与基准](Tests~/CoreHarness/README.md)

公开启动入口只有无参 `Ecs.Initialize()`。基础流程为：`Ecs.Initialize()` → `world.RegisterSystem<你的系统>()` → 创建实体 → 添加组件。泛型通过 `new T()` 创建并自动登记对应工厂，不扫描程序集。默认 World 由 Unity 自动更新，不要再手动重复驱动。

通过 Type、字符串或 `EntityArchetype` 类型数组动态创建时，需先 `ReferencePool.Instance.Register<你的类型>()` 或显式登记工厂。旧泛型 marker 和程序集参数初始化入口已移除，旧动态代码不能再依赖启动时扫描登记，需迁移调用和类型注册。UPM 安装后在 Samples 中导入 **QuickStart**，挂载 `QuickStartDemo` 即可观察移动、暂停/恢复和重启；无需配置 Prefab 或资源加载 provider。

旧 MVC/PureMVC 库保留在 [可选 LegacyMVC 包](Integrations~/LegacyMVC/README.md)，不参与默认编译。`2.0.0-preview.1` 的生命周期接口、动态类型登记和原生对象回收规则有迁移变化，见使用手册。资源加载使用显式 provider/lease；默认绑定自建 GameObject 使用 Unity Destroy，也可选择 Borrowed。

`Tests~`、`Documentation~` 和未导入的 `Samples~` 使用 Unity 忽略的目录名；.NET 8 测试代码不会参与包编译。正确性测试含托管边界模型，性能结果反映独立托管核心，不能代替目标 Unity Player 实测。

本轮独立包的重新验证与制作方式见 [发行说明](Documentation~/PACKAGE-RELEASE.zh-CN.md)。

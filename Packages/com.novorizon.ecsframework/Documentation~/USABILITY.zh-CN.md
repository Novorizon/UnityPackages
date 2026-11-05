# 使用简洁性评估

基础使用已经收敛为四步：**初始化、注册系统、创建并设置组件、按归属清理**。业务引用 `ECS` 及用到的 Framework 程序集，无需加载程序集、创建 MVC 对象或配置资源框架。ECS 保留专用池与生命周期适配，依赖 Framework、UnityEngine 与 Mathematics；真实 Unity Editor/Player 仍需在目标项目验证。

先读[5 分钟上手](QUICKSTART.zh-CN.md)，完整代码见[使用手册](USER-GUIDE.zh-CN.md)，可视化示例见 [QuickStart](../Samples~/QuickStart/README.md)。

## 基础使用保持四步

| 步骤 | 用户调用 | 说明 |
| --- | --- | --- |
| 1. 初始化 | `Ecs.Initialize()` | 唯一公开启动入口；返回稳定默认 World，重复调用安全，无程序集扫描。 |
| 2. 注册系统 | `world.RegisterSystem<MySystem>()` | 直接返回 MySystem；每次创建一个注册实例。 |
| 3. 创建与设置 | `EntityManager.Create()`、`AddComponentData<T>()` | class 组件保存数据，系统自动匹配；泛型创建首次自动登记工厂。 |
| 4. 局部清理 | `DestroyEntity(guid)`、`world.RemoveSystem(system)` | 保存 GUID 和返回的系统，只清理本入口拥有的对象。 |

默认 World 由 WorldManager 的 Unity 更新驱动。普通入口在 Start 中初始化，在 OnDestroy 中清理，不需要额外调用 World.OnUpdate。QuickStart 把多实例、暂停/继续、重启与退出清理封装在会话中；读者可先看最小数值示例，再理解这些归属细节。

## 已经消除的启动障碍

| 旧问题 | 当前实现 | 对使用者的影响 |
| --- | --- | --- |
| 池、单例和生命周期依赖 MVC，World/System 公开基类依赖 PureMVC | ECS 自带对应基础设施；移除 Notifier 继承 | 业务 asmdef 引用 ECS 与需要的 Framework 程序集，不安装旧 MVC UI/资源模块。 |
| 启动必须扫描 ECS、MVC、业务程序集才能创建类型 | 泛型创建缓存 `() => new T()`；动态创建显式 Register 工厂 | 普通常见路径不需 marker、LoadTypes 或 Assembly-CSharp。 |
| 同 FullName 跨程序集可能串池 | 按 CLR Type 分池，歧义名称拒绝解析 | 泛型或 Type 入口保持准确类型身份。 |
| World.Self 可能指向最后初始化的其它 World | Ecs.Initialize 返回 DefaultWorld | 不依赖隐含初始化顺序。 |
| 泛型注册返回接口，需要转换 | RegisterSystem<T> 返回 T | 注册后直接配置字段。 |
| BeginBatch/EndBatch 容易漏配对 | `using (WorldManager.Instance.Batch())` | 异常退出也会结束范围。 |

组件或系统分散在多个业务 DLL 不增加初始化步骤。只有从配置得到 `Type`、字符串或 Archetype 类型数组时，业务才需显式 `ReferencePool.Instance.Register<T>()` 或提供工厂。这是动态创建的明确约束，避免扫描未知程序集。旧 marker 和程序集参数 Initialize 重载已移除；旧的动态调用若依靠启动扫描登记，必须补齐显式注册，不能直接视为零改动升级。

## 批量初始化

```csharp
using (WorldManager.Instance.Batch())
{
    // 创建实体、添加并设置若干组件。
    // 离开最外层范围后统一重新匹配。
}
```

范围支持嵌套，Dispose 只结束自己的批处理。显式 BeginBatch/EndBatch 仍需 try/finally 配对；批处理未结束时不能更新 World。范围对象有一次托管分配，适合初始化批量操作；不能把重复匹配零分配理解为所有便利 API 零分配。

## 使用者仍需理解的四条规则

1. **组件会复用。** 使用 class : IComponent；以 ECS.IInitializeable.OnInitialized、ECS.IRecycleable.OnRecycle 或每次完整赋值重置字段。不能依赖构造函数每次运行。系统覆盖 OnRecycle 时调用 base.OnRecycle。
2. **帧内结构变更有边界。** 修改字段立即可见；更新期间 Remove、Destroy、系统增删在帧末提交。Add 可立即读取，但匹配在边界刷新。同回调先 Remove<T> 再 Add<T> 会因尚未删除而报重复。
3. **身份与顺序分开。** 长期保存 GUID，再 TryGetEntity 查找。Entity 引用会复用；系统 index 和顺序不稳定。
4. **默认世界共享且自动更新。** 入口只清理自己的实体和系统，不调用全局 DestroyAll、不销毁共享管理器、不额外 tick 默认 World。

这些是池与更新模型的实际语义。异步资源、HybridSystem 和自定义过滤器放在手册进阶部分；基础 Cube 示例不需要 provider。

## 当前边界

基础 ECS 仍包含 Unity 单例、GameObject 与 HybridSystem，并使用 Mathematics；没有实现无 Unity 的纯 C# Core，也没有独立 EcsRuntime。World 仍是共享全局实体表的执行组。当前按 Unity 框架维护；共享基础能力边界见 [Framework 接入](FRAMEWORK-INTEGRATION.zh-CN.md)。

已有场景对象可按拥有或借用方式绑定；资源加载必须显式配置 `EntityManager.Instance.GameObjectProvider`，租约记录实际释放方法。基础启动不会隐式创建 Addressables 或 MVC 资源服务。

## 验证与可复制性

独立调用方程序集直接编译公共 API，手册首个完整示例也参与编译和模型运行。DemoSmoke 编译同一套 QuickStart 业务与 Unity 入口；ECS 使用实际自有池和单例源码，Unity 外围使用托管模型。命令与结果见[正确性说明](CORRECTNESS.zh-CN.md)。

这些验证支持基础四步流程、类型身份及生命周期契约。Unity 依赖导入、原生 Destroy、自动 Update、场景切换、域重载、Mono 与 IL2CPP 仍需目标工程运行；托管模型不代表已经运行过 Unity Editor/Player。

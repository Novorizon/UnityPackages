# 托管 ECS 优化与迁移说明

当前版本已按[去 MVC 迁移说明](MVC-DECOUPLING.zh-CN.md)改用 ECS 自有基础设施，基础包依赖 Framework 和 Mathematics。本文的最初匹配优化和性能记录是历史快照；旧 MVC 路径、工厂行为与计数不能代表当前实现。当前使用方式见[使用手册](USER-GUIDE.zh-CN.md)，验证以[最新记录](CORRECTNESS.zh-CN.md)为准。

本次实现前四项优化，第五项连续组件存储见 [规划](STORAGE-ROADMAP.zh-CN.md)，尚未实现。生产代码仍使用 Unity 2022.3 的托管 class 组件，没有宣称已具备 Chunk、Burst 查询或多世界实体隔离。

## 已实现

1. 系统以 Entity → dense index 索引定位成员。新增、匹配和删除成员平均 O(1)，删除以末尾元素补位，并同步移动全部组件列。所有 1～5 组件系统及两种 HybridSystem 一致处理。
2. 多组件匹配使用局部变量，去掉每次注入的临时数组；泛型类型 ID 和只读依赖表只初始化一次。重复注入会刷新已有实体的组件引用，包括 GameObject 替换。
3. World 管理 PreUpdate/Update/PostUpdate 的结构变更边界，合并同一实体的通知；组件类型反向索引只触达受影响系统，多依赖系统每批只通知一次。无 ISystemFilter 的自定义系统保留全通知行为。
4. 类型以运行期唯一 ID 标识，系统赋 World 后只初始化一次、注册回填已有实体；默认世界注册幂等，销毁会注销；系统回收清理所有框架匹配缓存。资源回调同时校验实体 GUID 和请求编号。Archetype 在添加之前验证重复类型，避免部分添加。

另外修复无 Odin 时的 World 命名空间依赖，以及 CopyToTransformSystem 丢弃实际 JobHandle 的问题。Jobs 修改仍需真实 Unity 运行验证。

## 必须关注的兼容变化

- 公开启动入口只有无参 `Ecs.Initialize()`。旧 marker 和程序集参数重载已移除；泛型创建自动登记工厂，Type/string/Archetype 创建须先显式 Register，不能继续依赖启动扫描或 LoadTypes。
- 数据组件限定 `class : IComponent`。旧 struct 组件在泛型入口不再编译，Type/string 入口会明确拒绝；未来 unmanaged 组件是独立规划，不复用当前按值 API。
- `GetComponentData(entity, int)`、`RemoveComponentData(entity, int)` 和 `EntityArchetype.hash` 的整数现在是 `ComponentType.GetId<T>()` / `GetId(Type)` 的结果。`Entity.StringToHash` 保留为通用旧工具，但不能再用来标识组件。运行期 ID 不可持久化或作为网络协议 ID。
- swap-remove 不保证实体遍历顺序及 OnUpdate index 稳定。不要保存 index 作为实体身份；需要确定顺序的逻辑应显式维护业务顺序。
- 帧内 RegisterSystem 返回待注册实例，初始化及回填在帧末提交，下一帧参加执行。不要在返回后立即假定 World 和初始化状态可用；启动期间可在帧外注册。
- 泛型 RegisterSystem<T>() 现在直接返回 T。把多种系统放入同一数组时显式声明 `ISystemBase[]`，避免依赖旧接口返回类型的数组类型推导。
- 框架只保证主线程使用。对象池中的并发容器不使 World、EntityManager 或系统成员列表线程安全。

## 帧与批处理语义

通过 `World.OnUpdate` 驱动系统。整个 PreUpdate → Update → PostUpdate 期间，移除组件、销毁实体、移除/注册系统延迟到帧末。这样同帧的所有系统都能完成既有匹配快照，避免当前实体删除导致后继实体漏执行。

AddComponentData 需要返回可赋值的对象，所以会立即创建并写入实体字典，系统匹配通知延迟到提交。直接 GetComponentData 可立即看见新增组件，但依赖该组件的新系统成员下一帧才执行。因此这是“匹配快照 + 延迟破坏性变更”，不是所有 API 都延迟的数据库事务。

同一帧 Remove<T> 后立即 Add<T> 仍会因组件尚未移除而报重复；可修改现有组件，或把组合操作放到安全的帧外提交阶段。命令按提交顺序执行，不自动消除具有初始化/回收副作用的 Add/Remove。

帧外可用自动配对作用域合并初始化通知：

```csharp
using (WorldManager.Instance.Batch())
{
    var entity = EntityManager.Create();
    EntityManager.Instance.AddComponentData<Position>(entity).Value = 0;
    EntityManager.Instance.AddComponentData<Scale>(entity).Value = 1;
}
```

批处理可以嵌套，最外层作用域结束才提交通知。BeginBatch/EndBatch 低层接口仍可用，需用 try/finally 配对。不能在外部批处理尚未结束时调用 World.OnUpdate，框架会抛异常。直接手动调用 System.Update 并在其内部注入/移除实体不受 World 边界保护。

批处理的去重对象是“实体 × 受影响系统”：一个组件的反复通知合并，多种组件依赖同一系统也只匹配一次。GameObject 变化只通知 Hybrid/对象过滤系统和自定义 fallback 系统。ISystemFilter 的依赖必须完整且在注册期间保持稳定；类型 ID 数组不可来自旧字符串哈希。

## 回收、异常与异步

- OnRecycle 的系统派生实现必须调用 base.OnRecycle，让基类清理成员索引、实体列表、组件列及 World。业务字段仍由业务系统重置。
- 普通组件复用不会自动恢复字段默认值。通过 IInitializeable.OnInitialized 或 IRecycleable.OnRecycle 明确重置，不能依赖构造函数在每次复用时调用。当前 ECS 不再提供 ReflectionProvider 或程序集扫描；动态创建类型由业务显式登记工厂。
- 批处理中移除的组件在匹配缓存全部解除/刷新后才进入对象池，防止旧系统持有已复用对象。销毁在剔除缓存后退休组件及 Entity。
- 某个系统/世界注入失败时仍尝试通知其它系统并完成排队清理，然后重新抛出首个异常；这不提供任意用户回调的事务回滚。自定义系统自己的缓存、回调副作用和异常恢复仍由调用方管理。
- Entity 对象可以复用，旧对象引用可能指向新实体。长期任务应保存 GUID，并在执行时通过 TryGetEntity(GUID) 解析，不能事后读取旧引用的 GUID 当成旧身份。
- LoadGameObject 的旧实体回调和同一活实体的旧请求都会被拒绝；无效加载结果回收。替换对象时移除旧反查映射，刷新 Hybrid 缓存，再回收旧对象。

## 验证与性能

测试项目放在 `Tests~`，使用 Unity 忽略的目录名，避免 .NET 8 Harness 源文件被 Unity 包导入编译。项目直接引用生产 ECS 和池代码；Unity/MVC 外围使用明确的边界桩。

```sh
cd 'Tests~/CoreHarness'
dotnet build -c Release --configfile NuGet.Config
dotnet run -c Release --no-build -- test
DOTNET_TieredCompilation=0 dotnet run -c Release --no-build -- bench ../results-after
```

[64 项回归结果](../Tests~/correctness-results.after.json)全部通过；[性能前后对比](../Tests~/PERFORMANCE-AFTER.md)记录上一轮匹配优化的 34 场景、170 个正式原始样本及稳态 A/B 复核（对应文件指纹保留）；本轮启动入口和池身份修复未重新采集完整性能基准。3 万实体创建 2197.58 → 53.47 ms，重匹配 1140.58 → 3.77 ms，重匹配分配 40 B → 0 B；稳态更新仍为 0 B，但未证明一致加速，部分样本有回退。删除与销毁每实体分配分别增加 8 B 与 72 B，完整数据如实保留。索引表增加常驻元数据和冷创建分配，以换取线性成员查找的消除；不要只比较时间而忽略空间成本。通知合并需要少量可复用缓冲，首次扩容、结构命令及用户委托并不承诺零分配；稳态更新和预热后的重复匹配目标为零临时托管分配。

当前环境没有 Unity Editor，尚未覆盖 Player 的 Mono/IL2CPP、Addressables、原生内存、Unity 延迟 Destroy/fake-null、Transform 数值及 Jobs 异常释放。目标 Unity 工程合入后应补这些集成检查。

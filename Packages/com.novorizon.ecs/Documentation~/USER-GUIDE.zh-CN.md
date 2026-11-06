# ECS 使用手册

先按[5 分钟上手](QUICKSTART.zh-CN.md)导入并运行 QuickStart。日常代码围绕三个概念：Entity 是身份，class 组件保存数据，System 处理匹配的组件。组件匹配和缓存由框架维护。

## 依赖与安装

当前 ECS 管理专用对象池和生命周期接口，复用 Framework 的 Unity 单例，**不依赖 MVC 或 PureMVC**。基础包仍是 Unity 框架，依赖 Framework、UnityEngine 和 Mathematics；它不是无 Unity 的纯 .NET Core。

| 层 | 当前依赖与用途 |
| --- | --- |
| ECS | UnityEngine；Unity.Mathematics；自有 `ReferencePool`、`SingletonBehaviour<T>`（Framework 单例的 ECS 生命周期适配）、`IInitializeable`、`IRecycleable` |
| 基础包声明 | 同一发行标签的 Framework、Mathematics 1.2.6；Unity 最低声明为 2022.3 |
| 可选功能 | `ENABLE_JOBS` 的 Transform 同步需要项目自行提供兼容的 Jobs、Collections、Burst；Odin 代码受 `ODIN_INSPECTOR` 保护 |

通过 UPM 安装仓库根目录 `package.json`。业务 asmdef 引用 `ECS`、`Game.Framework.Core`、`Game.Framework.Unity`；代码直接使用 Mathematics 类型时再引用 `Unity.Mathematics`。QuickStart 的 asmdef 显式引用 ECS 和上述 Framework 程序集。旧项目如果同时使用 MVC，应显式写 `ECS.IInitializeable`、`ECS.ReferencePool` 等，避免与旧同名类型混淆；原来只实现 MVC 生命周期接口的组件需要改为 ECS 接口。

## 最小完整示例

将下面内容保存为 `MovementEntry.cs`，挂到一个空 GameObject，点击 Play。这个例子只修改数值；Inspector 不会自动显示运行时 Entity 组件，查看可视效果请运行 QuickStart。

```csharp
using ECS;
using UnityEngine;

namespace MyGame
{
    public sealed class GamePosition : IComponent, IInitializeable
    {
        public float Value;
        public void OnInitialized() { Value = 0f; }
    }

    public sealed class GameVelocity : IComponent, IInitializeable
    {
        public float Value;
        public void OnInitialized() { Value = 0f; }
    }

    public sealed class MoveSystem : ComponentSystem<GamePosition, GameVelocity>
    {
        public ulong EntityId;

        public override void OnInitialized() { EntityId = 0; }

        protected override void OnUpdate(
            int index, Entity entity, GamePosition position, GameVelocity velocity)
        {
            if (entity.GUID == EntityId)
                position.Value += velocity.Value * World.TimePerFrame;
        }
    }

    public sealed class MovementEntry : MonoBehaviour
    {
        private World world;
        private MoveSystem system;
        private ulong entityId;

        private void Start()
        {
            world = Ecs.Initialize();
            Entity entity = EntityManager.Create();
            entityId = entity.GUID;
            EntityManager.Instance.AddComponentData<GamePosition>(entity).Value = 0f;
            EntityManager.Instance.AddComponentData<GameVelocity>(entity).Value = 1f;
            system = world.RegisterSystem<MoveSystem>();
            system.EntityId = entityId;
        }

        private void OnDestroy()
        {
            // 应用退出时不再访问销毁中的单例。
            if (Ecs.IsQuitting || EntityManager.applicationIsQuitting || WorldManager.applicationIsQuitting ||
                ReferencePool.applicationIsQuitting) return;
            // 只清理本入口创建的对象；默认 World 由其它业务共享。
            if (system != null && system.EntityId == entityId &&
                object.ReferenceEquals(system.World, world)) world.RemoveSystem(system);
            if (entityId != 0)
            {
                EntityManager manager = EntityManager.Instance;
                if (manager != null) manager.DestroyEntity(entityId);
            }
        }
    }
}
```

每个入口持有自己返回的 system 和捕获的 GUID。这里用 GUID 限制系统只处理自己的实体，所以多个示例入口不会重复移动对方的数据。真正的全局移动系统通常只注册一次，并处理所有匹配实体。

## 初始化与 World

公开启动入口只有无参 `Ecs.Initialize()`，在 Unity 主线程调用即可。它初始化 ECS 自有管理器并返回稳定默认 World，**不枚举程序集中的类型**。泛型创建在首次使用时记录 `() => new T()` 工厂，随后复用对应 CLR Type 的池：

```csharp
World world = Ecs.Initialize();
MoveSystem system = world.RegisterSystem<MoveSystem>();
Entity entity = EntityManager.Create();
EntityManager.Instance.AddComponentData<GamePosition>(entity);
```

公开无参构造函数由 `new()` 泛型约束保证。组件或系统位于不同 asmdef 中也不需要加载其程序集；调用方只需正常引用相应程序集。旧 `Ecs.Initialize<TMarker>()` 与传入程序集参数的 Initialize 重载已移除，调用方必须改为无参入口。框架不再提供 marker、程序集加载或批量扫描登记接口。

重复初始化安全，但每次 `RegisterSystem<T>()` 都注册一个系统实例，并不是获取已有系统单例。保存返回值，清理时调用 `world.RemoveSystem(system)`。动态 Type/string/Archetype 入口需要显式工厂注册。旧代码若依赖初始化扫描后直接按 Type 或名称创建，必须补齐 Register，否则会报未注册类型；这部分旧动态调用需要业务迁移。见[类型入口](#类型入口与高级匹配)。

`Ecs.Initialize` 返回 `WorldManager.Instance.DefaultWorld`，由 WorldManager 的 Unity 更新自动驱动。不要在 MonoBehaviour.Update 中再次调用它的 `OnUpdate`。`World.Self` 表示最后初始化的 World，不保证是默认 World；外部业务也不能直接 `new World()`，其构造函数仍是 internal。

World 是系统执行组，实体记录和结构通知目前是全局的，并不提供实体归属隔离。不要用 World 数量推断实体隔离。`World.Destroy()` 清理该 World 的系统，也不是销毁全局实体的替代方法。

## 组件的创建、修改与回收

数据组件必须是实现 `IComponent` 的 **class**；非泛型入口也拒绝 struct。泛型 Add 还要求公开的无参构造函数。

```csharp
Entity entity = EntityManager.Create();
GameVelocity velocity = EntityManager.Instance.AddComponentData<GameVelocity>(entity);
velocity.Value = 2f;

GameVelocity same = EntityManager.Instance.GetComponentData<GameVelocity>(entity);
if (same != null) same.Value = 3f;

// 已有就返回，没有就创建。
GamePosition position = entity.GetOrAddComponentData<GamePosition>();
EntityManager.Instance.RemoveComponentData<GameVelocity>(entity);
EntityManager.Instance.DestroyEntity(entity);
```

Get 未找到时返回 null；同一实体再次 Add 同类型组件会抛异常。普通字段修改无需 Inject 或重新添加组件。

组件与系统来自 ECS 自有 `ReferencePool`。复用对象不保证重新执行构造函数；实现 `ECS.IInitializeable.OnInitialized()` 重置每次取出时的状态，也可用 `ECS.IRecycleable.OnRecycle()` 在回收时释放引用。列表、委托、owner、标志位都需要自行重置。不要在移除后继续保存或使用组件引用。

覆盖系统 `OnRecycle()` 时必须调用 `base.OnRecycle()`，让框架清除缓存实体、组件列表、索引和 World 引用；自己的额外状态也应清理。

## 实体身份与延迟访问

Entity 对象会被池复用。长期保存对象引用不能代表原来的实体，连该对象上的 `GUID` 也可能已变为新身份。创建时复制 GUID 值，延迟操作前重新解析：

```csharp
ulong savedId = entity.GUID;
// 以后某个回调或下一帧：
if (EntityManager.Instance.TryGetEntity(savedId, out Entity current))
{
    GamePosition p = EntityManager.Instance.GetComponentData<GamePosition>(current);
    if (p != null) p.Value = 10f;
}
```

死亡实体拒绝新增组件。销毁时可以直接 `DestroyEntity(savedId)`，找不到时无需额外处理。不要用系统回调中的 `index` 当作稳定 ID；成员采用 swap-remove，顺序和索引均可能变化。

## 系统与一帧内的可见性

`ComponentSystem<T1,...>` 自动匹配拥有全部指定数据组件的实体，支持 1～5 个组件。`OnUpdate` 的组件引用与同一 Entity 对齐。系统通过 `World.TimePerFrame` 读取本次更新的秒数；默认 World 使用 Unity 传入的帧间隔。

| 操作 | 系统帧内行为 |
| --- | --- |
| 修改已有组件字段 | 同一对象引用立即反映新值，后续读取可见 |
| Add 数据组件 | 立即加入实体字典，可立即 Get；系统成员匹配在帧末提交后刷新 |
| Remove 数据组件、Destroy Entity | 延迟到该 World 帧的 PreUpdate/Update/PostUpdate 完成后提交；当前帧仍使用原有成员快照 |
| Register/Remove 系统 | 帧内请求在帧末提交，新系统从下一次更新执行；注册会回填已有实体 |
| 帧外普通操作 | 通常同步完成并刷新匹配；显式 Batch 或重入通知期间会合并到边界 |

因此不要在一个系统回调内先 `RemoveComponentData<T>` 再立即 `AddComponentData<T>`：Remove 尚未提交，Add 会发现重复。要改变数值就修改现有组件；要改变是否参与更新，可分开提交移除和重加，或使用业务状态字段。

这些保证适用于由 World 驱动的更新。不要直接调用单个系统的 `Update()` 来获得结构变更保护。提交时会尽量完成其它系统的通知与清理，再重抛首个异常；框架不会回滚任意用户回调的业务副作用。

## 批量创建与修改

批量增加多个组件时用作用域合并匹配通知，退出 `using` 时提交。异常也会执行 Dispose；作用域支持嵌套，最外层结束后刷新。

```csharp
using (WorldManager.Instance.Batch())
{
    for (int i = 0; i < 100; ++i)
    {
        Entity item = EntityManager.Create();
        EntityManager.Instance.AddComponentData<GamePosition>(item).Value = i;
        EntityManager.Instance.AddComponentData<GameVelocity>(item).Value = 1f;
    }
}
```

Batch 合并通知，不是可回滚事务。Add 在作用域内仍可读取；帧外 Remove 会立即移出组件字典，Destroy 会立即将实体标为死亡，并不等到 Dispose 才执行。旧 `BeginBatch()/EndBatch()` 仍可用，必须 try/finally 配对。不要跨帧、跨 await 保持 Batch 打开，也不要在结束之前调用 World.OnUpdate，否则会抛异常。

## GameObject 与混合系统

数值实体用 `EntityManager.Create()`；绑定已有场景对象用 `EntityManager.Create(gameObject)`。后者可通过 `TryGetEntity(gameObject, out entity)` 找到对应实体。

`AddComponentObject<T>`、`GetComponentObject<T>`、`RemoveComponentObject<T>` 操作 Unity Component；它们与 `Add/Get/RemoveComponentData<T>` 的 class 数据组件不同。创建绑定后通过这些 API 变更对象组件，框架才能通知 Hybrid 刷新缓存。不要直接给 `entity.gameObject` 赋新对象来替换绑定。

`HybridSystem<UnityComponent, DataComponent>` 或三参数版本还要求 GameObject 上有指定 Unity Component。例如 `HybridSystem<Transform, GamePosition>` 处理同时具有 Transform 和 GamePosition 的实体。渲染、Transform 或原生对象销毁的时序仍需在目标 Unity 工程验证；实际 Unity Destroy 延迟与 fake-null 不能由 .NET 桩完全模拟。

## 异步资源

`EntityManager.Instance.LoadGameObject(entity, assetPath, ...)` 使用 ECS 的 `IGameObjectProvider`。调用前显式设置 `EntityManager.Instance.GameObjectProvider`；默认未配置时抛出明确异常。Ecs.Initialize 不启动资源框架，基础 QuickStart 也不走这条路径。

Provider 接收 `GameObjectRequest` 并通过回调返回 `GameObjectLease`，回调必须回到 Unity 主线程且只完成一次；null 租约表示加载失败。拥有的租约用 `GameObjectLease.Owned(go, release)` 捕获原始来源的释放方法，例如自己对象池的回收；借用场景对象用 `Borrowed(go)`，Dispose 不销毁它。租约自身的 Dispose 幂等。

`EntityManager.Create(go)` 默认拥有对象，销毁时调用 Unity Destroy。借用对象请明确写 `Create(go, GameObjectOwnership.Borrowed)`；外部池或资源服务可用 `Create(lease)` / `BindGameObject(entity, lease)`，让框架按原租约释放。不要把同一对象交给多个实体或多个拥有租约。

框架以创建时捕获的 GUID 和请求序号检查回调。实体销毁/复用后的旧请求、乱序完成的旧加载不会绑定到新实体；无效结果通过自己的租约释放。有效替换先清除旧映射并刷新 Hybrid，再释放旧租约。业务自己的延迟回调也应保存 GUID 后重新查找。

`EnableDataMode(true)` 是全局设置，会跳过 LoadGameObject；它不移除 Unity 编译依赖。`DestroyEntity(entity, delay)` 的实体数据在提交时已经退役，delay 仅用于延迟释放 GameObject 租约。队列由 EntityManager 自动更新，按 scaled delta 前进，timeScale 为 0 时不累计；不能把延迟销毁的实体继续当活实体使用。

## 类型入口与高级匹配

优先使用泛型入口，首次 `AddComponentData<T>()` / `RegisterSystem<T>()` 自动登记工厂。池不通过类型名去搜索程序集中未知的类。

当类型来自配置、反序列化或 `EntityArchetype` 的 `Type[]` 时，先由业务明确注册所有可创建类型：

```csharp
ReferencePool.Instance.Register<GamePosition>();
ReferencePool.Instance.Register<GameVelocity>();
// 注册后可走 Type/string/Archetype 创建路径。
Entity entity = EntityManager.Create();
EntityManager.Instance.AddComponentData(entity, typeof(GamePosition));
```

`Register<T>()` 缓存 `() => new T()`；也可用 `Register<MyComponent>(() => new MyComponent(config))` 提供自定义工厂。`Register(Type, Func<object>)` 供动态配置使用，返回对象必须恰好属于注册类型。自定义工厂要在首次取用前配置；同类型已经登记不同工厂时不允许替换，以免混用池对象来源。重复默认 Register 不覆盖已有自定义工厂。未登记类型明确报错，不会退回按名称反射构造或程序集扫描。单纯调用 `ComponentType.GetId<T>()` 只是分配匹配 ID，不等于登记创建工厂。

字符串入口仅解析已注册类型。唯一完整类型名可以使用；跨程序集存在相同完整类型名时使用 `typeof(T).AssemblyQualifiedName`，歧义名称会拒绝解析。实际池按 CLR Type 隔离。

整数入口需要 `ComponentType.GetId<T>()` 或 `ComponentType.GetId(typeof(T))`。ID 仅在当前进程有效，不用于存档、网络协议或跨次启动持久化；旧 `Entity.StringToHash()` 不是组件 ID。

泛型路径的构造函数在代码中直接引用，默认创建无需反射扫描。配置驱动的动态类型仍应通过可达的显式注册代码登记，按需要配置 `link.xml` 或 Unity 的保留机制，并在目标裁剪级别下验证。QuickStart 保留保守的 `link.xml` 配置；.NET 测试通过不能代替 IL2CPP Player 构建。

普通 ComponentSystem 和 HybridSystem 已提供精确路由，不需要手写过滤器。自定义 `SystemBase` 可实现 `ISystemFilter`，提供稳定、完整的 `IReadOnlyList<int> ComponentTypeIds` 和 `bool RequiresGameObject`，让框架只在相关结构变化时通知。遗漏依赖会导致旧匹配；依赖集合不应在注册后变更。不实现过滤器的自定义系统走全量 fallback 通知，兼容已有行为。

## 清理与运行边界

销毁自己的 GUID 并移除自己保存的系统实例即可。通用场景入口不要调用全局 `DestroyAll()`，也不要销毁共享的 WorldManager、EntityManager、ReferencePool。多个 demo 和业务对象共享默认 World，清理全局管理器会影响其它使用者。

实体、World、结构变更和 Unity 对象 API 均在主线程使用。对象池和 ECS 的生命周期回调同样应在主线程执行。异步结果必须回到合适的 Unity 主线程上下文。

手动 `world.OnUpdate(delta)` 用于没有 Unity 自动驱动的独立测试环境。仓库测试的边界桩不会触发 Unity Update，所以能显式更新默认 World；不要原样复制到正在运行 Unity 的入口里。

## 验证

导入 QuickStart 后使用实际 Unity 编译、Play 和生命周期检查。当前仓库工具与验证边界见 [统一验证说明](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.24/Documentation/Validation.md)。

# 5 分钟上手

此版本面向 Unity 2022.3，ECS 自带专用对象池和生命周期接口，单例基础能力由 Framework 提供，**不依赖 MVC 或 PureMVC**。基础包依赖 Framework、UnityEngine 和 Mathematics；QuickStart 不需要资源框架配置。详细依赖见[使用手册](USER-GUIDE.zh-CN.md#依赖与安装)。

## 1. 安装包

在 Unity 的 **Window → Package Manager → + → Add package from git URL** 添加：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework#v0.1.0-preview.25
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.ecs#v0.1.0-preview.25
```

上面使用固定发行标签。本地开发可 Add package from disk，选择 Packages/com.novorizon.ecs/package.json；同时声明 Framework 和 Mathematics 来源。

## 2. 运行可视化 demo

1. 在 Package Manager 选中 **ECS Framework**，展开 **Samples**，导入 **QuickStart**。
2. 新建普通 3D 场景，保留 Main Camera 和 Directional Light；创建空 GameObject，挂载导入的 `QuickStartDemo.cs`。
3. 点击 Play。demo 会创建一个 Cube，沿世界坐标 +X 移动，默认速度为每秒 1 个单位。
4. 点击屏幕上的 **Pause / Resume** 观察暂停、继续；**Restart** 使位置回到 0。结束 Play 后示例只清理自己的实体、系统和 Cube。

源码位于包内 `Packages/com.novorizon.ecs/Samples~/QuickStart`；导入后通常位于 `Assets/Samples/ECS Framework/<包版本>/QuickStart`。示例自行创建 Cube，不需要 Prefab 或 Addressables 资源配置。若没有看到 Cube，先在 Scene 视图选中它并按 F 聚焦，或调整 Camera 朝向原点。

示例的 `ECS.QuickStart.asmdef` 引用 `ECS`、`Game.Framework.Core` 和 `Game.Framework.Unity`。自己的 asmdef 同样引用这些程序集；直接使用 `Unity.Mathematics` 类型时再引用 `Unity.Mathematics`。

## 3. 理解主流程

普通业务代码只需要以下步骤。这里的 `GamePosition`、`GameVelocity`、`MoveSystem` 定义在[完整可复制示例](USER-GUIDE.zh-CN.md#最小完整示例)中：

```csharp
using ECS;
using MyGame;

World world = Ecs.Initialize();
Entity entity = EntityManager.Create();
ulong entityId = entity.GUID;
EntityManager.Instance.AddComponentData<GamePosition>(entity).Value = 0f;
EntityManager.Instance.AddComponentData<GameVelocity>(entity).Value = 1f;
MoveSystem system = world.RegisterSystem<MoveSystem>();
system.EntityId = entityId;
```

公开启动入口只有无参 `Ecs.Initialize()`，返回稳定默认 World，重复调用安全，不扫描程序集。`AddComponentData<T>()` 和 `RegisterSystem<T>()` 首次调用时以 `new T()` 工厂自动登记对应类型；业务类型分布在多个 asmdef 也无需另行加载。组件使用 `class : IComponent`，系统继承 `ComponentSystem<...>`，在 `OnUpdate` 中处理数据。旧 marker/程序集参数初始化入口已移除；Type/string/Archetype 动态创建必须先显式 Register，迁移细节见使用手册。

**默认 World 已由 Unity 自动更新。** 不要再从自己的 `Update` 调用 `world.OnUpdate()`。移动系统使用 `World.TimePerFrame`，所以速度按秒计算。

## 三条使用规则

- 组件来自对象池。通过 `ECS.IInitializeable.OnInitialized()` 重置字段，或在每次获取后完整赋值；不要依赖构造函数每次重跑。
- 修改已有组件字段立即可见。系统执行中的移除、销毁在帧末提交；新增组件立即可读取，新匹配从下一次系统执行开始参与。不要在同一个系统回调内先 Remove 再 Add 同类型组件。
- 长期保存实体时保存 **GUID 值**，用 `TryGetEntity` 重新查找。退出时只销毁自己创建的实体、移除自己注册的系统；不要在通用组件中调用全局 `DestroyAll()`。

需要批量创建、GameObject 混合系统、异步资源或自定义匹配时，继续阅读[使用手册](USER-GUIDE.zh-CN.md)。

## 验证范围

Import 后编译示例，在真实 Unity Play 中检查移动、暂停、重启和退出；未导入的 Samples~ 不参与编译。完整流程见仓库 [Demo 索引](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.25/Documentation/Samples.zh-CN.md)。

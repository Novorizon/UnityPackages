# QuickStart：一个移动方块

这个示例展示最短的实际使用流程：初始化 → 注册系统 → 创建实体和组件 → 观察移动 → 移除/恢复组件 → 清理自己创建的内容。

## 在 Unity 中运行

1. 在 Unity 2022.3 或项目实际使用的兼容版本安装本包，确保包声明的依赖已经解析。当前 ECS 使用自有池、单例与生命周期接口，基础依赖为 Framework、UnityEngine 和 Mathematics；示例不使用 MVC/PureMVC 或资源加载 provider。
2. 打开 Package Manager，选择 **ECS Framework**，在 **Samples** 下导入 **Quick Start**。也可把本目录整个复制到项目 `Assets`，保留 `ECS.QuickStart.asmdef`。
3. 创建或打开一个普通 3D 场景，保留 Main Camera 和 Directional Light。相机位于 `(0, 1, -10)` 附近并朝向前方即可看见初始方块。
4. 新建空 GameObject，添加 **Quick Start Demo** 组件，默认 Speed 为 1，然后进入 Play。

方块会从原点沿 +X 方向以每秒 1 单位移动。左上角显示 X 值：**Pause** 移除速度组件，位置停止；**Resume** 恢复速度组件，继续移动；**Restart** 清理本实例后从原点重建。方块持续向前移动，移出相机范围后可点 Restart。

退出场景或删除挂载组件时，本实例只销毁自己创建的实体、系统和方块。示例不调用 DestroyAll、不销毁共享默认 World 或管理器。可以放置两个示例组件；各自的 owner 标记避免同类系统把另一个实例的实体重复更新。

## 代码从哪里看

| 文件 | 作用 |
| --- | --- |
| `QuickStartComponents.cs` | 三个 class 组件，并通过 ECS.IInitializeable 显式重置池复用状态。 |
| `QuickStartMovementSystem.cs` | `position.X += velocity.Speed * World.TimePerFrame`；只处理自己 owner 的实体。 |
| `QuickStartSession.cs` | 一行初始化、注册、批量添加、Pause/Resume 和按 GUID 清理；与终端 smoke 共用的业务代码。 |
| `QuickStartDemo.cs` | Unity 入口和方块显示；只读取 ECS 结果，不手动驱动默认 World。 |
| `link.xml` | 保守保留示例类型和 Entity 构造函数，供 Unity 代码裁剪验证使用。 |

公开启动入口只有无参 `Ecs.Initialize()`，它初始化自有管理器并返回稳定默认 World，不扫描程序集。泛型组件/系统首次使用时自动记录 `new T()` 工厂。默认 World 已由 WorldManager 自动更新；**不要再在自己的 Update/LateUpdate 中调用 World.OnUpdate**。

旧 marker/程序集参数初始化入口已移除。本例的泛型创建无需另行注册；改用 Type/string 或 Archetype 类型数组时，必须在首次动态创建前显式 `ReferencePool.Instance.Register<T>()`，不能依赖启动扫描。

`Pause`/`Resume` 按钮在系统遍历之外完成组件变更；如果在系统回调中使用同类操作，Remove 会延迟到帧末，不能紧接着 Add 同类型组件。本例直接保存 GUID 而不是依赖可复用 Entity 对象的长期身份。

## 终端验证范围

仓库中的 `Tests~/DemoSmoke` 直接编译这些源文件到独立的消费程序集，引用另一程序集中的真实 ECS 核心，验证公共 API 可见性、移动数值、多实例隔离、暂停恢复、池复用重置、资源清理以及 Unity 入口不重复 tick。

Unity 外围使用最小模型桩；测试通过不表示已经在 Unity Editor/Player 运行了画面，也不验证渲染、原生对象销毁、Mono/IL2CPP 或实际帧顺序。`link.xml` 为示例提供代码裁剪保留配置，仍需在目标平台验证 IL2CPP 构建；配置驱动的动态类型还需显式 Register 工厂，并根据项目裁剪配置保留。

完整项目文档位于包内 `Packages/com.novorizon.ecsframework/Documentation~/QUICKSTART.zh-CN.md`；在仓库中可打开 [快速开始](../../Documentation~/QUICKSTART.zh-CN.md)。

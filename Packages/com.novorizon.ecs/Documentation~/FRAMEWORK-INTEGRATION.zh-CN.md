# Framework 接入与生命周期边界

ECS 2.0.0-preview.2 面向 Unity 2022.3，依赖 Framework 0.10.0-preview.1 与 Mathematics 1.2.6。Framework 不依赖 ECS，ECS 与技能包之间也没有依赖或 IUnit 桥接。

## 安装

先安装对应版本的 Framework 主包，再安装 ECS。Git 安装时在项目 manifest 中显式指定两个仓库地址；package.json 的版本依赖不会替项目选择一个 Git 分支。

当前接入分支：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework#v0.1.0-preview.3
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.ecs#v0.1.0-preview.3
```

自己的 asmdef 引用 ECS、Game.Framework.Core、Game.Framework.Unity，直接使用数学类型时另引用 Unity.Mathematics。QuickStart 已包含所需程序集引用。

## 复用和保留

- Framework.MonoSingleton 管理场景发现、创建、重复对象、逻辑退役、引用缓存和退出保护。
- ECS.SingletonBehaviour 保留公开入口和初始化/更新回调，继承该公共基类，不再维护一套对象发现/创建实现。
- World、EntityManager、ReferencePool 的启动、初始化幂等、更新阶段和清理顺序仍由 ECS 决定。
- ECS 的 ReferencePool 包含动态 Type/工厂登记、组件与系统的不同初始化时机、重入保护和退役规则，继续留在 ECS。它与 Framework 的 ReferencePool<T> 契约不同，不为此增加 Framework 专用接口。
- GameObjectProvider 与 GameObjectLease 继续作为 ECS 的资源所有权边界；项目可以接入 Framework 池或任意资源实现。

不更改组件匹配、结构变更事务、实体标识、World 执行顺序或默认自动驱动行为。仍然是多个 World 共享全局实体表，不新增运行时隔离。

## 会话与退出

ECS 按 World → EntityManager → ReferencePool 顺序清理。生命周期拥有者引用用于清理；Framework 即使先重置单例缓存，ECS 仍可释放上一会话的对象和租约。Framework 的重置先执行或后执行，都不能丢失这部分清理。

DestroyInstance 会先逻辑退役，再请求 Unity Destroy。TryGetInstance 不创建对象。退出过程中拒绝新建，下一 Play 会话允许重新初始化存活的场景组件。

## 验证

运行 Tests~/verify.sh，需要 .NET 8 和一个相邻的 Framework 工作副本，也可指定 FrameworkRoot。核心模型直接编译两边的生产源码，外部消费和 QuickStart 检查分别编译 Unity 边界、Framework 单例与 ECS 程序集。

本轮增加两种 Framework/ECS 重置顺序的租约释放、旧实体退役和重新初始化检查。现有组件、系统、池、延迟资源完成和异步回调边界回归继续运行。托管模型不代表实际 Unity Editor、IL2CPP 或设备验收。

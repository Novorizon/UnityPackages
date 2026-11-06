# Framework 接入与生命周期边界

ECS 面向 Unity 2022.3，依赖同一发行标签中的 Framework 与 Mathematics 1.2.6。Framework 不依赖 ECS，ECS 与 Ability 独立。

## 安装

先安装对应版本的 Framework 主包，再安装 ECS。Git 安装时在项目 manifest 中显式指定同仓库的两个包地址；package.json 的版本依赖不会替项目选择一个 Git 分支。

安装地址：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.framework#v0.1.0-preview.25
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.ecs#v0.1.0-preview.25
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

在 GameStarter 导入 QuickStart 后进行实际 Unity 编译和 Play，步骤见仓库 [Demo 索引](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.25/Documentation/Samples.zh-CN.md)。源仓库的旧 Tests~ 工具不在本 UPM 包内，不要求相邻 Framework 工作副本。

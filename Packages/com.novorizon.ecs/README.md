# ECS

Unity 2022.3 托管组件框架，使用 `class : IComponent` 保存数据，由 World 执行系统。单向依赖 Framework 和 Mathematics，不依赖 Ability、MVC 或 PureMVC。

- [快速开始](Documentation~/QUICKSTART.zh-CN.md)
- [使用手册](Documentation~/USER-GUIDE.zh-CN.md)
- [Framework 接入与清理](Documentation~/FRAMEWORK-INTEGRATION.zh-CN.md)
- [结构事务](Documentation~/WORLD-TRANSACTIONS.zh-CN.md)
- [QuickStart Demo](Samples~/QuickStart/README.md)

流程：`Ecs.Initialize()` → 注册系统 → 创建实体 → 添加组件。泛型工厂自动登记，Type/字符串/Archetype 动态创建前需显式登记类型。默认 World 由 Unity 自动驱动，不要重复 Tick。

Framework 提供公共单例；实体、组件/系统池、工厂和清理顺序保留在 ECS。World 是共享实体表上的系统执行组，不提供实体隔离、Chunk 或 Burst 查询。

示例可以直接创建 Cube，验证移动、暂停、重启和清理；不需要业务 Prefab 或资源 provider。真实 Unity 编译与验收见仓库 [Demo 索引](https://github.com/Novorizon/UnityPackages/blob/v0.1.0-preview.24/Documentation/Samples.zh-CN.md)。

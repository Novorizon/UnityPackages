# Runtime 公开接口约定

以提交 65794c791ab1f9ab59d2833bf79f867480f42411 的 0.2.1 行为为基线，本轮发布 0.2.2-preview.1。程序集和命名空间保持 Game.Ability，不重命名公开类型或删除已有入口。

- AbilitySystem 持有战斗作用域；项目显式 Initialize、Update、RegisterUnit、RemoveUnit、ClearRuntime。
- IUnit/IWorld/IResourceOwner/IPresentation 是 Ability 自己的边界。项目可以直接实现或适配；ECS 不依赖 Ability，也不要求 ECS 实体实现 IUnit。
- AbilityScript / ModifierScript 和工厂注册用于扩展项目技能；具体角色、技能、资源服务和生成表不进入运行时包。
- 技能、光环和 Modifier 的计时由调用方提供 simulation delta 推进，保留暂停、长帧和战斗清理语义，不替换为全局墙钟定时器。本包没有另建通用单例/对象池。
- Runtime 不依赖 Editor、Odin、ECS。Odin 编辑器作为 com.novorizon.ability.editor.odin 单独安装，显式依赖配套 Runtime，未安装编辑器也可运行技能。

Tools/AbilityVerification/ApiContract/public-api.txt 记录 1083 条公开类型及成员签名。检查器通过独立的 Game.Ability 程序集反射比较公开类型、字段、枚举值、构造、方法、属性访问器、事件、参数名称及默认值；接口改动会使验证失败，须先审查兼容性、调整版本并显式更新快照。快照不是对 IL2CPP 或 Unity 原生兼容性的替代验证。

运行：dotnet run --project Tools/AbilityVerification/ApiContract -c Release。仅在审查接口变更后使用 -- --write 更新基线。419 项运行时行为回归和独立包消费者继续验证该接口的实际使用。

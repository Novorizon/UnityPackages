# Ability

运行时和编辑器在同一个包中，公开接口遵循[API 冻结约定](Documentation-public-api.md)。

面向 Unity 2022.3 的独立 Dota 风格技能包。公开命名空间为 `Game.Ability`，程序集为 `Game.Ability`。

## 依赖方向

业务依赖技能包的公开 API 是正常用法：可以持有 `AbilitySystem`、`Ability`、`Modifier`、`CastResult`，实现 `IUnit` 等接口，并注册自己的技能脚本。这里的解耦要求是**技能包不引用具体项目或业务实现**，而不是业务不能引用技能类型。

运行时程序集只依赖 Unity 基础模块和 .NET 标准库，没有 Npc、TowerManager、DataManager、资源单例、数据表生成器或第三方插件依赖。`Game.Ability.asmdef` 没有其他用户程序集引用。只复制这个目录即可编译；也提供 UPM `package.json`。

| 包负责 | 项目负责 |
| --- | --- |
| 施法、队列、冷却、充能、Modifier、状态/属性、结算、投射物和事件规则 | 角色模型、HP数据、输入/AI、存档、物品/法力、世界查询、资源加载与表现 |
| 定义/JSON读取、校验和发布 | 提供要启用的定义来源、读取JSON文本、授予单位技能 |
| `AbilityScript` / `ModifierScript` 扩展点 | 在自己的程序集实现并注册特殊技能，不修改包源码 |

项目可以让自己的实体实现接口，也可以使用专门适配器。游戏的单位、资源与表现适配器放在自己的业务程序集，不随本包发布。

0.2.1 修复长帧计时与异常收尾、来源归属、深引用校验和事件查询。技能来源必须曾成功登记、属于当前引擎和战斗代际，Owner 未被注销；同代普通移除技能后，已发射对象仍可保留技能归因。死亡但尚未注销的来源与 null 环境来源仍被支持。精确 HP 适配器须返回已提交事实，项目提交后的通知异常应自行隔离。

IWorld 可返回粗候选；FindUnits 最终按有限坐标和精确三维球半径过滤，并按 EntityId 去重。它向结果列表追加，不清空已有条目；活跃实体ID须唯一，注销后复用ID应显式RegisterUnit。平面战斗应通过适配器提供一致的平面战斗坐标。内置静态状态筛选不消耗脚本操作次数，但调用深度和自定义状态回调仍受预算保护。

## 导入

通过一个 Git URL 安装运行时及编辑器：

```text
https://github.com/Novorizon/UnityPackages.git?path=/Packages/com.novorizon.ability#v0.1.0-preview.24
```

也可从 Package Manager 选择本目录 package.json 或导出的单个 Ability tarball。`Game.Ability.asmdef` 编译运行时；`Editor/Game.Ability.Editor.Odin.asmdef` 仅编译编辑器。没有 Odin 时运行时正常可用，工作台显示安装提示；Odin 插件由项目提供。

编辑器使用见[编辑器指南](Documentation~/Editor/README.md)。

## 最小接入流程

在业务程序集实现 `IUnit`、`IWorld`、`IResourceOwner`、`IPresentation`。需要精确治疗量、伤害量与 NonLethal 时实现 `ICombatUnit`；移动投射物表现可实现可选的 `IProjectilePresentation`。

```csharp
var engine = new AbilitySystem();
engine.Initialize(myWorld, myPresentation);

var registry = new AbilityDefinitionRegistry();
registry.LoadProvider(new JsonAbilityDefinitionProvider(jsonText, sourceName));
// requiresCustomScript 的工厂在发布前注册，脚本代码位于业务程序集。
// engine.RegisterAbilityScript("my.chain_lightning", () => new MyChainLightning());
if (!registry.ApplyTo(engine))
{
    // 使用 registry.LastApplyValidation 显示源位置和错误。
    return;
}

engine.RegisterUnit(caster);
Ability ability = engine.AddAbility(caster, "my.arc_bolt", 1, myResources);
var order = new CastOrder { Caster = caster, AbilityName = "my.arc_bolt", Target = target };
CastResult preview = engine.EvaluateOrder(order);
if (preview.Success)
{
    engine.IssueOrder(order);
}
// 每个 simulation tick 调用 engine.Update(deltaTime)。
// 单位注销：engine.RemoveUnit(unit)；战斗结束：engine.ClearRuntime()。
```

实际输入到来时必须再次 `IssueOrder` 校验，预检查不保留资源，也不保证未来执行成功。`CastFilter` 和数值 getter 是只读查询契约；自定义脚本不能在其中自行产生业务副作用。预检查不触发 OrderIssued、前摇钩子、费用、施法ID或队列变更。

## 公开 API 覆盖

| 场景 | 入口 |
| --- | --- |
| 定义/脚本 | Provider + Registry，RegisterAbilityScript / RegisterModifierScript |
| 单位与技能 | RegisterUnit / RemoveUnit，AddAbility / FindAbility / GetAbilities / RemoveAbility |
| 学习/可用性 | Ability.SetLevel / SetActivated；0级未学习 |
| 查询与施法 | EvaluateOrder / IssueOrder，CastAbilityOnTarget / CastAbilityOnPosition，Stop / GetQueuedOrders |
| 冷却与充能 | CooldownRemaining / CooldownDuration，Charges / ChargeRestoreRemaining，相关设置/恢复方法 |
| Buff/状态/属性 | AddModifier / RemoveModifier / Purge，HasState / GetModifierProperty |
| 战斗 | ApplyDamage / HealWithResult，BeginAttack / ResolveAttack / CancelAttack，NotifyDeath / NotifyRespawn |
| 持续对象 | Create/RemoveProjectile，Create/RemoveThinker |
| 观察与调试 | EventRaised、CastId/AttackId、CreateRuntimeSnapshot |

`CooldownDuration` 记录本次开始冷却时的时长，冷却期间属性变化不会改变进度条分母。接口结果和事件不能当作编辑运行时定义的入口。

## 内置机制与具体技能

包内置伤害、治疗、Modifier、驱散、投射物、光环、计时、状态与事件等通用机制；火球、冰环、毒、吸血、反伤等具体技能优先用 JSON 组合，而不是每个技能添加一个包内类。

特殊链式选敌等不能直接表达的行为，在业务程序集继承 `AbilityScript` / `ModifierScript` 后注册工厂；JSON 可继续提供跳数、范围、衰减等参数。需要通用新机制时才扩展包，不把项目资源路径、角色类型或某个技能ID写进包。

0.2.0 增加 Modifier 的 `levelProperties`，例如 `[{"property":6,"value":{"values":[1.5,3,4.5]}}]` 表示按来源技能当前等级读取护甲。旧 `properties` 固定 float 继续兼容，同一属性不能在两处重复；无来源技能时使用施加等级，层数只乘一次。光环子效果也实时跟随来源等级，不缓存等级属性结果。

`actions` 在施法执行时运行，`onProjectileHit` 只在该技能自己的弹体命中时运行，避免多个技能串触发。`triggerExcludedDamageFlags` 可按 modifier 过滤反射等事件；其他监听者仍会收到自己的事件。

JSON 必须经过严格解析和最终图校验；未知字段/拼写、类型、特殊值引用、立即创建环、不支持能力都应报错。发布会复制定义图，运行时按只读约定使用。资源路径如何加载由项目决定，包只接收字符串和 `IPresentation`。

## 时间、光环与扩展约定

普通 Modifier 的周期回调按有效时间推进，到期边界最后一次回调仍执行。新对象不追扣创建前的整帧时间。光环已有子效果在 Update 开始采样覆盖，覆盖期间保持完整离开滞留时间；离开后的 Update 才消耗该时间。维护续租在帧尾进行，不会因 source/child 更新顺序重复扣时；新进入者由 pulse 添加，下一 Update 开始计龄。

配置动作有递归/执行预算和周期补偿上限。清理、销毁和业务回调均可能重入，业务必须遵守战斗作用域和主线程调用约定。自定义 Modifier 的状态/属性每次查询都执行，不缓存动态结果；只对明确的内置静态读取避免不必要对象分配。

AutoCast、独立层到期、投射物视野尚未实现，相关配置明确拒绝；碰撞/物品/负再生等声明的脚本责任由项目扩展实现。这不是完整 Dota 2 所有版本规则的复刻。

## 验证材料

仓库 `Data/AbilityJsonSources/Validation` 提供多种真实JSON技能、跨来源引用和外部脚本样例。`Assets/Tests/EditMode/Ability` 测试从这些文件加载并检查实际结算。它们是业务和测试资料，不是包运行依赖。


`Tools/AbilityVerification/PackageSmoke/run.sh` 只把本目录复制到空临时项目，以另一个程序集自建全部接口和脚本，验证无需项目源码也可使用包。Core/Business 离线检查和性能基准在同一验证目录。

离线测试使用 Unity/NUnit 兼容层；正式发布仍需要 Unity 2022.3 EditMode/PlayMode、IL2CPP 和目标设备 Profiler 验证。

编辑器使用见[编辑器指南](Documentation~/Editor/README.md)。测试夹具位于仓库 tools/AbilityVerification/Fixtures，不随运行时发布。

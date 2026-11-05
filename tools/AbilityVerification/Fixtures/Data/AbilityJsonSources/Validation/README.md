# 可执行技能样例

这些文件是业务侧配置样例，不属于技能运行时包。`Assets/Tests/EditMode/Ability/Scenarios` 的测试直接读取这些文件，经过严格 JSON 解析、统一注册表校验和发布，再驱动真实 `AbilitySystem`；测试没有维护另一份字符串版技能配置。

| 配置 | 技能 | 验证行为 |
| --- | --- | --- |
| `composed_skills.json` | Arc Bolt、Ice Bolt | 施法只生成追踪弹；到达后才伤害；同一施法者拥有多个弹道技能时不串技能；冰弹附带减速；目标抵达时重新校验 |
| 同上 | Piercing Wave | 直线穿透、长帧扫掠碰撞、每个敌人只命中一次、排除友军和轴外目标 |
| 同上 | Frost Nova | 前摇、点选范围、伤害与限时减速、友军和魔免过滤 |
| 同上 | Venom | 持续三秒，每秒伤害一次；到期时最后一次跳伤；刷新同一实例和持续时间，保留跳伤相位；驱散后停止 |
| 同上 | Mend | 友军治疗、满血上限、事件报告实际有效治疗量 |
| 同上 | Spell Leech | 根据已提交的实际伤害吸血；过量伤害不放大吸血；排除其他来源、反射伤害和生命移除 |
| 同上 | Retaliation | 反射实际伤害的 20%；标记 `Reflected` 并过滤已反射伤害；双方都有反伤时只反射一次，无须触发执行熔断 |
| 同上 | Guardian Aura | 友军进入/离开光环、护甲和移速属性、周期维护不叠层、移除来源后的效果及表现清理 |
| `mixed_sources.json` | Shared Root、Shared Guard | JSON 引用公共 Excel/代码 modifier；代码技能引用公共 JSON modifier |
| `external_chain_lightning.json` | Chain Lightning | 业务程序集注册自定义 `AbilityScript`；JSON 提供伤害、跳数、范围、衰减；按距离和实体 ID 确定跳跃目标；每个目标最多一次 |

`Spell Leech` 是对普通攻击和技能伤害均生效的示范吸血规则，不声称等同于 Dota 2 的某件攻击吸血道具。`Retaliation` 的伤害来源是反伤持有者，伤害类型为纯粹伤害。

## 配置和扩展边界

伤害、治疗、modifier、追踪/直线弹道、光环等通用机制由包内置。能用这些机制组合的具体技能放在业务 JSON；需要选择链式目标等特殊逻辑时，在业务自己的程序集继承 `AbilityScript` 或 `ModifierScript`，注册工厂后发布配置，无须修改技能包。

`onProjectileHit` 属于发射弹道的技能。`actions` 内的生成弹道动作不会立即执行命中伤害，命中动作可以使用 `PrimaryTarget` 或 `EventTarget`。命中本身没有已提交伤害/治疗数值，因此不允许使用 `EventDamage`、`EventHealing` 等事件数值源。已发射的弹道可以在技能被移除后继续命中；弹道自身被移除或战斗清理后停止执行剩余动作。

`triggerExcludedDamageFlags` 是 modifier 的可选伤害事件过滤位掩码，默认不排除。示例使用 `16` 过滤 `DamageFlags.Reflected`；反伤动作使用 `17`，即 `Reflected | NoSpellAmplification`。过滤只影响该 modifier，不会吞掉其他观察者应收到的伤害事件。

JSON 的枚举使用当前公共枚举整数值。本组样例用到 `behavior`：`2` 被动、`8` 单位目标、`16` 点目标、`48` 点目标 + AOE；`actionType`：`1` 伤害、`2` 治疗、`3` 添加 modifier、`5` 追踪弹、`6` 直线弹。编写正式编辑器时应展示枚举名称并输出数值，避免作者手写魔法数字。

`mixed_sources.json` 本身依赖外部公共定义，不能单独发布。测试通过 `IAbilityDefinitionProvider` 传入代表 Excel 和代码产物的定义；它验证合并后的引用和执行，不验证 `.xlsx` 解析或 Luban 导出流程。真正项目接入时提供业务自己的 Excel provider 即可。

## 运行验证

Unity 中使用 EditMode Test Runner 运行 `JsonSkillScenarioTests` 和 `ProjectileActionContractTests`。测试需要完整仓库中的 `Data/AbilityJsonSources/Validation`；在独立空项目中只导入运行时包时，这些仓库场景测试不会随包导入。

仓库的便携验证入口：

```sh
dotnet run --project Tools/AbilityVerification/Core/AbilityTests.csproj -- JsonSkillScenarioTests
dotnet run --project Tools/AbilityVerification/Core/AbilityTests.csproj -- ProjectileActionContractTests
```

便携宿主使用 Unity 数学和 NUnit 兼容替身，覆盖配置到运行时的规则；它不替代 Unity 场景、物理、真实业务适配器、IL2CPP 或目标设备性能测试。

# 英雄技能 JSON 参考包

这组配置实现 7 位英雄的 14 个代表技能，用于验证同一套技能系统能承载不同玩法。它们是独立的业务数据，不是完整英雄的四技能套装，也没有把英雄代码加入技能包。所有文件只使用内置动作和 Modifier，不需要自定义脚本工厂。

最低运行时版本为 **Ability Runtime 0.2.0**，使用新增的 `levelProperties`。数值选用经典机制与演示预设，没有绑定某一版 Dota 2 补丁；不包含天赋、命石、魔晶、神杖或 Warcraft III 完整护甲/魔抗系统。距离、弹速和单位属性应按项目坐标与战斗模型调平，不能把两个游戏的预设直接视作平衡好的混战规则。

## 技能与覆盖

| 来源 | 英雄/文件 | 已实现技能 | 执行机制 |
| --- | --- | --- | --- |
| Dota 2 | [Lina](Dota2/lina.json) | 龙破斩、神灭斩 | 直线穿透弹道；单体高额魔法伤害 |
| Dota 2 | [Crystal Maiden](Dota2/crystal_maiden.json) | 冰霜新星、冰封禁制 | 点选范围伤害与双减速；英雄目标定身/禁攻及周期伤害 |
| Dota 2 | [Lion](Dota2/lion.json) | 裂地尖刺、妖术 | 穿透弹道伤害/眩晕；单体变形控制状态 |
| Dota 2 | [Sven](Dota2/sven.json) | 战吼 | 自身及范围友军限时护甲/移动加成 |
| Warcraft III | [山丘之王](Warcraft3/mountain_king.json) | 风暴之锤、雷霆一击 | 追踪弹命中伤害/眩晕；自身范围伤害与双减速 |
| Warcraft III | [牛头人酋长](Warcraft3/tauren_chieftain.json) | 震荡波、战争践踏、耐久光环 | 穿透弹道；自身范围伤害/眩晕；友军移速/攻速光环 |
| Warcraft III | [圣骑士](Warcraft3/paladin.json) | 专注光环、神圣护盾 | 友军护甲光环；自身限时不可普通驱散的无敌 |

每个英雄文件自带私有 Modifier/Projectile，既可单独发布，也可以全部加载并与已有 Validation 样例共存。`id` 与 `name` 当前均使用稳定、全局唯一的命名；更改显示名不影响引用。技能 JSON 只含生产 schema 字段，本 README 和编辑器规划不作为 Provider 输入。

## 与原作的边界

配置内的 `description` 是每个参考版本的具体说明。以下区别是有意限定的范围，不应被 UI 隐藏：

- 直线弹道使用固定半径，不模拟逐渐变宽、树木或投射物视野；地刺没有抛空表现。神灭斩采用经典魔法伤害版本，前摇结束即结算，没有施法后独立延时。
- 冰封禁制限定敌方英雄，使用英雄持续时间/跳伤预设；未冒充原作对普通单位的另一套规则。
- 妖术通过 Hexed 状态禁止攻击和施法，使用固定百分比减速，保留单位碰撞；没有固定绝对移动速度、模型变化和幻象处理。移速、攻速和角色移动由业务读取属性后执行。
- Warcraft III 配置没有地面/飞行筛选、英雄与普通单位不同控制时长。风暴之锤统一眩晕 3 秒；雷霆一击统一减速 5 秒；战争践踏统一使用配置的普通单位时长。震荡波未实现原作的总伤害上限。
- 神圣护盾没有主动取消命令。敌方普通选择与伤害尊重无敌；带显式忽略无敌标志的代码调用仍遵守包的通用规则。
- 攻速使用 `AttackSpeedBonus` 的加成通道，不自动换算 Warcraft III 的基础攻击间隔；护甲最终采用包与业务适配器约定的公式。
- Dota 预设与 Warcraft III 预设没有统一距离比例；正式项目应统一世界单位，再调整范围、速度、半径和寿命。

需要完整重现上述规则时，应先设计可复用的目标标签、条件动作、延迟任务、总量限制或业务扩展，而不是把“某个英雄ID”条件写进核心。没有选入本轮的召唤、概率被动、引导区域等技能也不能视为已经实现。

## 等级属性配置

```json
"levelProperties": [
  { "property": 6, "value": { "values": [1.5, 3, 4.5] } }
]
```

`6` 对应 `ModifierProperty.ArmorBonus`。原 `properties` 仍表示固定 float；同一 property 不得在固定和等级表中重复。等级属性按来源技能的当前等级读取；无来源技能时使用 `ModifierApplyOptions.Level`；超出数组沿用末项；层数只乘一次。光环子效果同样遵守该规则，升级无须等待下一次维护。已经存在的效果是否实时随等级改变是本包当前契约，不应默认等于某一原作技能的快照行为。

## 接入与验证

项目读取所需文件的文本，逐个传入 `JsonAbilityDefinitionProvider`，合并到一个 `AbilityDefinitionRegistry` 后校验和 `ApplyTo`。业务决定英雄与技能的授予关系、等级和资源，不需要修改运行时包。施法与单位注册流程见 [包 API 说明](../../../Assets/Scripts/Ability/README.md)。

在源码验证工程根目录运行：

```sh
dotnet run --project Tools/AbilityVerification/Core/AbilityTests.csproj -- DotaHeroJsonTests
dotnet run --project Tools/AbilityVerification/Core/AbilityTests.csproj -- WarcraftHeroJsonTests
dotnet run --project Tools/AbilityVerification/Core/AbilityTests.csproj -- HeroPackPublicationTests
dotnet run --project Tools/AbilityVerification/Core/AbilityTests.csproj -- ModifierLevelPropertyTests
```

测试读取这里的实际 JSON，经严格解析、发布后运行，断言各等级、目标关系、范围、魔免、弹道命中、控制、费用/冷却、光环升级与移除。全量回归还需运行 Core/Business/PackageSmoke；真实 Unity、IL2CPP 和目标设备门槛见 [验收记录](../../../Docs/AbilityValidation.md)。

## 编辑器规划与参考来源

[Odin 编辑器规划](../../../Docs/AbilityEditorOdinPlan.md) 以这些 JSON 的实际作者需求作为 MVP 验收：不手写枚举整数、等级表、动作上下文、引用、生产校验、可靠保存与语义往返。首版编辑器已实现，入口 Tools / Ability / Skill Studio；见 [使用指南](../../../Docs/AbilityEditorGuide.md)。运行时没有 Odin 依赖，真实 Unity GUI 与交互仍需验收。

机制参考采用官方英雄介绍与经典 Warcraft III 表格，下面页面不是本包特定补丁数值的一致性证明：

- Dota 2：[Lina](https://www.dota2.com/hero/lina)、[Crystal Maiden](https://www.dota2.com/hero/crystalmaiden)、[Lion](https://www.dota2.com/hero/lion)、[Sven](https://www.dota2.com/hero/sven)。官网部分正文依赖客户端脚本，不作为本配置逐字段精确数值的来源。
- Warcraft III：[Mountain King](https://classic.battle.net/war3/human/units/mountainking.shtml)、[Tauren Chieftain](https://classic.battle.net/war3/orc/units/taurenchieftain.shtml)、[Paladin](https://classic.battle.net/war3/human/units/paladin.shtml)。

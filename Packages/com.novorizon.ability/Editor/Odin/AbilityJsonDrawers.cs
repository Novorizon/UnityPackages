#if UNITY_EDITOR && ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Ability.Configuration;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Ability.Editor.Odin
{
    // Drawers stay in the optional Editor assembly. Public runtime DTOs carry no Odin attributes.
    public sealed class AbilityJsonAbilityDrawer : OdinValueDrawer<JsonAbility>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            if (AbilityFormContext.Current == null) { CallNextDrawer(label); return; }
            AbilityJsonForm.Draw(ValueEntry.SmartValue, "ability");
        }
    }

    public sealed class AbilityJsonModifierDrawer : OdinValueDrawer<JsonModifier>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            if (AbilityFormContext.Current == null) { CallNextDrawer(label); return; }
            AbilityJsonForm.Draw(ValueEntry.SmartValue, "modifier");
        }
    }

    public sealed class AbilityJsonProjectileDrawer : OdinValueDrawer<JsonProjectile>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            if (AbilityFormContext.Current == null) { CallNextDrawer(label); return; }
            AbilityJsonForm.Draw(ValueEntry.SmartValue, "projectile");
        }
    }

    public sealed class AbilityJsonDocumentDrawer : OdinValueDrawer<JsonAbilityDocument>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            if (AbilityFormContext.Current == null) { CallNextDrawer(label); return; }
            AbilityJsonForm.Draw(ValueEntry.SmartValue, "document");
        }
    }

    internal sealed class AbilityFormContext
    {
        [ThreadStatic] internal static AbilityFormContext Current;
        internal Func<string, IEnumerable<string>> References;
        private readonly Dictionary<string, bool> expanded = new Dictionary<string, bool>(StringComparer.Ordinal);

        internal bool Expanded(string key, bool fallback)
        {
            return expanded.TryGetValue(key, out bool value) ? value : fallback;
        }

        internal void SetExpanded(string key, bool value) => expanded[key] = value;
        internal void Reset() => expanded.Clear();
        internal static IDisposable Push(AbilityFormContext value) => new Scope(value);

        private sealed class Scope : IDisposable
        {
            private readonly AbilityFormContext previous;
            internal Scope(AbilityFormContext value) { previous = Current; Current = value; }
            public void Dispose() { Current = previous; }
        }
    }

    /// <summary>Schema-tree renderer: a modifier reference is always a name, never recursive content.</summary>
    internal static class AbilityJsonForm
    {
        private sealed class Section
        {
            internal string Title;
            internal string Detail;
            internal string[] Fields;
            internal bool Open;
            internal Section(string title, string detail, bool open, params string[] fields) { Title = title; Detail = detail; Open = open; Fields = fields; }
        }

        private sealed class EnumChoices
        {
            internal bool Flags;
            internal int[] Values;
            internal string[] Names;
            internal int[] Bits;
            internal string[] BitNames;
            internal int KnownBits;
        }

        private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> fields = new Dictionary<Type, Dictionary<string, FieldInfo>>();
        private static readonly Dictionary<Type, string[]> additionalFields = new Dictionary<Type, string[]>();
        private static readonly Dictionary<Type, EnumChoices> enumChoices = new Dictionary<Type, EnumChoices>();
        private static readonly Dictionary<Type, Section[]> sections = new Dictionary<Type, Section[]>
        {
            [typeof(JsonAbility)] = new[]
            {
                new Section("基础信息", "IDENTITY", true, "id", "name", "displayName", "description", "icon", "requiresCustomScript"),
                new Section("施法与目标", "CAST & TARGETING", true, "maxLevel", "behavior", "targetTeam", "targetType", "targetFlags"),
                new Section("每级参数", "LEVEL VALUES", true, "castRange", "aoeRadius", "castPoint", "castBackswing", "channelTime", "cooldown", "manaCost", "charges"),
                new Section("特殊数值", "SPECIAL VALUES", true, "specialValues"),
                new Section("施法动作", "ON SPELL START", true, "actions"),
                new Section("弹道命中", "OWN PROJECTILES ONLY", true, "onProjectileHit"),
                new Section("开关生命周期", "TOGGLE", false, "onToggleOn", "onToggleOff"),
                new Section("固有 Modifier", "INTRINSIC", true, "intrinsicModifierName")
            },
            [typeof(JsonModifier)] = new[]
            {
                new Section("基础信息", "IDENTITY", true, "id", "name", "displayName", "isPrivate", "requiresCustomScript"),
                new Section("持续与规则", "LIFETIME", true, "isHidden", "isDebuff", "isPurgable", "removeOnDeath", "duration", "interval", "maxStack", "attributes", "states", "sustainedEffectName"),
                new Section("属性加成", "STATIC & LEVEL-SCALED", true, "properties", "levelProperties"),
                new Section("光环", "AURA", false, "auraModifierName", "auraRadius", "auraDuration", "auraThinkInterval", "auraIncrementsStacks", "auraTargetTeam", "auraTargetType", "auraTargetFlags"),
                new Section("生命周期动作", "LIFECYCLE", false, "onCreated", "onRefresh", "onDestroy", "intervalActions"),
                new Section("事件触发", "EVENT REACTION", true, "triggerEventType", "triggerEventScope", "triggerExcludedDamageFlags", "triggerActions")
            },
            [typeof(JsonProjectile)] = new[]
            {
                new Section("基础信息", "IDENTITY", true, "id", "name", "isPrivate"),
                new Section("运动与碰撞", "MOTION", true, "speed", "radius", "distance", "maxLifetime", "trackingTargetPolicy", "deleteOnHit"),
                new Section("目标过滤", "TARGETING", true, "targetTeam", "targetType", "targetFlags"),
                new Section("表现与视野", "PRESENTATION", true, "effectName", "soundName", "providesVision", "visionRadius")
            },
            [typeof(JsonAbilityDocument)] = new[]
            {
                new Section("文件信息", "DOCUMENT", true, "schemaVersion", "namespace")
            }
        };

        private static readonly Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = "稳定 ID", ["name"] = "定义名称", ["displayName"] = "显示名称", ["description"] = "技能说明", ["icon"] = "图标标识",
            ["requiresCustomScript"] = "需要业务脚本", ["maxLevel"] = "最大等级", ["behavior"] = "技能行为", ["targetTeam"] = "目标关系", ["targetType"] = "单位类型", ["targetFlags"] = "目标规则",
            ["castRange"] = "施法距离", ["aoeRadius"] = "作用半径", ["castPoint"] = "施法前摇", ["castBackswing"] = "施法后摇", ["channelTime"] = "引导时间", ["cooldown"] = "冷却时间", ["manaCost"] = "法力消耗",
            ["charges"] = "充能配置", ["intrinsicModifierName"] = "固有效果", ["specialValues"] = "特殊数值", ["actions"] = "动作列表", ["onProjectileHit"] = "命中动作", ["onToggleOn"] = "开启动作", ["onToggleOff"] = "关闭动作",
            ["isPrivate"] = "私有定义", ["isHidden"] = "隐藏效果", ["isDebuff"] = "负面效果", ["isPurgable"] = "可被驱散", ["removeOnDeath"] = "死亡时移除", ["duration"] = "持续时间", ["interval"] = "周期间隔", ["maxStack"] = "层数上限", ["attributes"] = "效果特性", ["states"] = "附加状态",
            ["properties"] = "固定属性", ["levelProperties"] = "每级属性", ["onCreated"] = "创建时", ["onRefresh"] = "刷新时", ["onDestroy"] = "销毁时", ["intervalActions"] = "周期动作",
            ["triggerEventType"] = "触发事件", ["triggerEventScope"] = "事件范围", ["triggerExcludedDamageFlags"] = "排除伤害标记", ["triggerActions"] = "触发动作", ["sustainedEffectName"] = "持续表现标识",
            ["auraModifierName"] = "子效果引用", ["auraRadius"] = "光环半径", ["auraDuration"] = "离开滞留时间", ["auraThinkInterval"] = "维护间隔", ["auraIncrementsStacks"] = "维护时叠层", ["auraTargetTeam"] = "光环目标关系", ["auraTargetType"] = "光环单位类型", ["auraTargetFlags"] = "光环目标规则",
            ["speed"] = "移动速度", ["radius"] = "碰撞半径", ["distance"] = "最大距离", ["maxLifetime"] = "最大寿命", ["trackingTargetPolicy"] = "追踪失效策略", ["deleteOnHit"] = "命中后销毁", ["providesVision"] = "提供视野", ["visionRadius"] = "视野半径", ["effectName"] = "特效标识", ["soundName"] = "音效标识",
            ["actionType"] = "动作类型", ["target"] = "动作目标", ["valueSource"] = "数值来源", ["value"] = "数值", ["valueSpecialName"] = "数值 Special", ["durationSpecialName"] = "时长 Special", ["damageType"] = "伤害类型", ["damageFlags"] = "伤害标记", ["modifierName"] = "Modifier 引用", ["projectileName"] = "弹道引用",
            ["purgePositiveBuffs"] = "驱散正面效果", ["purgeDebuffs"] = "驱散负面效果", ["purgeOnlyPurgable"] = "仅驱散可驱散项", ["maxCharges"] = "充能上限", ["restoreTime"] = "单次恢复时间", ["startFull"] = "初始满充能", ["usesCooldown"] = "同时使用冷却", ["property"] = "属性通道",
            ["schemaVersion"] = "格式版本", ["namespace"] = "命名空间", ["abilities"] = "技能", ["modifiers"] = "Modifier", ["projectiles"] = "弹道"
        };

        private static readonly Dictionary<string, string> enumLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["None"] = "无", ["Hidden"] = "隐藏", ["Passive"] = "被动", ["NoTarget"] = "无目标", ["UnitTarget"] = "单位目标", ["PointTarget"] = "点目标", ["Aoe"] = "范围", ["Channelled"] = "引导", ["Toggle"] = "开关", ["Immediate"] = "立即", ["RootDisables"] = "缠绕时禁用", ["AutoCast"] = "自动施法", ["OptionalUnitTarget"] = "可选单位", ["Directional"] = "方向",
            ["Friendly"] = "友方", ["Enemy"] = "敌方", ["Both"] = "双方", ["Hero"] = "英雄", ["Basic"] = "普通单位", ["Building"] = "建筑", ["Creep"] = "小兵", ["Ward"] = "守卫", ["Other"] = "其它", ["All"] = "全部",
            ["Dead"] = "死亡单位", ["MagicImmuneEnemies"] = "魔免敌人", ["Invulnerable"] = "无敌", ["IncludeSelf"] = "包含自身", ["ExcludeSelf"] = "排除自身", ["VisibleOnly"] = "仅可见", ["IgnoreLineOfSight"] = "忽略视线", ["Untargetable"] = "不可选取",
            ["Stunned"] = "眩晕", ["Silenced"] = "沉默", ["Muted"] = "禁用物品", ["Rooted"] = "定身", ["Disarmed"] = "禁攻", ["Hexed"] = "妖术", ["MagicImmune"] = "魔免", ["OutOfGame"] = "离场", ["CommandRestricted"] = "禁止命令", ["NoUnitCollision"] = "无单位碰撞",
            ["Physical"] = "物理", ["Magical"] = "魔法", ["Pure"] = "纯粹", ["NoSpellAmplification"] = "不受技能增强", ["HpLoss"] = "生命移除", ["NonLethal"] = "非致命", ["IgnoreBlock"] = "忽略格挡", ["Reflected"] = "反射", ["NoDamageMultiplier"] = "不受伤害倍率", ["IgnoreInvulnerable"] = "忽略无敌", ["PiercesSpellImmunity"] = "穿透魔免",
            ["Permanent"] = "永久", ["Multiple"] = "允许多实例", ["Aura"] = "光环", ["NoDurationRefresh"] = "不刷新时长", ["StackIndependent"] = "独立层",
            ["MoveSpeedBonusPercent"] = "移动速度百分比", ["AttackSpeedBonus"] = "攻击速度", ["DamageOutgoingPercent"] = "造成伤害百分比", ["DamageIncomingPercent"] = "受到伤害百分比", ["SpellAmplifyPercent"] = "技能增强", ["ArmorBonus"] = "护甲", ["HealthRegen"] = "生命回复", ["CooldownReductionPercent"] = "冷却缩减", ["CastRangeBonus"] = "施法距离加成",
            ["Related"] = "相关单位", ["Global"] = "全局", ["OrderIssued"] = "命令发出", ["AbilityExecuted"] = "技能执行", ["AbilityFullyCast"] = "施法完成", ["ChannelFinished"] = "引导结束", ["DamageCalculated"] = "伤害计算", ["DamageDealt"] = "造成伤害", ["DamageTaken"] = "受到伤害", ["Healed"] = "治疗", ["AttackStart"] = "攻击开始", ["AttackLanded"] = "攻击命中", ["Death"] = "死亡", ["ProjectileHit"] = "弹道命中", ["ModifierAdded"] = "效果添加", ["ModifierRemoved"] = "效果移除", ["Respawn"] = "复活",
            ["Damage"] = "伤害", ["Heal"] = "治疗", ["AddModifier"] = "添加 Modifier", ["Purge"] = "驱散", ["CreateTrackingProjectile"] = "创建追踪弹道", ["CreateLinearProjectile"] = "创建直线弹道", ["PlayEffect"] = "播放特效", ["PlaySound"] = "播放音效",
            ["Caster"] = "施法者", ["PrimaryTarget"] = "主要目标", ["ContextTargets"] = "上下文目标组", ["Point"] = "目标点", ["Parent"] = "效果持有者", ["EventSource"] = "事件来源", ["EventTarget"] = "事件目标", ["LevelOrSpecial"] = "等级值 / Special", ["EventDamage"] = "实际伤害倍率", ["EventHealing"] = "实际治疗倍率", ["EventValue"] = "事件值倍率", ["KeepTracking"] = "继续追踪", ["RevalidateTarget"] = "命中时重验目标", ["Destroy"] = "销毁"
        };

        private static GUIStyle cardStyle;
        private static GUIStyle eyebrowStyle;
        private static GUIStyle itemStyle;
        private static bool styledDark;
        private static Color Accent => EditorGUIUtility.isProSkin ? new Color(0.27f, 0.82f, 0.73f) : new Color(0.04f, 0.48f, 0.45f);
        private static AbilityFormContext Context => AbilityFormContext.Current;

        internal static void Draw(object value, string path)
        {
            EnsureStyles();
            if (value == null)
            {
                EditorGUILayout.HelpBox("当前条目为空。请在条目列表中创建有效定义。", MessageType.Info);
                return;
            }
            if (sections.TryGetValue(value.GetType(), out Section[] groups))
            {
                foreach (Section group in groups) DrawSection(value, path, group);
                string[] additional = AdditionalFields(value.GetType(), groups);
                if (additional.Length > 0) DrawSection(value, path, new Section("扩展字段", "SCHEMA FIELDS", true, additional));
                if (value is JsonAbilityDocument document)
                {
                    EditorGUILayout.LabelField("定义概览", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField((document.abilities?.Length ?? 0) + " 个技能  ·  " + (document.modifiers?.Length ?? 0) + " 个 Modifier  ·  " + (document.projectiles?.Length ?? 0) + " 个弹道", EditorStyles.miniLabel);
                    EditorGUILayout.HelpBox("在左侧选择具体条目进行编辑。引用按名称连接，不会在此递归展开。", MessageType.Info);
                }
            }
            else DrawFields(value, path, Fields(value.GetType()).Keys);
        }

        private static void DrawSection(object owner, string path, Section group)
        {
            string key = path + "/" + group.Title;
            EditorGUILayout.BeginVertical(cardStyle);
            Rect accent = GUILayoutUtility.GetRect(0f, 2f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(accent, Accent);
            EditorGUILayout.Space(4f);
            bool open = Foldout(key, group.Title, group.Open);
            GUILayout.Label(group.Detail, eyebrowStyle);
            if (open)
            {
                EditorGUILayout.Space(7f);
                DrawFields(owner, path, group.Fields);
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(7f);
        }

        private static void DrawFields(object owner, string path, IEnumerable<string> names)
        {
            Dictionary<string, FieldInfo> map = Fields(owner.GetType());
            foreach (string name in names)
                if (map.TryGetValue(name, out FieldInfo field)) DrawField(owner, field, path + "/" + name);
        }

        private static void DrawField(object owner, FieldInfo field, string path)
        {
            GUIContent label = Label(field.Name);
            object current = field.GetValue(owner);
            object next;
            Type type = field.FieldType;
            if (type == typeof(string)) next = DrawString(field.Name, label, (string)current);
            else if (type == typeof(int)) next = DrawInteger(owner.GetType(), field.Name, label, (int)current);
            else if (type == typeof(float)) next = FiniteField(label, (float)current);
            else if (type == typeof(bool)) next = EditorGUILayout.Toggle(label, (bool)current);
            else if (type == typeof(JsonLevelValue)) next = DrawLevel(label, (JsonLevelValue)current, path, field.Name == "duration");
            else if (type.IsArray) next = DrawArray(label, (Array)current, type.GetElementType(), path);
            else next = DrawOptional(label, current, type, path);
            if (!Equals(current, next)) field.SetValue(owner, next);
        }

        private static string DrawString(string name, GUIContent label, string value)
        {
            if (name == "description")
            {
                EditorGUILayout.LabelField(label);
                EditorGUI.BeginChangeCheck();
                string edited = EditorGUILayout.TextArea(value ?? string.Empty, GUILayout.MinHeight(62f));
                return EditorGUI.EndChangeCheck() ? edited : value;
            }
            string reference = ReferenceKind(name);
            if (reference == null)
            {
                EditorGUI.BeginChangeCheck();
                string edited = EditorGUILayout.TextField(label, value ?? string.Empty);
                return EditorGUI.EndChangeCheck() ? edited : value;
            }
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string text = EditorGUILayout.TextField(label, value ?? string.Empty);
            string result = EditorGUI.EndChangeCheck() ? text : value;
            string[] options = ReferenceOptions(reference);
            if (options.Length > 0)
            {
                string[] choices = new[] { "选择引用…" }.Concat(options).ToArray();
                int selection = EditorGUILayout.Popup(0, choices, GUILayout.Width(106f));
                if (selection > 0) { result = choices[selection]; GUI.changed = true; }
            }
            EditorGUILayout.EndHorizontal();
            return result;
        }

        private static int DrawInteger(Type owner, string name, GUIContent label, int value)
        {
            Type enumeration = EnumType(owner, name);
            if (enumeration == null) return EditorGUILayout.IntField(label, value);
            EnumChoices options = Choices(enumeration);
            if (options.Flags)
            {
                int[] bits = options.Bits;
                int mask = 0;
                for (int i = 0; i < bits.Length; i++) if ((value & bits[i]) != 0) mask |= 1 << i;
                int edited = EditorGUILayout.MaskField(label, mask, options.BitNames);
                int known = options.KnownBits;
                if ((value & ~known) != 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("包含未知标记：" + (value & ~known), EditorStyles.miniLabel);
                    if (GUILayout.Button("清除未知", EditorStyles.miniButton, GUILayout.Width(70f)))
                    {
                        GUI.changed = true;
                        EditorGUILayout.EndHorizontal();
                        return value & known;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (edited == mask) return value;
                int result = value & ~known;
                for (int i = 0; i < bits.Length; i++) if ((edited & (1 << i)) != 0) result |= bits[i];
                return result;
            }
            int current = Array.IndexOf(options.Values, value);
            if (current >= 0)
            {
                int selected = EditorGUILayout.Popup(label, current, options.Names);
                return options.Values[Math.Max(0, Math.Min(selected, options.Values.Length - 1))];
            }
            int[] choices = options.Values.Concat(new[] { value }).ToArray();
            string[] names = options.Names.Concat(new[] { "未知值 " + value }).ToArray();
            int index = EditorGUILayout.Popup(label, choices.Length - 1, names);
            return choices[Math.Max(0, Math.Min(index, choices.Length - 1))];
        }

        private static JsonLevelValue DrawLevel(GUIContent label, JsonLevelValue value, string path, bool duration)
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(Mathf.Max(90f, EditorGUIUtility.labelWidth - 4f)));
            if (value == null)
            {
                GUILayout.Label(duration ? "继承 Modifier 时长" : "使用默认值", EditorStyles.miniLabel);
                if (GUILayout.Button("设置", EditorStyles.miniButton, GUILayout.Width(52f)))
                {
                    value = new JsonLevelValue();
                    GUI.changed = true;
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return value;
            }
            int mode = value.values != null && value.values.Length > 0 ? 1 : 0;
            int selected = GUILayout.Toolbar(mode, new[] { "常量", "每级" }, EditorStyles.miniButton, GUILayout.Width(116f));
            if (selected != mode)
            {
                if (selected == 1) value.values = new[] { value.baseValue };
                else { value.baseValue = value.values[0]; value.values = null; }
                GUI.changed = true;
                mode = selected;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("默认", EditorStyles.miniButton, GUILayout.Width(42f)))
            {
                GUI.changed = true;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return null;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUI.indentLevel++;
            if (mode == 0) value.baseValue = FiniteField(new GUIContent("数值"), value.baseValue);
            else
            {
                for (int i = 0; i < value.values.Length; i++) value.values[i] = FiniteField(new GUIContent("等级 " + (i + 1)), value.values[i]);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ 复制末级", EditorStyles.miniButton))
                {
                    float[] expanded = new float[value.values.Length + 1];
                    Array.Copy(value.values, expanded, value.values.Length);
                    expanded[expanded.Length - 1] = value.values[value.values.Length - 1];
                    value.values = expanded;
                    GUI.changed = true;
                }
                using (new EditorGUI.DisabledScope(value.values.Length <= 1))
                    if (GUILayout.Button("− 末级", EditorStyles.miniButton))
                    {
                        Array.Resize(ref value.values, value.values.Length - 1);
                        GUI.changed = true;
                    }
                EditorGUILayout.EndHorizontal();
            }
            if (duration) GUILayout.Label("显式 0 表示永久；未设置时继承 Modifier 定义。", EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4f);
            EditorGUILayout.EndVertical();
            return value;
        }

        private static object DrawOptional(GUIContent label, object value, Type type, string path)
        {
            EditorGUILayout.BeginVertical(itemStyle);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            bool exists = value != null;
            bool enabled = GUILayout.Toggle(exists, exists ? "已配置" : "使用默认", EditorStyles.miniButton, GUILayout.Width(78f));
            if (enabled != exists)
            {
                value = enabled ? NewValue(type) : null;
                GUI.changed = true;
            }
            EditorGUILayout.EndHorizontal();
            if (value != null)
            {
                EditorGUI.indentLevel++;
                Draw(value, path);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndVertical();
            return value;
        }

        private static Array DrawArray(GUIContent label, Array value, Type element, string path)
        {
            int count = value?.Length ?? 0;
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            bool open = Foldout(path, label.text + "  ·  " + count, count <= 3);
            if (GUILayout.Button("+ 添加", EditorStyles.miniButton, GUILayout.Width(62f)))
            {
                Array expanded = Array.CreateInstance(element, count + 1);
                if (value != null) Array.Copy(value, expanded, count);
                expanded.SetValue(NewValue(element), count);
                value = expanded;
                GUI.changed = true;
                Context?.SetExpanded(path, true);
                open = true;
                count++;
            }
            EditorGUILayout.EndHorizontal();
            if (open)
            {
                if (count == 0) GUILayout.Label("暂无条目。按顺序执行；添加后可上下调整。", EditorStyles.miniLabel);
                for (int i = 0; i < count; i++)
                {
                    object item = value.GetValue(i);
                    EditorGUILayout.BeginVertical(itemStyle);
                    EditorGUILayout.BeginHorizontal();
                    bool itemOpen = Foldout(path + "/item/" + i, ItemTitle(item, i), count <= 3);
                    bool movedUp;
                    bool movedDown;
                    using (new EditorGUI.DisabledScope(i == 0)) movedUp = GUILayout.Button("↑", EditorStyles.miniButtonLeft, GUILayout.Width(24f));
                    using (new EditorGUI.DisabledScope(i == count - 1)) movedDown = GUILayout.Button("↓", EditorStyles.miniButtonMid, GUILayout.Width(24f));
                    bool remove = GUILayout.Button("×", EditorStyles.miniButtonRight, GUILayout.Width(24f));
                    EditorGUILayout.EndHorizontal();
                    if (remove)
                    {
                        Array reduced = Array.CreateInstance(element, count - 1);
                        if (i > 0) Array.Copy(value, 0, reduced, 0, i);
                        if (i < count - 1) Array.Copy(value, i + 1, reduced, i, count - i - 1);
                        value = reduced;
                        GUI.changed = true;
                    }
                    else if (movedUp || movedDown)
                    {
                        int other = movedUp ? i - 1 : i + 1;
                        value.SetValue(value.GetValue(other), i);
                        value.SetValue(item, other);
                        GUI.changed = true;
                    }
                    else if (itemOpen)
                    {
                        if (item == null)
                        {
                            EditorGUILayout.HelpBox("空条目不会被静默忽略。请创建内容或删除此项。", MessageType.Warning);
                            if (GUILayout.Button("创建内容", EditorStyles.miniButton)) { value.SetValue(NewValue(element), i); GUI.changed = true; }
                        }
                        else if (item is JsonAction action) DrawAction(action, path + "/item/" + i);
                        else Draw(item, path + "/item/" + i);
                    }
                    EditorGUILayout.EndVertical();
                    if (remove || movedUp || movedDown) break;
                }
            }
            EditorGUILayout.EndVertical();
            return value;
        }

        private static void DrawAction(JsonAction action, string path)
        {
            DrawFields(action, path, new[] { "actionType", "target" });
            ActionType kind = (ActionType)action.actionType;
            HashSet<string> shown = new HashSet<string>(StringComparer.Ordinal) { "actionType", "target" };
            Action<string[]> show = names => { DrawFields(action, path, names); foreach (string name in names) shown.Add(name); };
            if (kind == ActionType.Damage || kind == ActionType.Heal) show(new[] { "valueSource", "valueSpecialName", "value" });
            if (kind == ActionType.Damage) show(new[] { "damageType", "damageFlags" });
            if (kind == ActionType.AddModifier) show(new[] { "modifierName", "durationSpecialName", "duration" });
            if (kind == ActionType.CreateTrackingProjectile || kind == ActionType.CreateLinearProjectile) show(new[] { "projectileName" });
            if (kind == ActionType.Purge) show(new[] { "purgePositiveBuffs", "purgeDebuffs", "purgeOnlyPurgable" });
            show(new[] { "effectName", "soundName" });
            if (Foldout(path + "/retained", "其它保留字段", false))
            {
                EditorGUILayout.HelpBox("切换动作类型不会删除原字段。这里只编辑确有需要的保留值；最终以生产校验为准。", MessageType.Info);
                DrawFields(action, path, Fields(typeof(JsonAction)).Keys.Where(name => !shown.Contains(name)));
            }
        }

        private static object NewValue(Type type)
        {
            object result = Activator.CreateInstance(type);
            if (result is JsonAction action) { action.actionType = (int)ActionType.Damage; action.value = new JsonLevelValue(); }
            if (result is JsonSpecialValue special) special.value = new JsonLevelValue();
            if (result is JsonModifierLevelProperty property) { property.property = (int)ModifierProperty.ArmorBonus; property.value = new JsonLevelValue(); }
            if (result is JsonModifierProperty constant) constant.property = (int)ModifierProperty.ArmorBonus;
            return result;
        }

        private static string ItemTitle(object item, int index)
        {
            string prefix = (index + 1).ToString("00") + "  ";
            if (item is JsonAction action) return prefix + EnumCaption(typeof(ActionType), action.actionType);
            if (item is JsonSpecialValue special) return prefix + (string.IsNullOrEmpty(special.name) ? "新特殊数值" : special.name);
            if (item is JsonModifierProperty property) return prefix + EnumCaption(typeof(ModifierProperty), property.property);
            if (item is JsonModifierLevelProperty leveled) return prefix + EnumCaption(typeof(ModifierProperty), leveled.property);
            if (item == null) return prefix + "空条目";
            Dictionary<string, FieldInfo> map = Fields(item.GetType());
            if (map.TryGetValue("displayName", out FieldInfo display) && display.GetValue(item) is string title && !string.IsNullOrEmpty(title)) return prefix + title;
            if (map.TryGetValue("name", out FieldInfo name) && name.GetValue(item) is string value && !string.IsNullOrEmpty(value)) return prefix + value;
            return prefix + "新条目";
        }

        private static Dictionary<string, FieldInfo> Fields(Type type)
        {
            if (!fields.TryGetValue(type, out Dictionary<string, FieldInfo> result))
            {
                result = type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.MetadataToken).ToDictionary(field => field.Name, field => field, StringComparer.Ordinal);
                fields.Add(type, result);
            }
            return result;
        }

        private static string[] AdditionalFields(Type type, Section[] groups)
        {
            if (!additionalFields.TryGetValue(type, out string[] result))
            {
                HashSet<string> covered = new HashSet<string>(groups.SelectMany(group => group.Fields), StringComparer.Ordinal);
                if (type == typeof(JsonAbilityDocument)) { covered.Add("abilities"); covered.Add("modifiers"); covered.Add("projectiles"); }
                result = Fields(type).Keys.Where(name => !covered.Contains(name)).ToArray();
                additionalFields.Add(type, result);
            }
            return result;
        }

        private static EnumChoices Choices(Type type)
        {
            if (!enumChoices.TryGetValue(type, out EnumChoices result))
            {
                int[] values = Enum.GetValues(type).Cast<object>().Select(Convert.ToInt32).ToArray();
                int[] bits = values.Where(value => value > 0 && (value & (value - 1)) == 0).Distinct().ToArray();
                result = new EnumChoices
                {
                    Flags = type.IsDefined(typeof(FlagsAttribute), false), Values = values, Names = values.Select(value => EnumCaption(type, value)).ToArray(),
                    Bits = bits, BitNames = bits.Select(value => EnumCaption(type, value)).ToArray(), KnownBits = bits.Aggregate(0, (all, bit) => all | bit)
                };
                enumChoices.Add(type, result);
            }
            return result;
        }

        private static bool Foldout(string path, string title, bool fallback)
        {
            bool value = Context?.Expanded(path, fallback) ?? fallback;
            bool wasChanged = GUI.changed;
            bool next = EditorGUILayout.Foldout(value, title, true);
            GUI.changed = wasChanged;
            if (next != value) Context?.SetExpanded(path, next);
            return next;
        }

        private static GUIContent Label(string name)
        {
            string text = labels.TryGetValue(name, out string label) ? label : name;
            string hint = name;
            if (name == "name") hint += " · 引用按这个名称解析，修改前请检查其它定义的引用。";
            if (name == "id") hint += " · 稳定身份；修改显示名称不应更改此值。";
            if (name == "levelProperties") hint += " · 按来源技能当前等级求值，不能与固定属性重复。";
            if (name == "triggerEventScope") hint += " · Global 会收到无关单位事件，应仅在明确需要时启用。";
            return new GUIContent(text, hint);
        }

        private static Type EnumType(Type owner, string name)
        {
            if (name == "behavior") return typeof(AbilityBehavior);
            if (name == "targetTeam" || name == "auraTargetTeam") return typeof(TargetTeam);
            if (name == "targetType" || name == "auraTargetType") return typeof(UnitType);
            if (name == "targetFlags" || name == "auraTargetFlags") return typeof(TargetFlags);
            if (name == "attributes") return typeof(ModifierAttribute);
            if (name == "states") return typeof(UnitState);
            if (name == "triggerEventType") return typeof(ModifierEventType);
            if (name == "triggerEventScope") return typeof(ModifierEventScope);
            if (name == "triggerExcludedDamageFlags" || name == "damageFlags") return typeof(DamageFlags);
            if (name == "actionType") return typeof(ActionType);
            if (name == "target") return typeof(ActionTarget);
            if (name == "valueSource") return typeof(ActionValueSource);
            if (name == "damageType") return typeof(DamageType);
            if (name == "property") return typeof(ModifierProperty);
            if (name == "trackingTargetPolicy") return typeof(TrackingTargetPolicy);
            return null;
        }

        private static string EnumCaption(Type type, int value)
        {
            string name = Enum.GetName(type, value);
            if (name == null) return "未知值 " + value;
            return enumLabels.TryGetValue(name, out string translation) ? translation + " · " + name : name;
        }

        private static string ReferenceKind(string name)
        {
            if (name == "modifierName" || name == "intrinsicModifierName" || name == "auraModifierName") return "modifier";
            if (name == "projectileName") return "projectile";
            if (name == "valueSpecialName" || name == "durationSpecialName") return "special";
            return null;
        }

        private static string[] ReferenceOptions(string kind)
        {
            IEnumerable<string> values = Context?.References?.Invoke(kind);
            return values?.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        }

        private static float FiniteField(GUIContent label, float value)
        {
            float edited = EditorGUILayout.FloatField(label, value);
            if (!float.IsNaN(edited) && !float.IsInfinity(edited)) return edited;
            EditorGUILayout.HelpBox("数值必须是有限数字；已保留上一次有效值。", MessageType.Warning);
            return value;
        }

        private static void EnsureStyles()
        {
            bool dark = EditorGUIUtility.isProSkin;
            if (cardStyle != null && styledDark == dark) return;
            styledDark = dark;
            cardStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(13, 13, 8, 12), margin = new RectOffset(0, 0, 0, 0) };
            itemStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 7, 9), margin = new RectOffset(0, 0, 3, 4) };
            eyebrowStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9, normal = { textColor = dark ? new Color(0.49f, 0.61f, 0.69f) : new Color(0.36f, 0.44f, 0.49f) } };
        }
    }
}
#endif

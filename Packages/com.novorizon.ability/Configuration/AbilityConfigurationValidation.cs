using System;
using System.Collections.Generic;

namespace Game.Ability.Configuration
{
    public enum AbilityValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    [Serializable]
    public sealed class AbilityConfigSource
    {
        public string SourceType;
        public string Path;
        public string Sheet;
        public int Row;
        public string Field;

        public override string ToString()
        {
            string location = string.IsNullOrEmpty(Path) ? SourceType : Path;
            if (!string.IsNullOrEmpty(Sheet))
            {
                location += "/" + Sheet;
            }

            if (Row > 0)
            {
                location += ":" + Row;
            }

            if (!string.IsNullOrEmpty(Field))
            {
                location += " [" + Field + "]";
            }

            return location;
        }
    }

    [Serializable]
    public sealed class AbilityValidationIssue
    {
        public AbilityValidationSeverity Severity;
        public string Code;
        public string Message;
        public string ReferenceChain;
        public AbilityConfigSource Source;
    }

    public sealed class AbilityValidationReport
    {
        private readonly List<AbilityValidationIssue> issues = new List<AbilityValidationIssue>();

        public IReadOnlyList<AbilityValidationIssue> Issues => issues;
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }
        public int InfoCount { get; private set; }
        public bool IsValid => ErrorCount == 0;

        public void Add(AbilityValidationSeverity severity, string code, string message, AbilityConfigSource source = null, string referenceChain = null)
        {
            issues.Add(new AbilityValidationIssue
            {
                Severity = severity,
                Code = code,
                Message = message,
                Source = source,
                ReferenceChain = referenceChain
            });

            switch (severity)
            {
                case AbilityValidationSeverity.Error:
                    ErrorCount++;
                    break;
                case AbilityValidationSeverity.Warning:
                    WarningCount++;
                    break;
                default:
                    InfoCount++;
                    break;
            }
        }

        public void Merge(AbilityValidationReport other)
        {
            if (other == null)
            {
                return;
            }

            for (int i = 0; i < other.Issues.Count; i++)
            {
                AbilityValidationIssue issue = other.Issues[i];
                Add(issue.Severity, issue.Code, issue.Message, issue.Source, issue.ReferenceChain);
            }
        }
    }

    public sealed class AbilityConfigCatalog
    {
        public readonly List<AbilityConfigRecord> Abilities = new List<AbilityConfigRecord>();
        public readonly List<AbilityActionConfigRecord> Actions = new List<AbilityActionConfigRecord>();
        public readonly List<AbilityModifierConfigRecord> Modifiers = new List<AbilityModifierConfigRecord>();
        public readonly List<AbilityProjectileConfigRecord> Projectiles = new List<AbilityProjectileConfigRecord>();
        public readonly List<TowerAbilityBindingRecord> TowerBindings = new List<TowerAbilityBindingRecord>();
        public readonly HashSet<int> ResourceIds = new HashSet<int>();
        public readonly HashSet<int> UsedAbilityIds = new HashSet<int>();
    }

    public sealed class AbilityConfigRecord
    {
        public int Id;
        public bool Enabled = true;
        public AbilityDefinition Definition;
        public int ActionGroupId;
        public int IntrinsicModifierId;
        public int CostResourceId;
        public int RawBehavior = -1;
        public int RawTargetTeam = -1;
        public AbilityConfigSource Source;
    }

    public sealed class AbilityActionConfigRecord
    {
        public int Id;
        public int GroupId;
        public int Order;
        public ActionDefinition Definition;
        public int ModifierId;
        public int ProjectileId;
        public int RawActionType = -1;
        public int RawTargetType = -1;
        public AbilityConfigSource Source;
    }

    public sealed class AbilityModifierConfigRecord
    {
        public int Id;
        public ModifierDefinition Definition;
        public int TriggerActionGroupId;
        public int PeriodicActionGroupId;
        public int OnCreatedActionGroupId;
        public int OnDestroyActionGroupId;
        public int RawPropertyType = -1;
        public int RawState = -1;
        public int RawTriggerEventType = -1;
        public string EffectName;
        public AbilityConfigSource Source;
    }

    public sealed class AbilityProjectileConfigRecord
    {
        public int Id;
        public ProjectileDefinition Definition;
        public AbilityConfigSource Source;
    }

    public sealed class TowerAbilityBindingRecord
    {
        public int TowerId;
        public int Level;
        public int AbilityId;
        public float AttackInterval;
        public AbilityConfigSource Source;
    }

    public sealed class AbilityValidationOptions
    {
        /// <summary>Called only for path-like resource names such as Assets/... .</summary>
        public Func<string, bool> AssetExists;
    }

    /// <summary>
    /// Source-neutral semantic validation shared by Excel and future JSON providers.
    /// Providers preserve their source locations in records so every issue can be traced back.
    /// </summary>
    public static class AbilityConfigurationValidator
    {
        public static AbilityValidationReport Validate(AbilityConfigCatalog catalog, AbilityValidationOptions options = null)
        {
            AbilityValidationReport report = new AbilityValidationReport();
            if (catalog == null)
            {
                report.Add(AbilityValidationSeverity.Error, "ABILITY000", "Ability configuration catalog is null.");
                return report;
            }

            Dictionary<int, AbilityConfigRecord> abilities = IndexAbilities(catalog.Abilities, report);
            Dictionary<int, AbilityModifierConfigRecord> modifiers = IndexModifiers(catalog.Modifiers, report);
            Dictionary<int, AbilityProjectileConfigRecord> projectiles = IndexProjectiles(catalog.Projectiles, report);
            Dictionary<int, List<AbilityActionConfigRecord>> actionGroups = IndexActions(catalog.Actions, report);

            ValidateAbilityNames(catalog.Abilities, report);
            ValidateModifierNames(catalog.Modifiers, report);
            ValidateProjectileNames(catalog.Projectiles, report);
            ValidateAbilities(catalog, abilities, modifiers, actionGroups, report, options);
            ValidateActions(catalog.Actions, modifiers, projectiles, report, options);
            ValidateModifiers(catalog.Modifiers, actionGroups, report, options);
            ValidateProjectiles(catalog.Projectiles, report, options);
            ValidatePeriodicTargets(catalog.Modifiers, actionGroups, report);
            ValidateModifierGraph(catalog.Modifiers, actionGroups, report);
            ValidateUnusedRecords(catalog, abilities, modifiers, actionGroups, report);
            ValidateTowerBindings(catalog.TowerBindings, abilities, report);
            ValidateRepeatedEffects(actionGroups, report);
            return report;
        }

        private static Dictionary<int, AbilityConfigRecord> IndexAbilities(IReadOnlyList<AbilityConfigRecord> records, AbilityValidationReport report)
        {
            Dictionary<int, AbilityConfigRecord> result = new Dictionary<int, AbilityConfigRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                AbilityConfigRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                if (record.Id <= 0)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY001", "Ability ID must be greater than zero.", record.Source);
                    continue;
                }

                if (result.ContainsKey(record.Id))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY002", "Duplicate ability ID: " + record.Id + ".", record.Source);
                    continue;
                }

                result.Add(record.Id, record);
            }

            return result;
        }

        private static Dictionary<int, AbilityModifierConfigRecord> IndexModifiers(IReadOnlyList<AbilityModifierConfigRecord> records, AbilityValidationReport report)
        {
            Dictionary<int, AbilityModifierConfigRecord> result = new Dictionary<int, AbilityModifierConfigRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                AbilityModifierConfigRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                if (record.Id <= 0)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY003", "Modifier ID must be greater than zero.", record.Source);
                    continue;
                }

                if (result.ContainsKey(record.Id))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY004", "Duplicate modifier ID: " + record.Id + ".", record.Source);
                    continue;
                }

                result.Add(record.Id, record);
            }

            return result;
        }

        private static Dictionary<int, AbilityProjectileConfigRecord> IndexProjectiles(IReadOnlyList<AbilityProjectileConfigRecord> records, AbilityValidationReport report)
        {
            Dictionary<int, AbilityProjectileConfigRecord> result = new Dictionary<int, AbilityProjectileConfigRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                AbilityProjectileConfigRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                if (record.Id <= 0)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY005", "Projectile ID must be greater than zero.", record.Source);
                    continue;
                }

                if (result.ContainsKey(record.Id))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY006", "Duplicate projectile ID: " + record.Id + ".", record.Source);
                    continue;
                }

                result.Add(record.Id, record);
            }

            return result;
        }

        private static Dictionary<int, List<AbilityActionConfigRecord>> IndexActions(IReadOnlyList<AbilityActionConfigRecord> records, AbilityValidationReport report)
        {
            Dictionary<int, List<AbilityActionConfigRecord>> groups = new Dictionary<int, List<AbilityActionConfigRecord>>();
            HashSet<int> ids = new HashSet<int>();
            HashSet<string> orders = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < records.Count; i++)
            {
                AbilityActionConfigRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                if (record.Id <= 0 || !ids.Add(record.Id))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY007", "Action ID is invalid or duplicated: " + record.Id + ".", record.Source);
                }

                if (record.GroupId <= 0)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY008", "Action group ID must be greater than zero.", record.Source);
                    continue;
                }

                string orderKey = record.GroupId + ":" + record.Order;
                if (!orders.Add(orderKey))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY009", "Duplicate action order " + record.Order + " in group " + record.GroupId + ".", record.Source);
                }

                if (!groups.TryGetValue(record.GroupId, out List<AbilityActionConfigRecord> group))
                {
                    group = new List<AbilityActionConfigRecord>();
                    groups.Add(record.GroupId, group);
                }

                group.Add(record);
            }

            return groups;
        }

        private static void ValidateAbilityNames(IReadOnlyList<AbilityConfigRecord> records, AbilityValidationReport report)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < records.Count; i++)
            {
                AbilityConfigRecord record = records[i];
                string name = record?.Definition?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY010", "Ability internal name is empty.", record?.Source);
                }
                else if (!names.Add(name.Trim()))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY011", "Duplicate ability internal name: " + name + ".", record.Source);
                }
            }
        }

        private static void ValidateModifierNames(IReadOnlyList<AbilityModifierConfigRecord> records, AbilityValidationReport report)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < records.Count; i++)
            {
                AbilityModifierConfigRecord record = records[i];
                string name = record?.Definition?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY012", "Modifier internal name is empty.", record?.Source);
                }
                else if (!names.Add(name.Trim()))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY013", "Duplicate modifier internal name: " + name + ".", record.Source);
                }
            }
        }

        private static void ValidateProjectileNames(IReadOnlyList<AbilityProjectileConfigRecord> records, AbilityValidationReport report)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < records.Count; i++)
            {
                AbilityProjectileConfigRecord record = records[i];
                string name = record?.Definition?.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY014", "Projectile internal name is empty.", record?.Source);
                }
                else if (!names.Add(name.Trim()))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY015", "Duplicate projectile internal name: " + name + ".", record.Source);
                }
            }
        }

        private static void ValidateAbilities(
            AbilityConfigCatalog catalog,
            IReadOnlyDictionary<int, AbilityConfigRecord> abilities,
            IReadOnlyDictionary<int, AbilityModifierConfigRecord> modifiers,
            IReadOnlyDictionary<int, List<AbilityActionConfigRecord>> actionGroups,
            AbilityValidationReport report,
            AbilityValidationOptions options)
        {
            for (int i = 0; i < catalog.Abilities.Count; i++)
            {
                AbilityConfigRecord record = catalog.Abilities[i];
                AbilityDefinition definition = record?.Definition;
                if (record == null || definition == null)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY016", "Ability definition is missing.", record?.Source);
                    continue;
                }

                if (record.ActionGroupId > 0 && !actionGroups.ContainsKey(record.ActionGroupId))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY017", "Ability " + record.Id + " references missing action group " + record.ActionGroupId + ".", record.Source, record.Id + " -> actionGroup:" + record.ActionGroupId);
                }

                if (record.IntrinsicModifierId > 0 && !modifiers.ContainsKey(record.IntrinsicModifierId))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY018", "Ability " + record.Id + " references missing intrinsic modifier " + record.IntrinsicModifierId + ".", record.Source, record.Id + " -> modifier:" + record.IntrinsicModifierId);
                }

                if ((definition.Behavior & AbilityBehavior.Passive) != 0 && record.IntrinsicModifierId <= 0)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY019", "Passive ability " + record.Id + " has no intrinsic modifier.", record.Source);
                }

                if ((definition.Behavior & AbilityBehavior.UnitTarget) != 0)
                {
                    if (definition.TargetTeam == TargetTeam.None || definition.TargetType == UnitType.None)
                    {
                        report.Add(AbilityValidationSeverity.Error, "ABILITY020", "Unit-target ability " + record.Id + " has no legal target team/type.", record.Source);
                    }

                    if (definition.CastRange == null || definition.CastRange.GetValue(1) <= 0f)
                    {
                        report.Add(AbilityValidationSeverity.Warning, "ABILITY021", "Unit-target ability " + record.Id + " has a non-positive cast range.", record.Source);
                    }

                    if (record.ActionGroupId > 0 && actionGroups.TryGetValue(record.ActionGroupId, out List<AbilityActionConfigRecord> unitActions))
                    {
                        bool hasLegalUnitAction = false;
                        for (int actionIndex = 0; actionIndex < unitActions.Count; actionIndex++)
                        {
                            ActionTarget target = unitActions[actionIndex]?.Definition != null
                                ? unitActions[actionIndex].Definition.Target
                                : ActionTarget.Point;
                            if (target == ActionTarget.PrimaryTarget || target == ActionTarget.ContextTargets)
                            {
                                hasLegalUnitAction = true;
                                break;
                            }
                        }

                        if (!hasLegalUnitAction)
                        {
                            report.Add(AbilityValidationSeverity.Warning, "ABILITY048", "Unit-target ability " + record.Id + " has no action targeting the selected unit/context.", record.Source);
                        }
                    }
                }

                if (record.Enabled && (definition.Behavior & AbilityBehavior.Passive) == 0 && record.ActionGroupId <= 0)
                {
                    report.Add(AbilityValidationSeverity.Warning, "ABILITY049", "Enabled active ability " + record.Id + " has no configured action group; it requires an explicit C# script to do anything.", record.Source);
                }

                if (record.RawBehavior >= 0 && (record.RawBehavior == 0 || (record.RawBehavior & ~127) != 0))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY050", "Ability " + record.Id + " uses unsupported legacy behavior bits: " + record.RawBehavior + ".", record.Source);
                }

                if (record.RawTargetTeam >= 0 && (record.RawTargetTeam < 1 || record.RawTargetTeam > 3))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY051", "Ability " + record.Id + " uses unsupported targetTeam value " + record.RawTargetTeam + ".", record.Source);
                }

                if (record.CostResourceId > 0 && !catalog.ResourceIds.Contains(record.CostResourceId))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY022", "Ability " + record.Id + " references missing resource " + record.CostResourceId + ".", record.Source, record.Id + " -> resource:" + record.CostResourceId);
                }

                ValidateAsset(definition.Icon, "ability icon", record.Source, report, options);
            }
        }

        private static void ValidateActions(
            IReadOnlyList<AbilityActionConfigRecord> actions,
            IReadOnlyDictionary<int, AbilityModifierConfigRecord> modifiers,
            IReadOnlyDictionary<int, AbilityProjectileConfigRecord> projectiles,
            AbilityValidationReport report,
            AbilityValidationOptions options)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                AbilityActionConfigRecord record = actions[i];
                ActionDefinition definition = record?.Definition;
                if (record == null || definition == null)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY023", "Action definition is missing.", record?.Source);
                    continue;
                }

                if (definition.ActionType == ActionType.None)
                {
                    report.Add(AbilityValidationSeverity.Warning, "ABILITY024", "Action " + record.Id + " has no supported runtime action.", record.Source);
                }

                if (definition.ActionType == ActionType.AddModifier)
                {
                    if (record.ModifierId <= 0 || !modifiers.ContainsKey(record.ModifierId))
                    {
                        report.Add(AbilityValidationSeverity.Error, "ABILITY025", "AddModifier action " + record.Id + " references missing modifier " + record.ModifierId + ".", record.Source, "action:" + record.Id + " -> modifier:" + record.ModifierId);
                    }
                }
                else if (record.ModifierId > 0)
                {
                    report.Add(AbilityValidationSeverity.Warning, "ABILITY026", "Action " + record.Id + " declares a modifier but its action type does not use it.", record.Source);
                }

                bool projectileAction = definition.ActionType == ActionType.CreateTrackingProjectile || definition.ActionType == ActionType.CreateLinearProjectile;
                if (projectileAction && (record.ProjectileId <= 0 || !projectiles.ContainsKey(record.ProjectileId)))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY027", "Projectile action " + record.Id + " references missing projectile " + record.ProjectileId + ".", record.Source, "action:" + record.Id + " -> projectile:" + record.ProjectileId);
                }

                ValidateAsset(definition.EffectName, "action effect", record.Source, report, options);
                ValidateAsset(definition.SoundName, "action sound", record.Source, report, options);

                if (record.RawActionType >= 0 && record.RawActionType > 4)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY052", "Action " + record.Id + " uses unsupported action type " + record.RawActionType + ".", record.Source);
                }

                if (record.RawTargetType >= 0 && (record.RawTargetType < 1 || record.RawTargetType > 5))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY053", "Action " + record.Id + " uses unsupported target type " + record.RawTargetType + ".", record.Source);
                }
            }
        }

        private static void ValidateModifiers(
            IReadOnlyList<AbilityModifierConfigRecord> modifiers,
            IReadOnlyDictionary<int, List<AbilityActionConfigRecord>> actionGroups,
            AbilityValidationReport report,
            AbilityValidationOptions options)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                AbilityModifierConfigRecord record = modifiers[i];
                ModifierDefinition definition = record?.Definition;
                if (record == null || definition == null)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY028", "Modifier definition is missing.", record?.Source);
                    continue;
                }

                ValidateGroupReference(record.Id, "trigger", record.TriggerActionGroupId, actionGroups, record.Source, report);
                ValidateGroupReference(record.Id, "periodic", record.PeriodicActionGroupId, actionGroups, record.Source, report);
                ValidateGroupReference(record.Id, "on-created", record.OnCreatedActionGroupId, actionGroups, record.Source, report);
                ValidateGroupReference(record.Id, "on-destroy", record.OnDestroyActionGroupId, actionGroups, record.Source, report);

                if (definition.Interval > 0f && record.PeriodicActionGroupId <= 0)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY029", "Periodic modifier " + record.Id + " has an interval but no periodic action group.", record.Source);
                }
                else if (record.PeriodicActionGroupId > 0 && definition.Interval <= 0f)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY030", "Modifier " + record.Id + " references periodic actions but has no positive interval.", record.Source);
                }

                if (definition.TriggerEventType != ModifierEventType.None && record.TriggerActionGroupId <= 0)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY031", "Modifier " + record.Id + " declares a trigger event but no trigger action group.", record.Source);
                }
                else if (record.TriggerActionGroupId > 0 && definition.TriggerEventType == ModifierEventType.None)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY032", "Modifier " + record.Id + " has trigger actions but no trigger event.", record.Source);
                }

                if (record.RawPropertyType >= 0 && record.RawPropertyType > 6)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY033", "Modifier " + record.Id + " uses unsupported property type " + record.RawPropertyType + ".", record.Source);
                }

                if (record.RawState >= 0 && record.RawState > 4)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY034", "Modifier " + record.Id + " uses unsupported state type " + record.RawState + ".", record.Source);
                }

                if (record.RawTriggerEventType >= 0 && record.RawTriggerEventType > 6)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY035", "Modifier " + record.Id + " uses unsupported trigger event type " + record.RawTriggerEventType + ".", record.Source);
                }

                ValidateAsset(record.EffectName, "modifier sustained effect", record.Source, report, options);
            }
        }

        private static void ValidateProjectiles(IReadOnlyList<AbilityProjectileConfigRecord> projectiles, AbilityValidationReport report, AbilityValidationOptions options)
        {
            for (int i = 0; i < projectiles.Count; i++)
            {
                AbilityProjectileConfigRecord record = projectiles[i];
                ProjectileDefinition definition = record?.Definition;
                if (record == null || definition == null)
                {
                    continue;
                }

                if (definition.Speed < 0f || definition.Radius < 0f || definition.Distance < 0f)
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY036", "Projectile " + record.Id + " has a negative speed, radius, or distance.", record.Source);
                }

                ValidateAsset(definition.EffectName, "projectile effect", record.Source, report, options);
                ValidateAsset(definition.SoundName, "projectile sound", record.Source, report, options);
            }
        }

        private static void ValidatePeriodicTargets(
            IReadOnlyList<AbilityModifierConfigRecord> modifiers,
            IReadOnlyDictionary<int, List<AbilityActionConfigRecord>> actionGroups,
            AbilityValidationReport report)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                AbilityModifierConfigRecord modifier = modifiers[i];
                if (modifier == null || modifier.PeriodicActionGroupId <= 0 || !actionGroups.TryGetValue(modifier.PeriodicActionGroupId, out List<AbilityActionConfigRecord> actions))
                {
                    continue;
                }

                for (int j = 0; j < actions.Count; j++)
                {
                    AbilityActionConfigRecord action = actions[j];
                    ActionType type = action.Definition != null ? action.Definition.ActionType : ActionType.None;
                    bool mutatesUnit = type == ActionType.Damage || type == ActionType.Heal || type == ActionType.AddModifier || type == ActionType.Purge;
                    if (mutatesUnit && action.Definition.Target != ActionTarget.PrimaryTarget)
                    {
                        report.Add(
                            AbilityValidationSeverity.Error,
                            "ABILITY037",
                            "Periodic modifier action " + action.Id + " must target Modifier Parent (PrimaryTarget), not " + action.Definition.Target + ".",
                            action.Source,
                            "modifier:" + modifier.Id + " -> actionGroup:" + modifier.PeriodicActionGroupId + " -> action:" + action.Id);
                    }
                }
            }
        }

        private static void ValidateModifierGraph(
            IReadOnlyList<AbilityModifierConfigRecord> modifiers,
            IReadOnlyDictionary<int, List<AbilityActionConfigRecord>> actionGroups,
            AbilityValidationReport report)
        {
            Dictionary<int, AbilityModifierConfigRecord> byId = new Dictionary<int, AbilityModifierConfigRecord>();
            Dictionary<int, HashSet<int>> edges = new Dictionary<int, HashSet<int>>();
            for (int i = 0; i < modifiers.Count; i++)
            {
                AbilityModifierConfigRecord modifier = modifiers[i];
                if (modifier == null || modifier.Id <= 0 || byId.ContainsKey(modifier.Id))
                {
                    continue;
                }

                byId.Add(modifier.Id, modifier);
                HashSet<int> targets = new HashSet<int>();
                AddModifierEdges(modifier.TriggerActionGroupId, actionGroups, targets);
                AddModifierEdges(modifier.PeriodicActionGroupId, actionGroups, targets);
                AddModifierEdges(modifier.OnCreatedActionGroupId, actionGroups, targets);
                AddModifierEdges(modifier.OnDestroyActionGroupId, actionGroups, targets);
                edges.Add(modifier.Id, targets);

                if (targets.Contains(modifier.Id))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY038", "Modifier " + modifier.Id + " directly adds itself.", modifier.Source, "modifier:" + modifier.Id + " -> modifier:" + modifier.Id);
                }
            }

            HashSet<int> visited = new HashSet<int>();
            HashSet<int> active = new HashSet<int>();
            List<int> stack = new List<int>();
            Stack<ModifierGraphVisit> visits = new Stack<ModifierGraphVisit>();
            HashSet<string> reportedCycles = new HashSet<string>(StringComparer.Ordinal);
            foreach (int id in byId.Keys)
            {
                VisitModifier(id, byId, edges, visited, active, stack, visits, reportedCycles, report);
            }
        }

        private static void VisitModifier(
            int id,
            IReadOnlyDictionary<int, AbilityModifierConfigRecord> modifiers,
            IReadOnlyDictionary<int, HashSet<int>> edges,
            ISet<int> visited,
            ISet<int> active,
            IList<int> stack,
            Stack<ModifierGraphVisit> visits,
            ISet<string> reportedCycles,
            AbilityValidationReport report)
        {
            if (visited.Contains(id))
            {
                return;
            }

            active.Add(id);
            stack.Add(id);
            edges.TryGetValue(id, out HashSet<int> rootTargets);
            visits.Push(new ModifierGraphVisit(id, rootTargets));
            while (visits.Count > 0)
            {
                ModifierGraphVisit visit = visits.Pop();
                if (!visit.TryNext(out int target))
                {
                    stack.RemoveAt(stack.Count - 1);
                    active.Remove(visit.Id);
                    visited.Add(visit.Id);
                    continue;
                }
                visits.Push(visit);
                if (!modifiers.ContainsKey(target)) continue;
                if (active.Contains(target))
                {
                    int start = stack.IndexOf(target);
                    List<int> cycle = new List<int>();
                    for (int i = Math.Max(0, start); i < stack.Count; i++) cycle.Add(stack[i]);
                    cycle.Add(target);
                    string chain = string.Join(" -> modifier:", cycle);
                    if (reportedCycles.Add(chain))
                    {
                        report.Add(AbilityValidationSeverity.Error, "ABILITY039", "Circular modifier reference detected.", modifiers[visit.Id].Source, "modifier:" + chain);
                    }
                    continue;
                }
                if (visited.Contains(target)) continue;
                active.Add(target);
                stack.Add(target);
                edges.TryGetValue(target, out HashSet<int> targets);
                visits.Push(new ModifierGraphVisit(target, targets));
            }
        }

        private struct ModifierGraphVisit
        {
            public readonly int Id;
            private HashSet<int>.Enumerator targets;
            private readonly bool hasTargets;
            public ModifierGraphVisit(int id, HashSet<int> targets) { Id = id; hasTargets = targets != null; this.targets = targets != null ? targets.GetEnumerator() : default; }
            public bool TryNext(out int target)
            {
                if (hasTargets && targets.MoveNext()) { target = targets.Current; return true; }
                target = 0;
                return false;
            }
        }

        private static void ValidateUnusedRecords(
            AbilityConfigCatalog catalog,
            IReadOnlyDictionary<int, AbilityConfigRecord> abilities,
            IReadOnlyDictionary<int, AbilityModifierConfigRecord> modifiers,
            IReadOnlyDictionary<int, List<AbilityActionConfigRecord>> actionGroups,
            AbilityValidationReport report)
        {
            HashSet<int> usedAbilities = new HashSet<int>(catalog.UsedAbilityIds);
            for (int i = 0; i < catalog.TowerBindings.Count; i++)
            {
                if (catalog.TowerBindings[i] != null && catalog.TowerBindings[i].AbilityId > 0)
                {
                    usedAbilities.Add(catalog.TowerBindings[i].AbilityId);
                }
            }

            foreach (KeyValuePair<int, AbilityConfigRecord> pair in abilities)
            {
                if (pair.Value.Enabled && !usedAbilities.Contains(pair.Key))
                {
                    report.Add(AbilityValidationSeverity.Info, "ABILITY040", "Ability " + pair.Key + " has no configuration binding; it may still be invoked explicitly by code or UI.", pair.Value.Source);
                }
            }

            HashSet<int> usedModifiers = new HashSet<int>();
            HashSet<int> usedGroups = new HashSet<int>();
            for (int i = 0; i < catalog.Abilities.Count; i++)
            {
                AbilityConfigRecord ability = catalog.Abilities[i];
                if (ability == null)
                {
                    continue;
                }
                if (ability.IntrinsicModifierId > 0) usedModifiers.Add(ability.IntrinsicModifierId);
                if (ability.ActionGroupId > 0) usedGroups.Add(ability.ActionGroupId);
            }

            for (int i = 0; i < catalog.Actions.Count; i++)
            {
                if (catalog.Actions[i] != null && catalog.Actions[i].ModifierId > 0)
                {
                    usedModifiers.Add(catalog.Actions[i].ModifierId);
                }
            }

            for (int i = 0; i < catalog.Modifiers.Count; i++)
            {
                AbilityModifierConfigRecord modifier = catalog.Modifiers[i];
                if (modifier == null) continue;
                AddPositive(usedGroups, modifier.TriggerActionGroupId);
                AddPositive(usedGroups, modifier.PeriodicActionGroupId);
                AddPositive(usedGroups, modifier.OnCreatedActionGroupId);
                AddPositive(usedGroups, modifier.OnDestroyActionGroupId);
            }

            foreach (KeyValuePair<int, AbilityModifierConfigRecord> pair in modifiers)
            {
                if (!usedModifiers.Contains(pair.Key))
                {
                    report.Add(AbilityValidationSeverity.Info, "ABILITY041", "Modifier " + pair.Key + " is not referenced by any ability or action.", pair.Value.Source);
                }
            }

            foreach (KeyValuePair<int, List<AbilityActionConfigRecord>> pair in actionGroups)
            {
                if (!usedGroups.Contains(pair.Key) && pair.Value.Count > 0)
                {
                    report.Add(AbilityValidationSeverity.Info, "ABILITY042", "Action group " + pair.Key + " is not referenced.", pair.Value[0].Source);
                }
            }
        }

        private static void ValidateTowerBindings(IReadOnlyList<TowerAbilityBindingRecord> bindings, IReadOnlyDictionary<int, AbilityConfigRecord> abilities, AbilityValidationReport report)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                TowerAbilityBindingRecord binding = bindings[i];
                if (binding == null || binding.AbilityId <= 0)
                {
                    continue;
                }

                if (!abilities.TryGetValue(binding.AbilityId, out AbilityConfigRecord ability))
                {
                    report.Add(AbilityValidationSeverity.Error, "ABILITY043", "Tower " + binding.TowerId + " level " + binding.Level + " references missing ability " + binding.AbilityId + ".", binding.Source);
                    continue;
                }

                float cooldown = ability.Definition?.Cooldown != null ? ability.Definition.Cooldown.GetValue(binding.Level) : 0f;
                if (binding.AttackInterval > 0f && cooldown > binding.AttackInterval + 0.0001f)
                {
                    report.Add(
                        AbilityValidationSeverity.Warning,
                        "ABILITY044",
                        "Tower attack interval " + binding.AttackInterval + "s is shorter than ability cooldown " + cooldown + "s; the extra skill behavior will not run on every attack.",
                        binding.Source,
                        "tower:" + binding.TowerId + "/level:" + binding.Level + " -> ability:" + binding.AbilityId);
                }
            }
        }

        private static void ValidateRepeatedEffects(IReadOnlyDictionary<int, List<AbilityActionConfigRecord>> actionGroups, AbilityValidationReport report)
        {
            foreach (KeyValuePair<int, List<AbilityActionConfigRecord>> pair in actionGroups)
            {
                Dictionary<string, AbilityActionConfigRecord> seen = new Dictionary<string, AbilityActionConfigRecord>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    AbilityActionConfigRecord action = pair.Value[i];
                    string effect = action?.Definition?.EffectName;
                    if (string.IsNullOrWhiteSpace(effect))
                    {
                        continue;
                    }

                    string key = effect.Trim() + "|" + action.Definition.Target;
                    if (seen.TryGetValue(key, out AbilityActionConfigRecord first))
                    {
                        report.Add(
                            AbilityValidationSeverity.Warning,
                            "ABILITY045",
                            "Action group " + pair.Key + " plays the same effect on the same target more than once (actions " + first.Id + " and " + action.Id + ").",
                            action.Source,
                            "action:" + first.Id + " -> action:" + action.Id);
                    }
                    else
                    {
                        seen.Add(key, action);
                    }
                }
            }
        }

        private static void ValidateGroupReference(
            int modifierId,
            string purpose,
            int groupId,
            IReadOnlyDictionary<int, List<AbilityActionConfigRecord>> actionGroups,
            AbilityConfigSource source,
            AbilityValidationReport report)
        {
            if (groupId > 0 && !actionGroups.ContainsKey(groupId))
            {
                report.Add(AbilityValidationSeverity.Error, "ABILITY046", "Modifier " + modifierId + " references missing " + purpose + " action group " + groupId + ".", source, "modifier:" + modifierId + " -> actionGroup:" + groupId);
            }
        }

        private static void AddModifierEdges(int groupId, IReadOnlyDictionary<int, List<AbilityActionConfigRecord>> actionGroups, ISet<int> targets)
        {
            if (groupId <= 0 || !actionGroups.TryGetValue(groupId, out List<AbilityActionConfigRecord> actions))
            {
                return;
            }

            for (int i = 0; i < actions.Count; i++)
            {
                AbilityActionConfigRecord action = actions[i];
                if (action != null && action.Definition != null && action.Definition.ActionType == ActionType.AddModifier && action.ModifierId > 0)
                {
                    targets.Add(action.ModifierId);
                }
            }
        }

        private static void ValidateAsset(string asset, string purpose, AbilityConfigSource source, AbilityValidationReport report, AbilityValidationOptions options)
        {
            if (string.IsNullOrWhiteSpace(asset) || options?.AssetExists == null || !LooksLikeAssetPath(asset))
            {
                return;
            }

            if (!options.AssetExists(asset))
            {
                report.Add(AbilityValidationSeverity.Error, "ABILITY047", "Missing " + purpose + ": " + asset + ".", source);
            }
        }

        private static bool LooksLikeAssetPath(string value)
        {
            return value.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                   value.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddPositive(ISet<int> values, int value)
        {
            if (value > 0)
            {
                values.Add(value);
            }
        }
    }
}


using System;
using System.Collections.Generic;

namespace Game.Ability.Configuration
{
    /// <summary>
    /// JSON provider for complex, nested ability definitions. JSON source files are authored
    /// separately from Luban-generated JSON and converted into the same runtime definitions.
    /// </summary>
    public sealed class JsonAbilityDefinitionProvider : IAbilityDefinitionProvider
    {
        private readonly string json;
        private readonly string sourcePath;

        public JsonAbilityDefinitionProvider(string json, string sourcePath, string providerName = null)
        {
            this.json = json;
            this.sourcePath = sourcePath;
            ProviderName = string.IsNullOrWhiteSpace(providerName) ? "JSON:" + sourcePath : providerName;
        }

        public string ProviderName { get; }
        public AbilityDefinitionSourceType SourceType => AbilityDefinitionSourceType.Json;

        public AbilityDefinitionBundle Load()
        {
            AbilityDefinitionBundle bundle = new AbilityDefinitionBundle();
            if (string.IsNullOrWhiteSpace(json))
            {
                bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON001", "JSON ability source is empty.", Source());
                return bundle;
            }

            if (!AbilityJsonSchemaValidator.TryDeserialize(json, out JsonAbilityDocument document, out string schemaError))
            {
                bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON012", schemaError, Source());
                return bundle;
            }

            if (document == null)
            {
                bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON003", "JSON did not produce an ability document.", Source());
                return bundle;
            }

            if (document.schemaVersion != 1)
            {
                bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON004", "Unsupported JSON ability schemaVersion " + document.schemaVersion + "; expected 1.", Source());
                return bundle;
            }

            Dictionary<string, ProjectileDefinition> localProjectiles = BuildProjectiles(document, bundle);
            BuildModifiers(document, bundle, localProjectiles);
            BuildAbilities(document, bundle, localProjectiles);
            return bundle;
        }

        private Dictionary<string, ProjectileDefinition> BuildProjectiles(JsonAbilityDocument document, AbilityDefinitionBundle bundle)
        {
            Dictionary<string, ProjectileDefinition> result = new Dictionary<string, ProjectileDefinition>(StringComparer.Ordinal);
            JsonProjectile[] projectiles = document.projectiles ?? Array.Empty<JsonProjectile>();
            for (int i = 0; i < projectiles.Length; i++)
            {
                JsonProjectile source = projectiles[i];
                if (source == null || string.IsNullOrWhiteSpace(source.name))
                {
                    bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON005", "Projectile has no internal name.", Source());
                    continue;
                }

                ProjectileDefinition definition = new ProjectileDefinition
                {
                    Name = source.name,
                    Speed = source.speed,
                    Radius = source.radius,
                    Distance = source.distance,
                    MaxLifetime = source.maxLifetime,
                    TrackingTargetPolicy = (TrackingTargetPolicy)source.trackingTargetPolicy,
                    DeleteOnHit = source.deleteOnHit,
                    ProvidesVision = source.providesVision,
                    VisionRadius = source.visionRadius,
                    TargetTeam = (TargetTeam)source.targetTeam,
                    TargetType = (UnitType)source.targetType,
                    TargetFlags = (TargetFlags)source.targetFlags,
                    EffectName = source.effectName,
                    SoundName = source.soundName
                };
                if (!result.ContainsKey(definition.Name))
                {
                    result.Add(definition.Name, definition);
                }

                bundle.Projectiles.Add(new ProjectileDefinitionRegistration
                {
                    Definition = definition,
                    IsPrivate = source.isPrivate,
                    Origin = Origin(document, source.id)
                });
            }

            return result;
        }

        private void BuildModifiers(JsonAbilityDocument document, AbilityDefinitionBundle bundle, IReadOnlyDictionary<string, ProjectileDefinition> projectiles)
        {
            JsonModifier[] modifiers = document.modifiers ?? Array.Empty<JsonModifier>();
            for (int i = 0; i < modifiers.Length; i++)
            {
                JsonModifier source = modifiers[i];
                if (source == null)
                {
                    bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON006", "Modifier entry is null.", Source());
                    continue;
                }

                ModifierDefinition definition = new ModifierDefinition
                {
                    Name = source.name,
                    DisplayName = source.displayName,
                    IsHidden = source.isHidden,
                    IsDebuff = source.isDebuff,
                    IsPurgable = source.isPurgable,
                    RemoveOnDeath = source.removeOnDeath,
                    Duration = source.duration,
                    Interval = source.interval,
                    MaxStack = source.maxStack,
                    Attributes = (ModifierAttribute)source.attributes,
                    States = (UnitState)source.states,
                    TriggerEventType = (ModifierEventType)source.triggerEventType,
                    TriggerEventScope = (ModifierEventScope)source.triggerEventScope,
                    TriggerExcludedDamageFlags = (DamageFlags)source.triggerExcludedDamageFlags,
                    SustainedEffectName = source.sustainedEffectName,
                    AuraModifierName = source.auraModifierName,
                    AuraRadius = source.auraRadius,
                    AuraDuration = source.auraDuration,
                    AuraThinkInterval = source.auraThinkInterval,
                    AuraIncrementsStacks = source.auraIncrementsStacks,
                    AuraTargetTeam = (TargetTeam)source.auraTargetTeam,
                    AuraTargetType = (UnitType)source.auraTargetType,
                    AuraTargetFlags = (TargetFlags)source.auraTargetFlags
                };
                JsonModifierProperty[] properties = source.properties ?? Array.Empty<JsonModifierProperty>();
                for (int propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
                {
                    JsonModifierProperty property = properties[propertyIndex];
                    if (property != null && Enum.IsDefined(typeof(ModifierProperty), property.property) && property.property != 0 && !definition.Properties.ContainsKey((ModifierProperty)property.property))
                    {
                        definition.Properties[(ModifierProperty)property.property] = property.value;
                    }
                    else
                    {
                        bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON007", "Modifier " + source.name + " contains an unsupported or duplicate property value.", Source());
                    }
                }

                JsonModifierLevelProperty[] levelProperties = source.levelProperties ?? Array.Empty<JsonModifierLevelProperty>();
                for (int propertyIndex = 0; propertyIndex < levelProperties.Length; propertyIndex++)
                {
                    JsonModifierLevelProperty property = levelProperties[propertyIndex];
                    ModifierProperty key = property != null ? (ModifierProperty)property.property : ModifierProperty.None;
                    if (property != null && property.value != null && Enum.IsDefined(typeof(ModifierProperty), key) && key != ModifierProperty.None && !definition.Properties.ContainsKey(key) && !definition.LevelProperties.ContainsKey(key))
                    {
                        definition.LevelProperties.Add(key, Level(property.value));
                    }
                    else
                    {
                        bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON013", "Modifier " + source.name + " contains a missing, unsupported or duplicate level property value.", Source());
                    }
                }

                AddActions(source.onCreated, definition.OnCreatedActions, source.name + ".onCreated", projectiles, bundle);
                AddActions(source.onRefresh, definition.OnRefreshActions, source.name + ".onRefresh", projectiles, bundle);
                AddActions(source.onDestroy, definition.OnDestroyActions, source.name + ".onDestroy", projectiles, bundle);
                AddActions(source.intervalActions, definition.IntervalActions, source.name + ".intervalActions", projectiles, bundle);
                AddActions(source.triggerActions, definition.TriggerActions, source.name + ".triggerActions", projectiles, bundle);
                bundle.Modifiers.Add(new ModifierDefinitionRegistration
                {
                    Definition = definition,
                    IsPrivate = source.isPrivate,
                    RequiresCustomScript = source.requiresCustomScript,
                    Origin = Origin(document, source.id)
                });
            }
        }

        private void BuildAbilities(JsonAbilityDocument document, AbilityDefinitionBundle bundle, IReadOnlyDictionary<string, ProjectileDefinition> projectiles)
        {
            JsonAbility[] abilities = document.abilities ?? Array.Empty<JsonAbility>();
            for (int i = 0; i < abilities.Length; i++)
            {
                JsonAbility source = abilities[i];
                if (source == null)
                {
                    bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON008", "Ability entry is null.", Source());
                    continue;
                }

                AbilityDefinition definition = new AbilityDefinition
                {
                    Name = source.name,
                    DisplayName = source.displayName,
                    Description = source.description,
                    Icon = source.icon,
                    MaxLevel = source.maxLevel,
                    Behavior = (AbilityBehavior)source.behavior,
                    TargetTeam = (TargetTeam)source.targetTeam,
                    TargetType = (UnitType)source.targetType,
                    TargetFlags = (TargetFlags)source.targetFlags,
                    CastRange = Level(source.castRange),
                    AoeRadius = Level(source.aoeRadius),
                    CastPoint = Level(source.castPoint),
                    CastBackswing = Level(source.castBackswing),
                    ChannelTime = Level(source.channelTime),
                    Cooldown = Level(source.cooldown),
                    ManaCost = Level(source.manaCost),
                    IntrinsicModifierName = source.intrinsicModifierName
                };
                if (source.charges != null)
                {
                    definition.Charges = new ChargeDefinition
                    {
                        MaxCharges = source.charges.maxCharges,
                        RestoreTime = source.charges.restoreTime,
                        StartFull = source.charges.startFull,
                        UsesCooldown = source.charges.usesCooldown
                    };
                }

                JsonSpecialValue[] specials = source.specialValues ?? Array.Empty<JsonSpecialValue>();
                for (int specialIndex = 0; specialIndex < specials.Length; specialIndex++)
                {
                    JsonSpecialValue special = specials[specialIndex];
                    if (special == null || string.IsNullOrWhiteSpace(special.name) || definition.SpecialValues.ContainsKey(special.name))
                    {
                        bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON009", "Ability " + source.name + " has an empty or duplicate special value name.", Source());
                        continue;
                    }

                    definition.SpecialValues.Add(special.name, Level(special.value));
                }

                AddActions(source.actions, definition.Actions, source.name + ".actions", projectiles, bundle);
                AddActions(source.onProjectileHit, definition.OnProjectileHitActions, source.name + ".onProjectileHit", projectiles, bundle);
                AddActions(source.onToggleOn, definition.OnToggleOnActions, source.name + ".onToggleOn", projectiles, bundle);
                AddActions(source.onToggleOff, definition.OnToggleOffActions, source.name + ".onToggleOff", projectiles, bundle);
                bundle.Abilities.Add(new AbilityDefinitionRegistration
                {
                    Definition = definition,
                    RequiresCustomScript = source.requiresCustomScript,
                    Origin = Origin(document, source.id)
                });
            }
        }

        private void AddActions(
            JsonAction[] sources,
            ICollection<ActionDefinition> target,
            string owner,
            IReadOnlyDictionary<string, ProjectileDefinition> projectiles,
            AbilityDefinitionBundle bundle)
        {
            JsonAction[] actions = sources ?? Array.Empty<JsonAction>();
            for (int i = 0; i < actions.Length; i++)
            {
                JsonAction source = actions[i];
                if (source == null || !Enum.IsDefined(typeof(ActionType), source.actionType) || !Enum.IsDefined(typeof(ActionTarget), source.target))
                {
                    bundle.Validation.Add(AbilityValidationSeverity.Error, "ABILITYJSON010", owner + " contains a null or unsupported action.", Source());
                    continue;
                }

                ActionDefinition action = new ActionDefinition
                {
                    ActionType = (ActionType)source.actionType,
                    Target = (ActionTarget)source.target,
                    ValueSource = (ActionValueSource)source.valueSource,
                    Value = Level(source.value),
                    ValueSpecialName = source.valueSpecialName,
                    Duration = Level(source.duration, float.NaN),
                    DurationSpecialName = source.durationSpecialName,
                    DamageType = (DamageType)source.damageType,
                    DamageFlags = (DamageFlags)source.damageFlags,
                    ModifierName = source.modifierName,
                    ProjectileName = source.projectileName,
                    PurgePositiveBuffs = source.purgePositiveBuffs,
                    PurgeDebuffs = source.purgeDebuffs,
                    PurgeOnlyPurgable = source.purgeOnlyPurgable,
                    EffectName = source.effectName,
                    SoundName = source.soundName
                };
                if (!string.IsNullOrEmpty(source.projectileName))
                {
                    // Named references may resolve from another provider at final graph validation.
                    if (projectiles.TryGetValue(source.projectileName, out ProjectileDefinition projectile)) action.Projectile = projectile;
                }

                target.Add(action);
            }
        }

        private AbilityDefinitionOrigin Origin(JsonAbilityDocument document, string stableId)
        {
            return new AbilityDefinitionOrigin
            {
                SourceType = AbilityDefinitionSourceType.Json,
                ProviderName = ProviderName,
                SourcePath = sourcePath,
                StableId = stableId,
                Namespace = document.@namespace
            };
        }

        private AbilityConfigSource Source()
        {
            return new AbilityConfigSource
            {
                SourceType = AbilityDefinitionSourceType.Json.ToString(),
                Path = sourcePath
            };
        }

        private static LevelValue Level(JsonLevelValue value, float fallback = 0f)
        {
            LevelValue result = LevelValue.Constant(value != null ? value.baseValue : fallback);
            if (value?.values != null)
            {
                result.ValuesByLevel.AddRange(value.values);
            }
            return result;
        }
    }

    [Serializable]
    public sealed class JsonAbilityDocument
    {
        public int schemaVersion = 1;
        public string @namespace;
        public JsonAbility[] abilities;
        public JsonModifier[] modifiers;
        public JsonProjectile[] projectiles;
    }

    [Serializable]
    public sealed class JsonAbility
    {
        public string id;
        public string name;
        public string displayName;
        public string description;
        public string icon;
        public bool requiresCustomScript;
        public int maxLevel = 1;
        public int behavior = (int)AbilityBehavior.NoTarget;
        public int targetTeam = (int)TargetTeam.Enemy;
        public int targetType = (int)UnitType.All;
        public int targetFlags;
        public JsonLevelValue castRange;
        public JsonLevelValue aoeRadius;
        public JsonLevelValue castPoint;
        public JsonLevelValue castBackswing;
        public JsonLevelValue channelTime;
        public JsonLevelValue cooldown;
        public JsonLevelValue manaCost;
        public JsonCharge charges;
        public string intrinsicModifierName;
        public JsonSpecialValue[] specialValues;
        public JsonAction[] actions;
        public JsonAction[] onProjectileHit;
        public JsonAction[] onToggleOn;
        public JsonAction[] onToggleOff;
    }

    [Serializable]
    public sealed class JsonModifier
    {
        public string id;
        public string name;
        public string displayName;
        public bool isPrivate;
        public bool requiresCustomScript;
        public bool isHidden;
        public bool isDebuff;
        public bool isPurgable = true;
        public bool removeOnDeath = true;
        public float duration;
        public float interval;
        public int maxStack = 1;
        public int attributes;
        public int states;
        public JsonModifierProperty[] properties;
        public JsonModifierLevelProperty[] levelProperties;
        public JsonAction[] onCreated;
        public JsonAction[] onRefresh;
        public JsonAction[] onDestroy;
        public JsonAction[] intervalActions;
        public int triggerEventType;
        public int triggerEventScope;
        public int triggerExcludedDamageFlags;
        public JsonAction[] triggerActions;
        public string sustainedEffectName;
        public string auraModifierName;
        public float auraRadius;
        public float auraDuration = 0.5f;
        public float auraThinkInterval = 0.25f;
        public bool auraIncrementsStacks;
        public int auraTargetTeam = (int)TargetTeam.Friendly;
        public int auraTargetType = (int)UnitType.All;
        public int auraTargetFlags;
    }

    [Serializable]
    public sealed class JsonProjectile
    {
        public string id;
        public string name;
        public bool isPrivate;
        public float speed = 900f;
        public float radius = 96f;
        public float distance = 1200f;
        public float maxLifetime = 10f;
        public int trackingTargetPolicy;
        public bool deleteOnHit = true;
        public bool providesVision;
        public float visionRadius;
        public int targetTeam = (int)TargetTeam.Enemy;
        public int targetType = (int)UnitType.All;
        public int targetFlags;
        public string effectName;
        public string soundName;
    }

    [Serializable]
    public sealed class JsonAction
    {
        public int actionType;
        public int target = (int)ActionTarget.ContextTargets;
        public int valueSource;
        public JsonLevelValue value;
        public string valueSpecialName;
        public JsonLevelValue duration;
        public string durationSpecialName;
        public int damageType = (int)DamageType.Magical;
        public int damageFlags;
        public string modifierName;
        public string projectileName;
        public bool purgePositiveBuffs;
        public bool purgeDebuffs = true;
        public bool purgeOnlyPurgable = true;
        public string effectName;
        public string soundName;
    }

    [Serializable]
    public sealed class JsonLevelValue
    {
        public float baseValue;
        public float[] values;
    }

    [Serializable]
    public sealed class JsonCharge
    {
        public int maxCharges;
        public float restoreTime;
        public bool startFull = true;
        public bool usesCooldown = true;
    }

    [Serializable]
    public sealed class JsonSpecialValue
    {
        public string name;
        public JsonLevelValue value;
    }

    [Serializable]
    public sealed class JsonModifierProperty
    {
        public int property;
        public float value;
    }

    [Serializable]
    public sealed class JsonModifierLevelProperty
    {
        public int property;
        public JsonLevelValue value;
    }
}


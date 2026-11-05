using System.Collections.Generic;

namespace Game.Ability.Configuration
{
    /// <summary>Publication owns a detached graph; editing provider data cannot change a running game.</summary>
    internal sealed class AbilityDefinitionCloner
    {
        private readonly Dictionary<ProjectileDefinition, ProjectileDefinition> projectiles = new Dictionary<ProjectileDefinition, ProjectileDefinition>();

        private readonly IReadOnlyDictionary<string, ProjectileDefinitionRegistration> namedProjectiles;

        public AbilityDefinitionCloner(IReadOnlyDictionary<string, ProjectileDefinitionRegistration> namedProjectiles)
        {
            this.namedProjectiles = namedProjectiles;
        }

        public AbilityDefinition Clone(AbilityDefinition source)
        {
            AbilityDefinition result = new AbilityDefinition
            {
                Name = source.Name, DisplayName = source.DisplayName, Description = source.Description, Icon = source.Icon,
                MaxLevel = source.MaxLevel, Behavior = source.Behavior, TargetTeam = source.TargetTeam, TargetType = source.TargetType, TargetFlags = source.TargetFlags,
                CastRange = Clone(source.CastRange), AoeRadius = Clone(source.AoeRadius), CastPoint = Clone(source.CastPoint), CastBackswing = Clone(source.CastBackswing),
                ChannelTime = Clone(source.ChannelTime), Cooldown = Clone(source.Cooldown), ManaCost = Clone(source.ManaCost), IntrinsicModifierName = source.IntrinsicModifierName,
                Charges = source.Charges == null ? null : new ChargeDefinition { MaxCharges = source.Charges.MaxCharges, RestoreTime = source.Charges.RestoreTime, StartFull = source.Charges.StartFull, UsesCooldown = source.Charges.UsesCooldown }
            };
            foreach (KeyValuePair<string, LevelValue> pair in source.SpecialValues) result.SpecialValues.Add(pair.Key, Clone(pair.Value));
            CloneActions(source.Actions, result.Actions);
            CloneActions(source.OnProjectileHitActions, result.OnProjectileHitActions);
            CloneActions(source.OnToggleOnActions, result.OnToggleOnActions);
            CloneActions(source.OnToggleOffActions, result.OnToggleOffActions);
            return result;
        }

        public ModifierDefinition Clone(ModifierDefinition source)
        {
            ModifierDefinition result = new ModifierDefinition
            {
                Name = source.Name, DisplayName = source.DisplayName, IsHidden = source.IsHidden, IsDebuff = source.IsDebuff, IsPurgable = source.IsPurgable,
                RemoveOnDeath = source.RemoveOnDeath, Duration = source.Duration, Interval = source.Interval, MaxStack = source.MaxStack, Attributes = source.Attributes, States = source.States,
                TriggerEventType = source.TriggerEventType, TriggerEventScope = source.TriggerEventScope, SustainedEffectName = source.SustainedEffectName,
                TriggerExcludedDamageFlags = source.TriggerExcludedDamageFlags,
                AuraModifierName = source.AuraModifierName, AuraRadius = source.AuraRadius, AuraDuration = source.AuraDuration, AuraThinkInterval = source.AuraThinkInterval,
                AuraIncrementsStacks = source.AuraIncrementsStacks, AuraTargetTeam = source.AuraTargetTeam, AuraTargetType = source.AuraTargetType, AuraTargetFlags = source.AuraTargetFlags
            };
            foreach (KeyValuePair<ModifierProperty, float> pair in source.Properties) result.Properties.Add(pair.Key, pair.Value);
            foreach (KeyValuePair<ModifierProperty, LevelValue> pair in source.LevelProperties) result.LevelProperties.Add(pair.Key, Clone(pair.Value));
            CloneActions(source.OnCreatedActions, result.OnCreatedActions);
            CloneActions(source.OnRefreshActions, result.OnRefreshActions);
            CloneActions(source.OnDestroyActions, result.OnDestroyActions);
            CloneActions(source.IntervalActions, result.IntervalActions);
            CloneActions(source.TriggerActions, result.TriggerActions);
            return result;
        }

        private static LevelValue Clone(LevelValue source)
        {
            if (source == null) return null;
            LevelValue result = LevelValue.Constant(source.BaseValue);
            result.ValuesByLevel.AddRange(source.ValuesByLevel);
            return result;
        }

        private void CloneActions(IReadOnlyList<ActionDefinition> source, ICollection<ActionDefinition> target)
        {
            for (int i = 0; i < source.Count; i++)
            {
                ActionDefinition action = source[i];
                target.Add(new ActionDefinition
                {
                    ActionType = action.ActionType, Target = action.Target, ValueSource = action.ValueSource, Value = Clone(action.Value), ValueSpecialName = action.ValueSpecialName,
                    Duration = Clone(action.Duration), DurationSpecialName = action.DurationSpecialName, DamageType = action.DamageType, DamageFlags = action.DamageFlags,
                    ModifierName = action.ModifierName, PurgePositiveBuffs = action.PurgePositiveBuffs, PurgeDebuffs = action.PurgeDebuffs, PurgeOnlyPurgable = action.PurgeOnlyPurgable,
                    ProjectileName = action.ProjectileName, Projectile = Clone(!string.IsNullOrEmpty(action.ProjectileName) ? namedProjectiles[action.ProjectileName].Definition : action.Projectile), EffectName = action.EffectName, SoundName = action.SoundName
                });
            }
        }

        private ProjectileDefinition Clone(ProjectileDefinition source)
        {
            if (source == null) return null;
            if (projectiles.TryGetValue(source, out ProjectileDefinition existing)) return existing;
            ProjectileDefinition result = new ProjectileDefinition
            {
                Name = source.Name, Speed = source.Speed, Radius = source.Radius, Distance = source.Distance, MaxLifetime = source.MaxLifetime,
                TrackingTargetPolicy = source.TrackingTargetPolicy, DeleteOnHit = source.DeleteOnHit, ProvidesVision = source.ProvidesVision, VisionRadius = source.VisionRadius,
                TargetTeam = source.TargetTeam, TargetType = source.TargetType, TargetFlags = source.TargetFlags, EffectName = source.EffectName, SoundName = source.SoundName
            };
            projectiles.Add(source, result);
            return result;
        }
    }
}

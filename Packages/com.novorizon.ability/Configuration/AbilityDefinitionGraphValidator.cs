using System;
using System.Collections.Generic;

namespace Game.Ability.Configuration
{
    /// <summary>Source-independent validation for the graph that the runtime will actually consume.</summary>
    internal sealed class AbilityDefinitionGraphValidator
    {
        private readonly IReadOnlyDictionary<string, AbilityDefinitionRegistration> abilities;
        private readonly IReadOnlyDictionary<string, ModifierDefinitionRegistration> modifiers;
        private readonly IReadOnlyDictionary<string, ProjectileDefinitionRegistration> projectiles;
        private readonly AbilityValidationReport report;
        private readonly Stack<SpecialVisit> specialVisits = new Stack<SpecialVisit>();
        private AbilityDefinitionOrigin origin;
        private string owner;

        public AbilityDefinitionGraphValidator(IReadOnlyDictionary<string, AbilityDefinitionRegistration> abilities, IReadOnlyDictionary<string, ModifierDefinitionRegistration> modifiers, IReadOnlyDictionary<string, ProjectileDefinitionRegistration> projectiles, AbilityValidationReport report)
        {
            this.abilities = abilities;
            this.modifiers = modifiers;
            this.projectiles = projectiles;
            this.report = report;
        }

        public void Validate()
        {
            foreach (KeyValuePair<string, AbilityDefinitionRegistration> pair in abilities)
            {
                origin = pair.Value.Origin;
                owner = "ability " + pair.Key;
                AbilityDefinition definition = pair.Value.Definition;
                if (definition == null) { Error("Definition is null."); continue; }
                ValidateName(pair.Key, definition.Name);
                if (definition.MaxLevel < 1) Error("MaxLevel must be positive.");
                Flags(definition.Behavior, "Behavior");
                Targeting(definition.TargetTeam, definition.TargetType, definition.TargetFlags);
                AbilityBehavior behavior = definition.Behavior;
                if ((behavior & AbilityBehavior.AutoCast) != 0) Error("AutoCast has no runtime policy; use an explicit order policy instead.");
                bool passive = (behavior & AbilityBehavior.Passive) != 0;
                bool toggle = (behavior & AbilityBehavior.Toggle) != 0;
                if ((behavior & AbilityBehavior.NoTarget) != 0 && (behavior & (AbilityBehavior.UnitTarget | AbilityBehavior.PointTarget | AbilityBehavior.OptionalUnitTarget)) != 0) Error("NoTarget cannot be combined with unit or point targeting.");
                if (passive && (behavior & (AbilityBehavior.NoTarget | AbilityBehavior.UnitTarget | AbilityBehavior.PointTarget | AbilityBehavior.OptionalUnitTarget | AbilityBehavior.Toggle | AbilityBehavior.Channelled)) != 0) Error("Passive cannot also declare an active cast mode.");
                if (!passive && (behavior & (AbilityBehavior.NoTarget | AbilityBehavior.UnitTarget | AbilityBehavior.PointTarget | AbilityBehavior.OptionalUnitTarget)) == 0) Error("An active ability requires a supported cast mode, including when a custom script is declared.");
                if (toggle && (behavior & AbilityBehavior.Channelled) != 0) Error("Toggle cannot also be Channelled.");
                if (toggle && definition.Actions.Count > 0) Error("Toggle uses OnToggleOnActions and OnToggleOffActions, so normal Actions must be empty.");
                if (!toggle && (definition.OnToggleOnActions.Count > 0 || definition.OnToggleOffActions.Count > 0)) Error("Toggle lifecycle actions require Toggle behavior.");
                if (passive && definition.Actions.Count > 0) Error("Passive Actions have no cast lifecycle; use an intrinsic modifier.");
                Level(definition.CastRange, "CastRange", true); Level(definition.AoeRadius, "AoeRadius", true);
                Level(definition.CastPoint, "CastPoint", true); Level(definition.CastBackswing, "CastBackswing", true);
                Level(definition.ChannelTime, "ChannelTime", true); Level(definition.Cooldown, "Cooldown", true); Level(definition.ManaCost, "ManaCost", true);
                if ((behavior & AbilityBehavior.Channelled) != 0 && !AllPositive(definition.ChannelTime)) Error("Channelled requires positive ChannelTime at every level.");
                if (definition.Charges != null)
                {
                    if (definition.Charges.MaxCharges < 1) Error("Charges.MaxCharges must be positive.");
                    Number(definition.Charges.RestoreTime, "Charges.RestoreTime", true, true);
                }
                foreach (KeyValuePair<string, LevelValue> special in definition.SpecialValues)
                {
                    if (string.IsNullOrWhiteSpace(special.Key)) Error("Special value name must be nonempty.");
                    Level(special.Value, "SpecialValues." + special.Key, false);
                }
                Reference(definition.IntrinsicModifierName, "intrinsic modifier", false);
                Actions(definition.Actions);
                Actions(definition.OnProjectileHitActions, triggerContext: true);
                for (int i = 0; i < definition.OnProjectileHitActions.Count; i++)
                    if (definition.OnProjectileHitActions[i] != null && definition.OnProjectileHitActions[i].ValueSource != ActionValueSource.LevelOrSpecial) Error("Projectile hit actions do not carry committed damage or healing values.");
                Actions(definition.OnToggleOnActions);
                Actions(definition.OnToggleOffActions);
                HashSet<string> reachable = new HashSet<string>(StringComparer.Ordinal);
                SpecialActions(definition.Actions, definition, reachable);
                SpecialActions(definition.OnProjectileHitActions, definition, reachable);
                SpecialActions(definition.OnToggleOnActions, definition, reachable);
                SpecialActions(definition.OnToggleOffActions, definition, reachable);
                SpecialModifier(definition.IntrinsicModifierName, definition, reachable);
            }

            foreach (KeyValuePair<string, ModifierDefinitionRegistration> pair in modifiers)
            {
                origin = pair.Value.Origin;
                owner = "modifier " + pair.Key;
                ModifierDefinition definition = pair.Value.Definition;
                if (definition == null) { Error("Definition is null."); continue; }
                ValidateName(pair.Key, definition.Name);
                Number(definition.Duration, "Duration"); Number(definition.Interval, "Interval", true);
                if (definition.MaxStack < 1) Error("MaxStack must be positive.");
                Flags(definition.Attributes, "Attributes"); Flags(definition.States, "States");
                if ((definition.Attributes & ModifierAttribute.StackIndependent) != 0) Error("StackIndependent is unsupported; independent stack lifetime requires a separate implementation.");
                if ((definition.States & UnitState.Muted) != 0 && !pair.Value.RequiresCustomScript) Error("Muted requires an explicitly declared custom modifier script that owns an item policy; ordinary spell casting has no item concept.");
                if ((definition.Attributes & ModifierAttribute.Aura) != 0 && string.IsNullOrEmpty(definition.AuraModifierName) && !pair.Value.RequiresCustomScript) Error("Aura requires AuraModifierName or an explicitly declared custom modifier script.");
                if ((definition.States & UnitState.NoUnitCollision) != 0 && !pair.Value.RequiresCustomScript) Error("NoUnitCollision requires an explicitly declared custom modifier script.");
                foreach (KeyValuePair<ModifierProperty, float> property in definition.Properties)
                {
                    Defined(property.Key, "Property");
                    if (property.Key == ModifierProperty.None) Error("Property.None has no consumer.");
                    Number(property.Value, property.Key.ToString());
                    if (property.Key == ModifierProperty.HealthRegen && property.Value < 0f && !pair.Value.RequiresCustomScript) Error("Negative HealthRegen requires an explicit custom modifier script; the built-in regeneration consumer heals only.");
                }
                foreach (KeyValuePair<ModifierProperty, LevelValue> property in definition.LevelProperties)
                {
                    Defined(property.Key, "LevelProperty");
                    if (property.Key == ModifierProperty.None) Error("LevelProperty.None has no consumer.");
                    if (definition.Properties.ContainsKey(property.Key)) Error("A property cannot appear in both Properties and LevelProperties: " + property.Key + ".");
                    Level(property.Value, "LevelProperties." + property.Key, property.Key == ModifierProperty.HealthRegen && !pair.Value.RequiresCustomScript);
                }
                Defined(definition.TriggerEventType, "TriggerEventType"); Defined(definition.TriggerEventScope, "TriggerEventScope");
                Flags(definition.TriggerExcludedDamageFlags, "TriggerExcludedDamageFlags");
                if (definition.TriggerExcludedDamageFlags != DamageFlags.None && definition.TriggerEventType != ModifierEventType.DamageCalculated && definition.TriggerEventType != ModifierEventType.DamageDealt && definition.TriggerEventType != ModifierEventType.DamageTaken) Error("TriggerExcludedDamageFlags requires a damage event.");
                if (definition.IntervalActions.Count > 0 && definition.Interval <= 0f) Error("IntervalActions require a positive Interval.");
                if (definition.TriggerActions.Count > 0 && definition.TriggerEventType == ModifierEventType.None) Error("TriggerActions require a TriggerEventType.");
                Reference(definition.AuraModifierName, "aura modifier", false);
                Number(definition.AuraRadius, "AuraRadius", true); Number(definition.AuraDuration, "AuraDuration", true); Number(definition.AuraThinkInterval, "AuraThinkInterval", true);
                Targeting(definition.AuraTargetTeam, definition.AuraTargetType, definition.AuraTargetFlags);
                if (!string.IsNullOrEmpty(definition.AuraModifierName))
                {
                    if (definition.AuraDuration <= 0f || definition.AuraThinkInterval <= 0f) Error("Aura duration and think interval must be positive.");
                }
                foreach (IReadOnlyList<ActionDefinition> list in ModifierActions(definition)) Actions(list, true, ReferenceEquals(list, definition.TriggerActions));
                for (int i = 0; i < definition.TriggerActions.Count; i++)
                {
                    ActionDefinition action = definition.TriggerActions[i];
                    if (action == null) continue;
                    if (action.ValueSource == ActionValueSource.EventDamage && definition.TriggerEventType != ModifierEventType.DamageDealt && definition.TriggerEventType != ModifierEventType.DamageTaken) Error("EventDamage requires a committed DamageDealt or DamageTaken event.");
                    if (action.ValueSource == ActionValueSource.EventHealing && definition.TriggerEventType != ModifierEventType.Healed) Error("EventHealing requires a Healed event.");
                }
            }
            foreach (KeyValuePair<string, ProjectileDefinitionRegistration> pair in projectiles)
            {
                origin = pair.Value.Origin;
                owner = "projectile " + pair.Key;
                if (pair.Value.Definition == null) { Error("Definition is null."); continue; }
                ValidateName(pair.Key, pair.Value.Definition.Name);
                Projectile(pair.Value.Definition);
            }
            Dictionary<string, int> colors = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> path = new List<string>();
            Stack<ImmediateVisit> visits = new Stack<ImmediateVisit>();
            foreach (string name in modifiers.Keys) VisitImmediate(name, colors, path, visits);
        }

        private void Actions(IReadOnlyList<ActionDefinition> actions, bool modifierContext = false, bool triggerContext = false)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                ActionDefinition action = actions[i];
                if (action == null) { Error("Contains a null action.", "ABILITYPROVIDER006"); continue; }
                Defined(action.ActionType, "ActionType"); Defined(action.Target, "ActionTarget"); Defined(action.ValueSource, "ActionValueSource");
                if (action.Target == ActionTarget.Parent && !modifierContext) Error("Parent action target requires a modifier lifecycle.");
                if ((action.Target == ActionTarget.EventSource || action.Target == ActionTarget.EventTarget || action.ValueSource != ActionValueSource.LevelOrSpecial) && !triggerContext) Error("Event targets and value sources require TriggerActions.");
                if (action.ActionType == ActionType.None) Error("ActionType.None has no operation.");
                if (action.ActionType == ActionType.PlayEffect && string.IsNullOrWhiteSpace(action.EffectName)) Error("PlayEffect requires EffectName.");
                if (action.ActionType == ActionType.PlaySound && string.IsNullOrWhiteSpace(action.SoundName)) Error("PlaySound requires SoundName.");
                bool unitOperation = action.ActionType == ActionType.Damage || action.ActionType == ActionType.Heal || action.ActionType == ActionType.AddModifier || action.ActionType == ActionType.Purge || action.ActionType == ActionType.CreateTrackingProjectile;
                if (unitOperation && (action.Target == ActionTarget.Point || action.Target == ActionTarget.None)) Error(action.ActionType + " requires a unit action target.");
                Level(action.Value, "Action.Value", action.ActionType == ActionType.Damage || action.ActionType == ActionType.Heal);
                Level(action.Duration, "Action.Duration", false, true);
                Defined(action.DamageType, "DamageType"); Flags(action.DamageFlags, "DamageFlags");
                if (action.ActionType == ActionType.Damage && action.DamageType == DamageType.None) Error("Damage requires a damage type.");
                if (action.ActionType == ActionType.AddModifier) Reference(action.ModifierName, "modifier", true);
                bool projectile = action.ActionType == ActionType.CreateTrackingProjectile || action.ActionType == ActionType.CreateLinearProjectile;
                ProjectileDefinition effectiveProjectile = action.Projectile;
                if (!string.IsNullOrEmpty(action.ProjectileName))
                {
                    if (projectiles.TryGetValue(action.ProjectileName, out ProjectileDefinitionRegistration named))
                    {
                        if (action.Projectile != null && !ReferenceEquals(action.Projectile, named.Definition)) Error("ProjectileName and inline Projectile must refer to the same registered definition.");
                        effectiveProjectile = named.Definition;
                    }
                    else Error("References missing projectile '" + action.ProjectileName + "'.", "ABILITYPROVIDER010");
                }
                if (projectile && effectiveProjectile == null) Error("References missing projectile.", "ABILITYPROVIDER010");
                if (effectiveProjectile != null) Projectile(effectiveProjectile);
            }
        }

        private void Projectile(ProjectileDefinition definition)
        {
            Number(definition.Speed, "Projectile.Speed", true); Number(definition.Radius, "Projectile.Radius", true); Number(definition.Distance, "Projectile.Distance", true);
            Number(definition.MaxLifetime, "Projectile.MaxLifetime", true, true); Number(definition.VisionRadius, "Projectile.VisionRadius", true);
            Defined(definition.TrackingTargetPolicy, "TrackingTargetPolicy");
            Targeting(definition.TargetTeam, definition.TargetType, definition.TargetFlags);
            if (definition.ProvidesVision) Error("Projectile vision has no runtime consumer; ProvidesVision must be false.");
        }

        private void SpecialActions(IReadOnlyList<ActionDefinition> actions, AbilityDefinition ability, HashSet<string> reachable)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                ActionDefinition action = actions[i];
                if (action == null) continue;
                ValidateActionSpecials(action, ability);
                if (action.ActionType == ActionType.AddModifier) SpecialModifier(action.ModifierName, ability, reachable);
            }
        }

        private void SpecialModifier(string name, AbilityDefinition ability, HashSet<string> reachable)
        {
            if (string.IsNullOrEmpty(name) || reachable.Contains(name)) return;
            specialVisits.Push(new SpecialVisit(name));
            while (specialVisits.Count > 0)
            {
                SpecialVisit visit = specialVisits.Pop();
                if (visit.Action != null)
                {
                    ValidateActionSpecials(visit.Action, ability);
                    if (visit.Action.ActionType == ActionType.AddModifier && !string.IsNullOrEmpty(visit.Action.ModifierName))
                        specialVisits.Push(new SpecialVisit(visit.Action.ModifierName));
                    continue;
                }
                if (!reachable.Add(visit.Name) || !modifiers.TryGetValue(visit.Name, out ModifierDefinitionRegistration registration) || registration.Definition == null) continue;
                ModifierDefinition modifier = registration.Definition;
                // Reverse scheduling preserves the old depth-first diagnostic order without
                // consuming a native stack frame for each referenced modifier.
                if (!string.IsNullOrEmpty(modifier.AuraModifierName)) specialVisits.Push(new SpecialVisit(modifier.AuraModifierName));
                PushSpecialActions(modifier.TriggerActions);
                PushSpecialActions(modifier.IntervalActions);
                PushSpecialActions(modifier.OnDestroyActions);
                PushSpecialActions(modifier.OnRefreshActions);
                PushSpecialActions(modifier.OnCreatedActions);
            }
        }

        private void ValidateActionSpecials(ActionDefinition action, AbilityDefinition ability)
        {
            Special(action.ValueSpecialName, ability);
            Special(action.DurationSpecialName, ability);
            if (!string.IsNullOrEmpty(action.ValueSpecialName) && ability.SpecialValues.TryGetValue(action.ValueSpecialName, out LevelValue special) && (action.ActionType == ActionType.Damage || action.ActionType == ActionType.Heal))
                Level(special, "Action.SpecialValues." + action.ValueSpecialName, true);
        }

        private void PushSpecialActions(IReadOnlyList<ActionDefinition> actions)
        {
            for (int i = actions.Count - 1; i >= 0; i--)
                if (actions[i] != null) specialVisits.Push(new SpecialVisit(actions[i]));
        }

        private readonly struct SpecialVisit
        {
            public readonly string Name;
            public readonly ActionDefinition Action;
            public SpecialVisit(string name) { Name = name; Action = null; }
            public SpecialVisit(ActionDefinition action) { Name = null; Action = action; }
        }

        private void Special(string name, AbilityDefinition ability)
        {
            if (!string.IsNullOrEmpty(name) && !ability.SpecialValues.ContainsKey(name)) Error("References missing special value '" + name + "' in ability " + ability.Name + ".", "ABILITYPROVIDER010");
        }

        private void VisitImmediate(string name, IDictionary<string, int> colors, List<string> path, Stack<ImmediateVisit> visits)
        {
            if (colors.ContainsKey(name) || !modifiers.TryGetValue(name, out ModifierDefinitionRegistration registration) || registration.Definition == null) return;
            colors[name] = 1;
            path.Add(name);
            visits.Push(new ImmediateVisit(name, registration.Definition));
            while (visits.Count > 0)
            {
                ImmediateVisit visit = visits.Pop();
                string target = visit.NextReference();
                if (target == null)
                {
                    colors[visit.Name] = 2;
                    path.RemoveAt(path.Count - 1);
                    continue;
                }
                visits.Push(visit);
                if (colors.TryGetValue(target, out int color))
                {
                    if (color == 1)
                    {
                        origin = modifiers[target].Origin; owner = "modifier " + target;
                        Error("Immediate modifier creation cycle: " + string.Join(" -> ", path) + " -> " + target + ".", "ABILITYGRAPH002");
                    }
                    continue;
                }
                if (!modifiers.TryGetValue(target, out ModifierDefinitionRegistration child) || child.Definition == null) continue;
                colors[target] = 1;
                path.Add(target);
                visits.Push(new ImmediateVisit(target, child.Definition));
            }
        }

        private struct ImmediateVisit
        {
            public readonly string Name;
            private readonly ModifierDefinition definition;
            private int group;
            private int nextAction;

            public ImmediateVisit(string name, ModifierDefinition definition) { Name = name; this.definition = definition; group = 0; nextAction = 0; }

            public string NextReference()
            {
                while (group < 2)
                {
                    IReadOnlyList<ActionDefinition> actions = group == 0 ? definition.OnCreatedActions : definition.OnRefreshActions;
                    while (nextAction < actions.Count)
                    {
                        ActionDefinition action = actions[nextAction++];
                        if (action != null && action.ActionType == ActionType.AddModifier && !string.IsNullOrEmpty(action.ModifierName)) return action.ModifierName;
                    }
                    group++;
                    nextAction = 0;
                }
                if (group++ == 2 && !string.IsNullOrEmpty(definition.AuraModifierName)) return definition.AuraModifierName;
                return null;
            }
        }

        private static IEnumerable<IReadOnlyList<ActionDefinition>> ModifierActions(ModifierDefinition definition)
        {
            yield return definition.OnCreatedActions; yield return definition.OnRefreshActions; yield return definition.OnDestroyActions;
            yield return definition.IntervalActions; yield return definition.TriggerActions;
        }

        private void Reference(string name, string kind, bool required)
        {
            if ((required || !string.IsNullOrEmpty(name)) && (string.IsNullOrEmpty(name) || !modifiers.ContainsKey(name))) Error("References missing " + kind + " '" + name + "'.", "ABILITYPROVIDER010");
        }

        private void ValidateName(string key, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || !string.Equals(key, name, StringComparison.Ordinal)) Error("Internal name changed after registration; reload the provider instead.");
        }

        private void Targeting(TargetTeam team, UnitType type, TargetFlags flags)
        {
            Flags(team, "TargetTeam"); Flags(type, "TargetType"); Flags(flags, "TargetFlags");
            if ((flags & (TargetFlags.IncludeSelf | TargetFlags.ExcludeSelf)) == (TargetFlags.IncludeSelf | TargetFlags.ExcludeSelf)) Error("IncludeSelf and ExcludeSelf cannot be combined.");
        }

        private void Level(LevelValue value, string field, bool nonnegative, bool allowUnspecified = false)
        {
            if (value == null) { Error(field + " cannot be null."); return; }
            if (!(allowUnspecified && float.IsNaN(value.BaseValue))) Number(value.BaseValue, field, nonnegative);
            for (int i = 0; i < value.ValuesByLevel.Count; i++) Number(value.ValuesByLevel[i], field + "[" + i + "]", nonnegative);
        }

        private static bool AllPositive(LevelValue value)
        {
            if (value == null) return false;
            if (value.ValuesByLevel.Count == 0) return value.BaseValue > 0f;
            for (int i = 0; i < value.ValuesByLevel.Count; i++) if (!(value.ValuesByLevel[i] > 0f)) return false;
            return true;
        }

        private void Number(float value, string field, bool nonnegative = false, bool positive = false)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || (nonnegative && value < 0f) || (positive && value <= 0f)) Error(field + " must be " + (positive ? "positive and finite." : nonnegative ? "nonnegative and finite." : "finite."));
        }

        private void Defined<T>(T value, string field) where T : struct
        {
            if (!Enum.IsDefined(typeof(T), value)) Error(field + " contains an unknown value " + value + ".");
        }

        private void Flags<T>(T value, string field) where T : struct
        {
            ulong mask = 0;
            foreach (object item in Enum.GetValues(typeof(T))) mask |= Convert.ToUInt64(item);
            long raw = Convert.ToInt64(value);
            if (raw < 0 || ((ulong)raw & ~mask) != 0) Error(field + " contains unknown flags " + value + ".");
        }

        private void Error(string message, string code = "ABILITYGRAPH001")
        {
            report.Add(AbilityValidationSeverity.Error, code, owner + ": " + message, origin?.ToConfigSource(), owner);
        }
    }
}

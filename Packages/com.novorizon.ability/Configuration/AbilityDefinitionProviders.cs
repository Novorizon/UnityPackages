using System;
using System.Collections.Generic;

namespace Game.Ability.Configuration
{
    public enum AbilityDefinitionSourceType
    {
        Unknown = 0,
        Excel = 1,
        Json = 2,
        Code = 3
    }

    [Serializable]
    public sealed class AbilityDefinitionOrigin
    {
        public AbilityDefinitionSourceType SourceType;
        public string ProviderName;
        public string SourcePath;
        public string StableId;
        public string Namespace;

        public AbilityConfigSource ToConfigSource()
        {
            return new AbilityConfigSource
            {
                SourceType = SourceType.ToString(),
                Path = SourcePath
            };
        }

        public override string ToString()
        {
            string id = string.IsNullOrEmpty(StableId) ? string.Empty : "#" + StableId;
            return SourceType + ":" + SourcePath + id;
        }
    }

    public sealed class AbilityDefinitionRegistration
    {
        public AbilityDefinition Definition;
        public AbilityDefinitionOrigin Origin;
        // Declare script-owned behavior explicitly; publication verifies the factory exists.
        public bool RequiresCustomScript;
    }

    public sealed class ModifierDefinitionRegistration
    {
        public ModifierDefinition Definition;
        public AbilityDefinitionOrigin Origin;
        public bool IsPrivate;
        public bool RequiresCustomScript;
    }

    public sealed class ProjectileDefinitionRegistration
    {
        public ProjectileDefinition Definition;
        public AbilityDefinitionOrigin Origin;
        public bool IsPrivate;
    }

    public sealed class AbilityDefinitionBundle
    {
        public readonly List<AbilityDefinitionRegistration> Abilities = new List<AbilityDefinitionRegistration>();
        public readonly List<ModifierDefinitionRegistration> Modifiers = new List<ModifierDefinitionRegistration>();
        public readonly List<ProjectileDefinitionRegistration> Projectiles = new List<ProjectileDefinitionRegistration>();
        public readonly AbilityValidationReport Validation = new AbilityValidationReport();
    }

    public interface IAbilityDefinitionProvider
    {
        string ProviderName { get; }
        AbilityDefinitionSourceType SourceType { get; }
        AbilityDefinitionBundle Load();
    }

    /// <summary>
    /// Merges definitions from providers without order-based overwrite. Duplicate stable IDs or
    /// names make the registry invalid, and no definitions are applied to AbilitySystem.
    /// </summary>
    public sealed class AbilityDefinitionRegistry
    {
        private readonly Dictionary<string, AbilityDefinitionRegistration> abilitiesByName =
            new Dictionary<string, AbilityDefinitionRegistration>(StringComparer.Ordinal);
        private readonly Dictionary<string, AbilityDefinitionRegistration> abilitiesByStableId =
            new Dictionary<string, AbilityDefinitionRegistration>(StringComparer.Ordinal);
        private readonly Dictionary<string, ModifierDefinitionRegistration> modifiersByName =
            new Dictionary<string, ModifierDefinitionRegistration>(StringComparer.Ordinal);
        private readonly Dictionary<string, ModifierDefinitionRegistration> modifiersByStableId =
            new Dictionary<string, ModifierDefinitionRegistration>(StringComparer.Ordinal);
        private readonly Dictionary<string, ProjectileDefinitionRegistration> projectilesByName =
            new Dictionary<string, ProjectileDefinitionRegistration>(StringComparer.Ordinal);
        private readonly Dictionary<string, ProjectileDefinitionRegistration> projectilesByStableId =
            new Dictionary<string, ProjectileDefinitionRegistration>(StringComparer.Ordinal);

        private readonly AbilityValidationReport loadValidation = new AbilityValidationReport();

        public AbilityValidationReport Validation => ValidateMergedDefinitions();
        public bool IsValid => Validation.IsValid;
        public IReadOnlyDictionary<string, AbilityDefinitionRegistration> Abilities => abilitiesByName;
        public IReadOnlyDictionary<string, ModifierDefinitionRegistration> Modifiers => modifiersByName;
        public IReadOnlyDictionary<string, ProjectileDefinitionRegistration> Projectiles => projectilesByName;

        public void LoadProviders(IEnumerable<IAbilityDefinitionProvider> providers)
        {
            if (providers == null)
            {
                loadValidation.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER001", "Provider collection is null.");
                return;
            }

            foreach (IAbilityDefinitionProvider provider in providers)
            {
                LoadProvider(provider);
            }

            ValidateMergedDefinitions();
        }

        public void LoadProvider(IAbilityDefinitionProvider provider)
        {
            if (provider == null)
            {
                loadValidation.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER002", "Ability definition provider is null.");
                return;
            }

            AbilityDefinitionBundle bundle;
            try
            {
                bundle = provider.Load();
            }
            catch (Exception exception)
            {
                loadValidation.Add(
                    AbilityValidationSeverity.Error,
                    "ABILITYPROVIDER003",
                    "Provider " + provider.ProviderName + " failed: " + exception.Message + ".",
                    new AbilityConfigSource { SourceType = provider.SourceType.ToString() });
                return;
            }

            if (bundle == null)
            {
                loadValidation.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER004", "Provider " + provider.ProviderName + " returned no bundle.");
                return;
            }

            loadValidation.Merge(bundle.Validation);
            for (int i = 0; i < bundle.Abilities.Count; i++) AddAbility(bundle.Abilities[i]);
            for (int i = 0; i < bundle.Modifiers.Count; i++) AddModifier(bundle.Modifiers[i]);
            for (int i = 0; i < bundle.Projectiles.Count; i++) AddProjectile(bundle.Projectiles[i]);
        }

        public bool ApplyTo(AbilitySystem engine)
        {
            if (engine == null)
            {
                LastApplyValidation = ValidateMergedDefinitions();
                LastApplyValidation.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER005", "Cannot apply definitions to a null AbilitySystem.");
                return false;
            }

            // Never trust a previous validation: provider definitions remain editable authoring data.
            LastApplyValidation = null;
            AbilityValidationReport report = ValidateMergedDefinitions();
            foreach (AbilityDefinitionRegistration registration in abilitiesByName.Values)
                if (registration.Definition != null && registration.RequiresCustomScript && !engine.HasAbilityScript(registration.Definition.Name))
                    report.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER011", "Required ability script is not registered: " + registration.Definition.Name + ".", registration.Origin?.ToConfigSource());
            foreach (ModifierDefinitionRegistration registration in modifiersByName.Values)
                if (registration.Definition != null && registration.RequiresCustomScript && !engine.HasModifierScript(registration.Definition.Name))
                    report.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER011", "Required modifier script is not registered: " + registration.Definition.Name + ".", registration.Origin?.ToConfigSource());
            if (!report.IsValid)
            {
                LastApplyValidation = report;
                return false;
            }

            AbilityDefinitionCloner cloner = new AbilityDefinitionCloner(projectilesByName);
            List<AbilityDefinition> abilities = new List<AbilityDefinition>();
            List<ModifierDefinition> modifiers = new List<ModifierDefinition>();
            foreach (AbilityDefinitionRegistration registration in abilitiesByName.Values) abilities.Add(cloner.Clone(registration.Definition));
            foreach (ModifierDefinitionRegistration registration in modifiersByName.Values) modifiers.Add(cloner.Clone(registration.Definition));
            bool applied = engine.ReplaceDefinitions(abilities, modifiers);
            if (!applied) report.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER012", "Cannot replace definitions while runtime objects exist or the engine is clearing.");
            LastApplyValidation = report;
            return applied;
        }

        /// <summary>Result of the latest ApplyTo attempt, including engine-specific script and runtime requirements.</summary>
        public AbilityValidationReport LastApplyValidation { get; private set; }

        public bool TryGetAbilityNameByStableId(string stableId, out string name)
        {
            if (stableId != null && abilitiesByStableId.TryGetValue(stableId, out AbilityDefinitionRegistration registration))
            {
                name = registration.Definition?.Name;
                return !string.IsNullOrEmpty(name);
            }
            name = null;
            return false;
        }

        public bool TryResolveAbilityName(string key, out string name)
        {
            if (key != null && abilitiesByName.ContainsKey(key)) { name = key; return true; }
            return TryGetAbilityNameByStableId(key, out name);
        }

        public bool TryResolveModifierName(string key, out string name)
        {
            if (key != null && modifiersByName.ContainsKey(key)) { name = key; return true; }
            if (key != null && modifiersByStableId.TryGetValue(key, out ModifierDefinitionRegistration registration)) { name = registration.Definition.Name; return true; }
            name = null;
            return false;
        }

        public bool TryGetAbilityOrigin(string name, out AbilityDefinitionOrigin origin)
        {
            if (name != null && abilitiesByName.TryGetValue(name, out AbilityDefinitionRegistration registration))
            {
                origin = registration.Origin;
                return true;
            }

            origin = null;
            return false;
        }

        private void AddAbility(AbilityDefinitionRegistration registration)
        {
            string name = registration?.Definition?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                AddInvalidRegistration("ability", registration?.Origin);
                return;
            }

            if (abilitiesByName.TryGetValue(name, out AbilityDefinitionRegistration existing))
            {
                AddCollision("ability name", name, existing.Origin, registration.Origin);
                return;
            }

            if (!TryAddStableId("ability", registration.Origin, registration, abilitiesByStableId))
            {
                return;
            }

            abilitiesByName.Add(name, registration);
        }

        private void AddModifier(ModifierDefinitionRegistration registration)
        {
            string name = registration?.Definition?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                AddInvalidRegistration("modifier", registration?.Origin);
                return;
            }

            if (modifiersByName.TryGetValue(name, out ModifierDefinitionRegistration existing))
            {
                AddCollision("modifier name", name, existing.Origin, registration.Origin);
                return;
            }

            if (!TryAddStableId("modifier", registration.Origin, registration, modifiersByStableId))
            {
                return;
            }

            ValidatePrivateName("modifier", name, registration.IsPrivate, registration.Origin);
            modifiersByName.Add(name, registration);
        }

        private void AddProjectile(ProjectileDefinitionRegistration registration)
        {
            string name = registration?.Definition?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                AddInvalidRegistration("projectile", registration?.Origin);
                return;
            }

            if (projectilesByName.TryGetValue(name, out ProjectileDefinitionRegistration existing))
            {
                AddCollision("projectile name", name, existing.Origin, registration.Origin);
                return;
            }

            if (!TryAddStableId("projectile", registration.Origin, registration, projectilesByStableId))
            {
                return;
            }

            ValidatePrivateName("projectile", name, registration.IsPrivate, registration.Origin);
            projectilesByName.Add(name, registration);
        }

        private bool TryAddStableId<T>(string kind, AbilityDefinitionOrigin origin, T registration, IDictionary<string, T> index)
        {
            string stableId = origin?.StableId;
            if (string.IsNullOrWhiteSpace(stableId))
            {
                return true;
            }

            if (index.ContainsKey(stableId))
            {
                AbilityDefinitionOrigin existingOrigin = GetOrigin(index[stableId]);
                AddCollision(kind + " stable ID", stableId, existingOrigin, origin);
                return false;
            }

            index.Add(stableId, registration);
            return true;
        }

        private AbilityValidationReport ValidateMergedDefinitions()
        {
            AbilityValidationReport report = new AbilityValidationReport();
            report.Merge(loadValidation);
            ValidateStableIndex(abilitiesByStableId, abilitiesByName, report);
            ValidateStableIndex(modifiersByStableId, modifiersByName, report);
            ValidateStableIndex(projectilesByStableId, projectilesByName, report);
            new AbilityDefinitionGraphValidator(abilitiesByName, modifiersByName, projectilesByName, report).Validate();
            return report;
        }

        private static void ValidateStableIndex<T>(IReadOnlyDictionary<string, T> ids, IReadOnlyDictionary<string, T> names, AbilityValidationReport report)
        {
            foreach (T registration in names.Values)
            {
                AbilityDefinitionOrigin registrationOrigin = GetOrigin(registration);
                string stableId = registrationOrigin?.StableId;
                if (!string.IsNullOrWhiteSpace(stableId) && (!ids.TryGetValue(stableId, out T indexed) || !ReferenceEquals(indexed, registration)))
                    report.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER013", "Stable ID changed after registration; reload the provider instead.", registrationOrigin?.ToConfigSource());
            }
            foreach (KeyValuePair<string, T> pair in ids)
            {
                AbilityDefinitionOrigin origin = GetOrigin(pair.Value);
                if (pair.Key != origin?.StableId) report.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER013", "Stable ID changed after registration; reload the provider instead.", origin?.ToConfigSource());
                if (names.TryGetValue(pair.Key, out T byName) && !ReferenceEquals(byName, pair.Value)) report.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER009", "Stable ID is ambiguous with another definition name: " + pair.Key + ".", origin?.ToConfigSource());
            }
        }

        private void ValidatePrivateName(string kind, string name, bool isPrivate, AbilityDefinitionOrigin origin)
        {
            if (!isPrivate)
            {
                return;
            }

            string expectedPrefix = string.IsNullOrWhiteSpace(origin?.Namespace) ? null : origin.Namespace + ".";
            if (string.IsNullOrEmpty(expectedPrefix) || !name.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                loadValidation.Add(
                    AbilityValidationSeverity.Warning,
                    "ABILITYPROVIDER007",
                    "Private " + kind + " " + name + " should use its document namespace prefix " + (expectedPrefix ?? "<missing namespace>") + ".",
                    origin?.ToConfigSource());
            }
        }

        private void AddInvalidRegistration(string kind, AbilityDefinitionOrigin origin)
        {
            loadValidation.Add(AbilityValidationSeverity.Error, "ABILITYPROVIDER008", "Provider returned a " + kind + " with no internal name.", origin?.ToConfigSource());
        }

        private void AddCollision(string keyKind, string key, AbilityDefinitionOrigin first, AbilityDefinitionOrigin second)
        {
            loadValidation.Add(
                AbilityValidationSeverity.Error,
                "ABILITYPROVIDER009",
                "Duplicate " + keyKind + " " + key + ". First: " + first + "; second: " + second + ". No load-order override is allowed.",
                second?.ToConfigSource(),
                first + " <-> " + second);
        }

        private static AbilityDefinitionOrigin GetOrigin<T>(T registration)
        {
            if (registration is AbilityDefinitionRegistration ability) return ability.Origin;
            if (registration is ModifierDefinitionRegistration modifier) return modifier.Origin;
            if (registration is ProjectileDefinitionRegistration projectile) return projectile.Origin;
            return null;
        }
    }
}


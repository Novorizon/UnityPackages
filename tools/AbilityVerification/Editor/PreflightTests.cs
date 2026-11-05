using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Ability;
using Game.Ability.Configuration;
using Game.Ability.Editor.Core;

namespace Game.Ability.Editor.Verification
{
    public static class PreflightTests
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            passed = failed = 0;
            Case("ten thousand references are bounded without recursive traversal", VeryLongChainIsRejected);
            foreach (string slot in new[] { "created", "refresh", "destroy", "interval", "trigger", "aura" })
            {
                string captured = slot;
                Case("modifier reference edge is counted: " + slot, () => ModifierSlotsAreChecked(captured));
            }
            foreach (string slot in new[] { "intrinsic", "actions", "projectile", "toggleOn", "toggleOff" })
            {
                string captured = slot;
                Case("ability start is counted: " + slot, () => AbilityStartsAreChecked(captured));
            }
            Case("a thousand independent definitions are not a path limit violation", IndependentModifiersPass);
            Case("cross-file references preserve diagnostic source", CrossFileReferencesAreChecked);
            Case("small event cycles remain allowed by preflight and production", SmallEventCycleIsAllowed);
            Case("immediate cycle legality remains with production validator", ImmediateCycleRemainsProductionResponsibility);
            Case("large cycles receive a complexity guard, not a cycle prohibition", LargeCycleIsBounded);
            Case("branching DAG uses a path bound rather than exponential enumeration", BranchingDagIsBounded);
            Case("random DAG path bounds match an independent reference", RandomDagBoundsMatchReference);
            Case("SCC weights are counted once along their condensation path", WeightedSccPathCountsEachGroupOnce);
            Case("explicit code-provider bundles are checked before registry validation", RuntimeBundlesAreChecked);
            Case("missing references and duplicate-name validity are not reimplemented", DomainValidityRemainsProductionResponsibility);
            Case("real hero and composition files stay below the authoring limit", ExistingFixturesPass);
            Case("preflight does not mutate authoring DTOs", InputIsNotMutated);
            Case("authoring budget is configurable and cannot be disabled with int.MaxValue", BudgetIsBounded);
            Console.WriteLine("Editor reference preflight: " + passed + " passed, " + failed + " failed.");
            return failed;
        }

        private static void VeryLongChainIsRejected()
        {
            JsonAbilityDocument document = Chain(10000, "created");
            IReadOnlyList<AbilityEditorDiagnostic> diagnostics = AbilityReferencePreflight.Validate(new[] { document });
            Require(diagnostics.Count == 1 && diagnostics[0].Code == AbilityReferencePreflight.DiagnosticCode, "Long chain must produce one authoring complexity diagnostic.");
            Require(diagnostics[0].Message.Contains("10000") && diagnostics[0].Message.Contains("256"), "Diagnostic must expose the static upper bound and authoring budget.");
            Require(diagnostics[0].ReferenceChain.Length < 1000, "A diagnostic cannot allocate/render a ten-thousand-entry reference string.");
        }

        private static void ModifierSlotsAreChecked(string slot)
        {
            JsonAbilityDocument document = Chain(5, slot);
            Require(AbilityReferencePreflight.Validate(new[] { document }, 4).Count == 1, "Modifier reference slot was not traversed: " + slot);
            Require(AbilityReferencePreflight.Validate(new[] { document }, 5).Count == 0, "Exact authoring boundary should pass: " + slot);
        }

        private static void AbilityStartsAreChecked(string slot)
        {
            JsonAbilityDocument document = Chain(2, "created");
            JsonAbility ability = new JsonAbility { name = "source", behavior = (int)AbilityBehavior.NoTarget };
            switch (slot)
            {
                case "intrinsic": ability.intrinsicModifierName = "m0"; break;
                case "actions": ability.actions = Actions("m0"); break;
                case "projectile": ability.onProjectileHit = Actions("m0"); break;
                case "toggleOn": ability.onToggleOn = Actions("m0"); break;
                case "toggleOff": ability.onToggleOff = Actions("m0"); break;
            }
            document.abilities = new[] { ability };
            Require(AbilityReferencePreflight.Validate(new[] { document }, 2).Count == 1, "Skill root must add one authoring definition: " + slot);
            Require(AbilityReferencePreflight.Validate(new[] { document }, 3).Count == 0, "Exact skill-plus-modifier boundary must pass.");
        }

        private static void IndependentModifiersPass()
        {
            JsonAbilityDocument document = new JsonAbilityDocument
            {
                modifiers = Enumerable.Range(0, 1000).Select(index => new JsonModifier { name = "independent." + index }).ToArray(),
                abilities = Enumerable.Range(0, 1000).Select(index => new JsonAbility { name = "source." + index, intrinsicModifierName = "independent." + index }).ToArray()
            };
            Require(AbilityReferencePreflight.Validate(new[] { document }, 2).Count == 0, "Total library size is not reference depth.");
        }

        private static void CrossFileReferencesAreChecked()
        {
            JsonAbilityDocument first = new JsonAbilityDocument { abilities = new[] { new JsonAbility { name = "hero.cast", actions = Actions("a") } }, modifiers = new[] { new JsonModifier { name = "a", onCreated = Actions("b") } } };
            JsonAbilityDocument second = new JsonAbilityDocument { modifiers = new[] { new JsonModifier { name = "b", triggerActions = Actions("c") }, new JsonModifier { name = "c" } } };
            var sources = new[] { new KeyValuePair<string, JsonAbilityDocument>("hero.json", first), new KeyValuePair<string, JsonAbilityDocument>("shared.json", second) };
            IReadOnlyList<AbilityEditorDiagnostic> diagnostics = AbilityReferencePreflight.ValidateSources(sources, 3);
            Require(diagnostics.Count == 1 && diagnostics[0].FilePath == "hero.json", "Cross-file path must retain its skill source.");
            Require(diagnostics[0].ReferenceChain.Contains("hero.cast") && diagnostics[0].ReferenceChain.Contains("modifier c"), "Cross-file reference chain should aid navigation.");
            Require(AbilityReferencePreflight.ValidateSources(sources, 4).Count == 0, "Cross-file exact boundary should pass.");
        }

        private static void SmallEventCycleIsAllowed()
        {
            JsonAbilityDocument document = Chain(2, "trigger");
            document.modifiers[1].triggerEventType = (int)ModifierEventType.DamageTaken;
            document.modifiers[1].triggerActions = Actions("m0");
            document.abilities = new[] { new JsonAbility { name = "event.source", actions = new[] { new JsonAction { actionType = (int)ActionType.AddModifier, target = (int)ActionTarget.Caster, modifierName = "m0" } } } };
            Require(AbilityReferencePreflight.Validate(new[] { document }).Count == 0, "Event cycles alone are not prohibited.");
            AbilityDefinitionRegistry registry = Registry(document);
            Require(registry.IsValid, "Production validation must still permit this event cycle.");
        }

        private static void ImmediateCycleRemainsProductionResponsibility()
        {
            JsonAbilityDocument document = Chain(2, "created");
            document.modifiers[1].onCreated = Actions("m0");
            Require(AbilityReferencePreflight.Validate(new[] { document }).Count == 0, "Preflight must not replace production cycle diagnostics.");
            AbilityDefinitionRegistry registry = Registry(document);
            Require(!registry.IsValid, "Production must reject an immediate creation cycle.");
            Require(registry.Validation.Issues.Any(issue => issue.Code == "ABILITYGRAPH002"), "Expected the authoritative immediate-cycle diagnostic.");
        }

        private static void LargeCycleIsBounded()
        {
            JsonAbilityDocument document = Chain(1000, "trigger");
            document.modifiers[999].triggerActions = Actions("m0");
            document.modifiers[999].triggerEventType = (int)ModifierEventType.DamageTaken;
            IReadOnlyList<AbilityEditorDiagnostic> diagnostics = AbilityReferencePreflight.Validate(new[] { document });
            Require(diagnostics.Count == 1 && diagnostics[0].ReferenceChain.Contains("强连通组 1000"), "Large SCC should be conservatively bounded without recursive cycle walking.");
            Require(diagnostics[0].Message.Contains("保守上界"), "Upper bound must not be misreported as an exact actual execution path.");
        }

        private static void BranchingDagIsBounded()
        {
            const int layers = 300;
            JsonModifier[] modifiers = new JsonModifier[layers * 2];
            for (int layer = 0; layer < layers; layer++)
                for (int side = 0; side < 2; side++)
                {
                    int index = layer * 2 + side;
                    modifiers[index] = new JsonModifier { name = "dag." + index };
                    if (layer + 1 < layers) modifiers[index].onDestroy = new[] { Actions("dag." + ((layer + 1) * 2))[0], Actions("dag." + ((layer + 1) * 2 + 1))[0] };
                }
            JsonAbilityDocument document = new JsonAbilityDocument { modifiers = modifiers };
            Require(AbilityReferencePreflight.Validate(new[] { document }, layers - 1).Count == 1, "Long branching path should exceed the lower budget.");
            Require(AbilityReferencePreflight.Validate(new[] { document }, layers).Count == 0, "The DAG's total node count must not replace path length.");
        }

        private static void RandomDagBoundsMatchReference()
        {
            Random random = new Random(1739);
            for (int sample = 0; sample < 50; sample++)
            {
                const int count = 30;
                JsonModifier[] modifiers = new JsonModifier[count];
                int[] longest = new int[count];
                int maximum = 1;
                // Generated edges strictly increase the index, so a small reverse-index DP
                // is independent of the implementation's SCC and topological-order machinery.
                for (int index = count - 1; index >= 0; index--)
                {
                    List<JsonAction> actions = new List<JsonAction>();
                    longest[index] = 1;
                    for (int target = index + 1; target < count; target++)
                        if (random.NextDouble() < 0.14)
                        {
                            actions.Add(Actions("random." + target)[0]);
                            longest[index] = Math.Max(longest[index], 1 + longest[target]);
                        }
                    modifiers[index] = new JsonModifier { name = "random." + index, triggerActions = actions.ToArray() };
                    maximum = Math.Max(maximum, longest[index]);
                }
                JsonAbilityDocument document = new JsonAbilityDocument { modifiers = modifiers };
                Require(AbilityReferencePreflight.Validate(new[] { document }, maximum).Count == 0, "DAG bound falsely exceeded its exact maximum at sample " + sample);
                if (maximum > 1) Require(AbilityReferencePreflight.Validate(new[] { document }, maximum - 1).Count == 1, "DAG bound missed its longest path at sample " + sample);
            }
        }

        private static void WeightedSccPathCountsEachGroupOnce()
        {
            List<JsonModifier> modifiers = new List<JsonModifier>();
            int[] sizes = { 3, 2, 4 };
            for (int group = 0; group < sizes.Length; group++)
                for (int member = 0; member < sizes[group]; member++)
                {
                    List<JsonAction> edges = new List<JsonAction> { Actions("scc." + group + "." + ((member + 1) % sizes[group]))[0] };
                    if (member == 0 && group + 1 < sizes.Length) edges.Add(Actions("scc." + (group + 1) + ".0")[0]);
                    modifiers.Add(new JsonModifier { name = "scc." + group + "." + member, triggerActions = edges.ToArray() });
                }
            JsonAbilityDocument document = new JsonAbilityDocument { modifiers = modifiers.ToArray() };
            Require(AbilityReferencePreflight.Validate(new[] { document }, 8).Count == 1, "SCC path weights must add across groups.");
            Require(AbilityReferencePreflight.Validate(new[] { document }, 9).Count == 0, "SCC cycles cannot be counted repeatedly.");
        }

        private static void RuntimeBundlesAreChecked()
        {
            AbilityDefinitionBundle first = new AbilityDefinitionBundle();
            AbilityDefinitionBundle second = new AbilityDefinitionBundle();
            first.Abilities.Add(new AbilityDefinitionRegistration { Definition = new AbilityDefinition { Name = "code.source", IntrinsicModifierName = "code.a" }, Origin = new AbilityDefinitionOrigin { SourcePath = "code-provider" } });
            ModifierDefinition a = new ModifierDefinition { Name = "code.a", AuraModifierName = "code.b" };
            first.Modifiers.Add(new ModifierDefinitionRegistration { Definition = a });
            ModifierDefinition b = new ModifierDefinition { Name = "code.b" };
            b.IntervalActions.Add(new ActionDefinition { ActionType = ActionType.AddModifier, ModifierName = "code.c" });
            second.Modifiers.Add(new ModifierDefinitionRegistration { Definition = b });
            second.Modifiers.Add(new ModifierDefinitionRegistration { Definition = new ModifierDefinition { Name = "code.c" } });
            IReadOnlyList<AbilityEditorDiagnostic> diagnostics = AbilityReferencePreflight.ValidateBundles(new[] { first, second }, 3);
            Require(diagnostics.Count == 1 && diagnostics[0].FilePath == "code-provider", "Code and JSON providers need the same pre-registry guard.");
            Require(AbilityReferencePreflight.ValidateBundles(new[] { first, second }, 4).Count == 0, "Exact code-provider boundary should pass.");
        }

        private static void DomainValidityRemainsProductionResponsibility()
        {
            JsonAbilityDocument document = new JsonAbilityDocument { modifiers = new[] { new JsonModifier { name = "orphan", onCreated = Actions("missing") }, new JsonModifier { name = "orphan" } } };
            Require(AbilityReferencePreflight.Validate(new[] { document }, 1).Count == 0, "Missing/duplicate definitions belong to production validation.");
            Require(!Registry(document).IsValid, "Production still rejects this invalid document.");
            Require(AbilityReferencePreflight.Validate(new[] { new JsonAbilityDocument { modifiers = new JsonModifier[] { null, new JsonModifier() }, abilities = new JsonAbility[] { null } } }).Count == 0, "Incomplete drafts must not crash the preflight.");
        }

        private static void ExistingFixturesPass()
        {
            string root = FindSourceRoot();
            JsonAbilityDocument[] documents = Directory.GetFiles(Path.Combine(root, "Data", "AbilityJsonSources"), "*.json", SearchOption.AllDirectories)
                .Select(path => { Require(AbilityJsonCodec.TryRead(File.ReadAllText(path), out JsonAbilityDocument document, out string error), error); return document; }).ToArray();
            Require(documents.Length >= 10 && AbilityReferencePreflight.Validate(documents).Count == 0, "Ordinary hero/composition files should not hit the static guard.");
        }

        private static void InputIsNotMutated()
        {
            JsonAbilityDocument document = Chain(10, "trigger");
            string before = AbilityJsonCodec.Write(document);
            AbilityReferencePreflight.Validate(new[] { document }, 4);
            Require(before == AbilityJsonCodec.Write(document), "Preflight modified the authoring draft.");
        }

        private static void BudgetIsBounded()
        {
            JsonAbilityDocument document = Chain(5, "created");
            Require(AbilityReferencePreflight.Validate(new[] { document }, 4).Count == 1 && AbilityReferencePreflight.Validate(new[] { document }, 5).Count == 0, "Authoring budget must be configurable.");
            foreach (int invalid in new[] { 0, -1, int.MaxValue })
            {
                bool rejected = false;
                try { AbilityReferencePreflight.Validate(new[] { document }, invalid); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Require(rejected, "An unsupported authoring budget was accepted: " + invalid);
            }
            Require(AbilityReferencePreflight.Validate(null).Count == 0, "No documents should have no graph complexity diagnostic.");
        }

        private static JsonAbilityDocument Chain(int length, string slot)
        {
            JsonModifier[] modifiers = new JsonModifier[length];
            for (int index = 0; index < length; index++)
            {
                JsonModifier modifier = new JsonModifier { name = "m" + index };
                modifiers[index] = modifier;
                if (index + 1 == length) continue;
                JsonAction[] actions = Actions("m" + (index + 1));
                switch (slot)
                {
                    case "created": modifier.onCreated = actions; break;
                    case "refresh": modifier.onRefresh = actions; break;
                    case "destroy": modifier.onDestroy = actions; break;
                    case "interval": modifier.interval = 1f; modifier.intervalActions = actions; break;
                    case "trigger": modifier.triggerEventType = (int)ModifierEventType.DamageTaken; modifier.triggerActions = actions; break;
                    case "aura": modifier.auraModifierName = "m" + (index + 1); break;
                }
            }
            return new JsonAbilityDocument { modifiers = modifiers };
        }

        private static JsonAction[] Actions(string modifierName) => new[] { new JsonAction { actionType = (int)ActionType.AddModifier, target = (int)ActionTarget.Parent, modifierName = modifierName } };

        private static AbilityDefinitionRegistry Registry(JsonAbilityDocument document)
        {
            AbilityDefinitionRegistry registry = new AbilityDefinitionRegistry();
            registry.LoadProvider(new JsonAbilityDefinitionProvider(AbilityJsonCodec.Write(document), "preflight.json"));
            return registry;
        }

        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

        private static void Case(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS preflight: " + name); }
            catch (Exception exception) { failed++; Console.WriteLine("FAIL preflight: " + name + "\n" + exception); }
        }

        private static string FindSourceRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (DirectoryInfo directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                    if (Directory.Exists(Path.Combine(directory.FullName, "tools", "AbilityVerification", "Fixtures", "Data", "AbilityJsonSources", "Heroes"))) return Path.Combine(directory.FullName, "tools", "AbilityVerification", "Fixtures");
            throw new DirectoryNotFoundException("Preflight fixtures require Data/AbilityJsonSources in the source verification directory.");
        }
    }
}

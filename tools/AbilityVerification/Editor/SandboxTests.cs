using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Ability.Configuration;
using Game.Ability.Editor.Core;

namespace Game.Ability.Editor.Verification
{
    public static class SandboxTests
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            passed = failed = 0;
            Case("real hero Nova uses level damage, targets and resources", () =>
            {
                AbilitySandboxReport report = Hero("Dota2/crystal_maiden.json", "hero.dota2.crystal_maiden.crystal_nova", 4, new AbilitySandboxOptions { TargetCount = 3, SimulationSeconds = 0.6f });
                Completed(report);
                Equal(report.DamageTotal, 780f);
                Equal(report.ManaSpent, 175f);
                Require(report.Units.Count == 4 && report.Units.Skip(1).All(unit => unit.MoveSpeedBonus == -50f), "Configured slow and target count must be visible.");
            });
            Case("real hero tracking bolt damages and stuns only its primary target", () =>
            {
                AbilitySandboxReport report = Hero("Warcraft3/mountain_king.json", "wc3.mountain_king.storm_bolt", 3, new AbilitySandboxOptions { TargetCount = 3, SimulationSeconds = 1f });
                Completed(report);
                Equal(report.DamageTotal, 350f);
                Require((report.Units[1].States & UnitState.Stunned) != 0 && report.Units[2].States == UnitState.None, "Stun must belong to the primary target.");
                Require(report.Events.Any(line => line.Contains("ProjectileHit")), "The production projectile path was not exercised.");
            });
            Case("real hero frostbite runs immediate and final boundary damage", () =>
            {
                AbilitySandboxReport report = Hero("Dota2/crystal_maiden.json", "hero.dota2.crystal_maiden.frostbite", 1, new AbilitySandboxOptions { TargetCount = 1, SimulationSeconds = 2f });
                Completed(report);
                Equal(report.DamageTotal, 100f);
                Require((report.Units[1].States & (UnitState.Rooted | UnitState.Disarmed)) == 0, "Expired control must no longer appear in the scene result.");
            });
            Case("real hero aura includes caster and follows selected level", () =>
            {
                AbilitySandboxReport report = Hero("Warcraft3/paladin.json", "wc3.paladin.devotion_aura", 3, new AbilitySandboxOptions { TargetCount = 2, SimulationSeconds = 0.3f });
                Completed(report);
                Require(report.Units.Count == 3 && report.Units.All(unit => unit.TeamId == 1 && unit.ArmorBonus == 4.5f), "The passive source and children must use rank 3.");
                Equal(report.ManaSpent, 0f);
            });
            Case("real hero piercing wave runs configured hit behavior", () =>
            {
                AbilitySandboxReport report = Hero("Dota2/lina.json", "hero.dota2.lina.dragon_slave", 2, new AbilitySandboxOptions { TargetCount = 3, SimulationSeconds = 1f });
                Completed(report);
                Equal(report.DamageTotal, 480f);
                Require(report.RuntimeObjectsBeforeCleanup > 0, "The active ability/projectile must be cleared after this window.");
            });
            Case("friendly heal reports actual health change", () =>
            {
                AbilitySandboxReport report = AbilitySandbox.Run(new[] { Fixture("Validation/composed_skills.json") }, "validation.mend", 1, new AbilitySandboxOptions { TargetCount = 1, SimulationSeconds = 0f });
                Completed(report);
                Equal(report.HealingTotal, 30f);
                Equal(report.Units[1].Health, 5030f);
            });
            Case("event recursion passes static graph but is stopped by operation budget", () =>
            {
                AbilitySandboxReport report = AbilitySandbox.Run(new[] { Json(RecursiveDamage()) }, "probe", 1, new AbilitySandboxOptions { TargetCount = 1, MaxExecutionDepth = 128, MaxExecutionOperations = 24 });
                BudgetAborted(report);
                Require(report.ValidationMessages.Count == 0 && report.BudgetMessages.Any(line => line.Contains("operations=")), "The run should expose the engine's operation count instead of claiming static rejection.");
            });
            Case("event recursion is stopped by depth budget", () =>
            {
                AbilitySandboxReport report = AbilitySandbox.Run(new[] { Json(RecursiveDamage()) }, "probe", 1, new AbilitySandboxOptions { TargetCount = 1, MaxExecutionDepth = 8, MaxExecutionOperations = 8192 });
                BudgetAborted(report);
                Require(report.BudgetMessages.Any(line => line.Contains("depth=")), "The actual engine depth must be reported.");
            });
            Case("the same acyclic JSON can exceed its operation budget only at larger target counts", () =>
            {
                const string json = "{\"abilities\":[{\"name\":\"fanout\",\"behavior\":36,\"targetTeam\":2,\"aoeRadius\":{\"baseValue\":10},\"actions\":[{\"actionType\":1,\"target\":3,\"damageType\":3,\"value\":{\"baseValue\":1}}]}]}";
                AbilitySandboxReport one = AbilitySandbox.Run(new[] { Json(json) }, "fanout", 1, new AbilitySandboxOptions { TargetCount = 1, MaxExecutionOperations = 64 });
                AbilitySandboxReport many = AbilitySandbox.Run(new[] { Json(json) }, "fanout", 1, new AbilitySandboxOptions { TargetCount = 128, MaxExecutionOperations = 64 });
                Completed(one);
                BudgetAborted(many);
                Require(many.ValidationMessages.Count == 0, "A valid reference graph cannot statically guarantee every scene's execution budget.");
            });
            Case("a short window does not pretend to have completed a pending cast", () =>
            {
                AbilitySandboxReport report = Hero("Dota2/lina.json", "hero.dota2.lina.laguna_blade", 3, new AbilitySandboxOptions { TargetCount = 1, SimulationSeconds = 0.1f });
                Completed(report);
                Require(report.HasPendingWork && report.Message.Contains("未完成"), "Pending runtime work must be disclosed instead of claiming every ability path was tested.");
                Equal(report.DamageTotal, 0f);
            });
            Case("immediate modifier creation cycle is rejected before running", () =>
            {
                const string json = "{\"abilities\":[{\"name\":\"probe\",\"actions\":[{\"actionType\":3,\"target\":1,\"modifierName\":\"a\"}]}],\"modifiers\":[{\"name\":\"a\",\"onCreated\":[{\"actionType\":3,\"target\":5,\"modifierName\":\"b\"}]},{\"name\":\"b\",\"onCreated\":[{\"actionType\":3,\"target\":5,\"modifierName\":\"a\"}]}]}";
                AbilitySandboxReport report = AbilitySandbox.Run(new[] { Json(json) }, "probe", 1);
                Require(!report.Success && !report.ExecutionAborted && report.ValidationMessages.Any(line => line.IndexOf("cycle", StringComparison.OrdinalIgnoreCase) >= 0 || line.Contains("环")), "Static graph evidence must be distinguished from a runtime budget stop.");
                Clean(report);
            });
            Case("very long reference chains are rejected iteratively before runtime graph validation", () =>
            {
                StringBuilder json = new StringBuilder("{\"abilities\":[{\"name\":\"long\",\"actions\":[{\"actionType\":3,\"target\":1,\"modifierName\":\"m0\"}]}],\"modifiers\":[");
                const int length = 10000;
                for (int i = 0; i < length; i++)
                {
                    if (i != 0) json.Append(',');
                    json.Append("{\"name\":\"m").Append(i).Append('"');
                    if (i + 1 < length) json.Append(",\"onCreated\":[{\"actionType\":3,\"target\":5,\"modifierName\":\"m").Append(i + 1).Append("\"}]");
                    json.Append('}');
                }
                json.Append("]}");
                AbilitySandboxReport report = AbilitySandbox.Run(new[] { Json(json.ToString()) }, "long", 1);
                Require(!report.Success && !report.ExecutionAborted && report.ValidationMessages.Any(line => line.Contains("EDITOR_REFERENCE_COMPLEXITY")), "A long acyclic chain must return a preflight error without entering production graph validation or gameplay.");
                Require(report.Events.Count == 0, "An editor reference limit is a preflight decision, not a runtime budget stop.");
                Clean(report);
            });
            Case("custom script declarations never discover or execute project factories", () =>
            {
                AbilitySandboxReport report = AbilitySandbox.Run(new[] { Json("{\"abilities\":[{\"name\":\"custom\",\"requiresCustomScript\":true}]}") }, "custom", 1);
                Require(!report.Success && report.ValidationMessages.Any(line => line.Contains("Required ability script is not registered")), "The ordinary editor sandbox must reject missing custom factories.");
                Require(report.Events.Count == 0, "No skill should execute on failed publication.");
                Clean(report);
            });
            Case("missing ability and malformed definitions remain isolated", () =>
            {
                AbilitySandboxReport missing = AbilitySandbox.Run(new[] { Json("{}") }, "missing", 1);
                Require(!missing.Success && missing.Message.Contains("找不到技能"), "Missing name must have a clear result.");
                Clean(missing);
                AbilitySandboxReport malformed = AbilitySandbox.Run(new[] { Json("{\"abilities\":[{\"unknown\":1}]}") }, "missing", 1);
                Require(!malformed.Success && malformed.ValidationMessages.Count > 0, "Malformed data must not run.");
                Clean(malformed);
            });
            Case("simulation input limits reject unbounded or nonfinite work", () =>
            {
                foreach (AbilitySandboxOptions options in new[]
                {
                    new AbilitySandboxOptions { TargetCount = 129 }, new AbilitySandboxOptions { TargetCount = 0 },
                    new AbilitySandboxOptions { SimulationSeconds = 11 }, new AbilitySandboxOptions { SimulationSeconds = float.NaN },
                    new AbilitySandboxOptions { TickSeconds = 0 }, new AbilitySandboxOptions { TickSeconds = float.PositiveInfinity },
                    new AbilitySandboxOptions { MaxExecutionDepth = 129 }, new AbilitySandboxOptions { MaxExecutionOperations = 8193 }
                })
                {
                    AbilitySandboxReport report = AbilitySandbox.Run(new[] { Json("{}") }, "ignored", 1, options);
                    Require(!report.Success && !string.IsNullOrEmpty(report.Message) && report.Events.Count == 0, "Unsafe options must be rejected before execution.");
                    Clean(report);
                }
            });
            Case("repeat runs use fresh engines and do not retain runtime references", () =>
            {
                AbilitySandboxReport first = Hero("Warcraft3/mountain_king.json", "wc3.mountain_king.storm_bolt", 2, new AbilitySandboxOptions { TargetCount = 1, SimulationSeconds = 1f });
                AbilitySandboxReport second = Hero("Warcraft3/mountain_king.json", "wc3.mountain_king.storm_bolt", 2, new AbilitySandboxOptions { TargetCount = 1, SimulationSeconds = 1f });
                Completed(first); Completed(second);
                Equal(first.DamageTotal, 225f); Equal(first.DamageTotal, second.DamageTotal);
                Require(first.Events.SequenceEqual(second.Events), "Deterministic scene input must produce the same event log in a new engine.");
            });
            Console.WriteLine("Editor sandbox: " + passed + " passed, " + failed + " failed.");
            return failed;
        }

        private static string RecursiveDamage()
        {
            return "{\"abilities\":[{\"name\":\"probe\",\"behavior\":36,\"targetTeam\":2,\"aoeRadius\":{\"baseValue\":10},\"actions\":[{\"actionType\":3,\"target\":1,\"modifierName\":\"reflect\"},{\"actionType\":3,\"target\":3,\"modifierName\":\"reflect\"},{\"actionType\":1,\"target\":3,\"damageType\":3,\"value\":{\"baseValue\":1}}]}],\"modifiers\":[{\"name\":\"reflect\",\"duration\":-1,\"triggerEventType\":7,\"triggerActions\":[{\"actionType\":1,\"target\":6,\"damageType\":3,\"valueSource\":1,\"value\":{\"baseValue\":1}}]}]}";
        }

        private static AbilitySandboxReport Hero(string file, string ability, int level, AbilitySandboxOptions options)
        {
            return AbilitySandbox.Run(new[] { Fixture("Heroes/" + file) }, ability, level, options);
        }

        private static IAbilityDefinitionProvider Fixture(string relative)
        {
            string path = Path.Combine(FindRoot(), "Data", "AbilityJsonSources", relative.Replace('/', Path.DirectorySeparatorChar));
            return new JsonAbilityDefinitionProvider(File.ReadAllText(path), path);
        }

        private static IAbilityDefinitionProvider Json(string text) => new JsonAbilityDefinitionProvider(text, "sandbox-regression.json");

        private static string FindRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (DirectoryInfo directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                    if (Directory.Exists(Path.Combine(directory.FullName, "tools", "AbilityVerification", "Fixtures", "Data", "AbilityJsonSources", "Heroes"))) return Path.Combine(directory.FullName, "tools", "AbilityVerification", "Fixtures");
            throw new DirectoryNotFoundException("The sandbox fixtures require Data/AbilityJsonSources from the source verification bundle.");
        }

        private static void Completed(AbilitySandboxReport report)
        {
            Require(report.Success && report.CastSucceeded && !report.ExecutionAborted, report.Message + " " + string.Join("\n", report.ValidationMessages));
            Clean(report);
        }

        private static void BudgetAborted(AbilitySandboxReport report)
        {
            Require(!report.Success && report.ExecutionAborted && report.BudgetMessages.Count > 0, report.Message + " " + string.Join("\n", report.ValidationMessages));
            Require(report.Events.Count <= 512, "A problematic skill cannot allocate an unbounded editor log.");
            Clean(report);
        }

        private static void Clean(AbilitySandboxReport report)
        {
            Require(report.CleanupSucceeded && report.RemainingRuntimeObjects == 0, "Every return path must clear runtime objects and presentation handles.");
        }

        private static void Case(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS EditorSandbox " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL EditorSandbox " + name + ": " + error.Message); }
        }

        private static void Equal(float actual, float expected) => Require(Math.Abs(actual - expected) <= 0.001f, "Expected " + expected + ", actual " + actual);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}

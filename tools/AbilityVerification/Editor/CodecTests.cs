using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Ability.Configuration;
using Game.Ability.Editor.Core;

namespace Game.Ability.Editor.Verification
{
    public static class CodecTests
    {
        private static int passed;
        private static int failed;

        public static int Run()
        {
            passed = failed = 0;
            string root = FindSourceRoot();
            foreach (string file in Directory.GetFiles(Path.Combine(root, "Data", "AbilityJsonSources", "Heroes"), "*.json", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(root, "Data", "AbilityJsonSources", "Validation"), "*.json")).OrderBy(value => value, StringComparer.Ordinal))
            {
                string fixture = file;
                Case("real JSON semantic round trip: " + Path.GetFileName(file), () => FixtureRoundTrip(fixture));
            }
            Case("all public DTO fields survive export", EveryDtoFieldRoundTrips);
            Case("null duration inherits; empty duration remains explicit zero", DurationPresenceIsNotInvented);
            Case("defaults, false overrides and empty arrays survive", DefaultsAndEmptyArraysRoundTrip);
            Case("strings preserve Unicode, escaped controls and surrogate units", StringsRoundTrip);
            Case("writer is deterministic and keeps action/level order", OrderAndDeterminism);
            Case("float output is independent of current culture", CultureIndependentNumbers);
            Case("nonfinite floats cannot be exported", NonfiniteValuesAreRejected);
            Case("unknown, duplicate, malformed and future schema input is rejected", UnsupportedInputsAreRejected);
            Case("domain-invalid drafts remain editable", InvalidBusinessDraftsRemainReadable);
            Case("cloning detaches arrays and nested values", CloneDoesNotShareMutableData);
            Case("SHA-256 fingerprints exact UTF-8 content", HashIsStable);
            Case("null and unsupported exports fail explicitly", InvalidExportArguments);
            Console.WriteLine("Editor codec: " + passed + " passed, " + failed + " failed.");
            return failed;
        }

        private static void FixtureRoundTrip(string path)
        {
            string original = File.ReadAllText(path);
            JsonAbilityDocument first = Read(original);
            string canonical = AbilityJsonCodec.Write(first);
            JsonAbilityDocument second = Read(canonical);
            Equivalent(first, second, "$", false);
            Require(canonical == AbilityJsonCodec.Write(second), "Repeated export must produce identical bytes.");

            // Provider conversion gives an independent check of inherited durations, default
            // values and published definition numbers. Mixed sources intentionally lack their
            // external providers, so this compares bundles without pretending to ApplyTo them.
            AbilityDefinitionBundle before = new JsonAbilityDefinitionProvider(original, path).Load();
            AbilityDefinitionBundle after = new JsonAbilityDefinitionProvider(canonical, path).Load();
            Require(before.Validation.IsValid == after.Validation.IsValid, "Provider validation changed.");
            Equivalent(before.Abilities, after.Abilities, "abilities", true);
            Equivalent(before.Modifiers, after.Modifiers, "modifiers", true);
            Equivalent(before.Projectiles, after.Projectiles, "projectiles", true);
        }

        private static void EveryDtoFieldRoundTrips()
        {
            JsonAbilityDocument document = (JsonAbilityDocument)Populate(typeof(JsonAbilityDocument), "$", 0);
            document.schemaVersion = 1;
            Equivalent(document, Read(AbilityJsonCodec.Write(document)), "$", false);
        }

        private static void DurationPresenceIsNotInvented()
        {
            JsonAbilityDocument document = Read("{\"abilities\":[{\"actions\":[{\"actionType\":3,\"modifierName\":\"m\"},{\"actionType\":3,\"modifierName\":\"m\",\"duration\":{}},{\"actionType\":3,\"duration\":null}]}]}");
            JsonAbilityDocument copy = Read(AbilityJsonCodec.Write(document));
            Require(copy.abilities[0].actions[0].duration == null, "Omitted duration must continue to inherit.");
            Require(copy.abilities[0].actions[1].duration != null && copy.abilities[0].actions[1].duration.baseValue == 0f, "An explicit zero duration cannot become inherited.");
            Require(copy.abilities[0].actions[2].duration == null, "Null duration must continue to inherit.");
        }

        private static void DefaultsAndEmptyArraysRoundTrip()
        {
            JsonAbilityDocument source = Read("{\"abilities\":[{\"charges\":{\"startFull\":false,\"usesCooldown\":false},\"actions\":[]}],\"modifiers\":[{},{\"isPurgable\":false,\"removeOnDeath\":false,\"properties\":[]}],\"projectiles\":[{\"deleteOnHit\":false}]}");
            JsonAbilityDocument copy = Read(AbilityJsonCodec.Write(source));
            Equivalent(source, copy, "$", false);
            Require(copy.modifiers[0].isPurgable && copy.modifiers[0].removeOnDeath, "DTO true defaults must survive.");
            Require(!copy.modifiers[1].isPurgable && !copy.modifiers[1].removeOnDeath, "Explicit false cannot fall back to true.");
            Require(copy.abilities[0].actions != null && copy.abilities[0].actions.Length == 0, "An explicit empty array cannot become null.");
            Require(copy.projectiles[0].speed == 900f && copy.projectiles[0].radius == 96f, "Projectile defaults changed.");
        }

        private static void StringsRoundTrip()
        {
            string value = "中文 / fire \"quote\" \\ path\r\n\t\b\f\0\u001f 😀 \u2028\u2029\ud800";
            JsonAbilityDocument document = new JsonAbilityDocument { @namespace = value, abilities = new[] { new JsonAbility { name = value, description = value } } };
            string text = AbilityJsonCodec.Write(document);
            Require(!text.Contains("\0"), "A raw control character must never enter JSON.");
            Require(text.Contains("\\ud800"), "Unpaired surrogate must be escaped before UTF-8 encoding.");
            Require(Read(text).abilities[0].description == value, "String content was changed.");
            Require(Read(text).@namespace == value, "Namespace field did not use its JSON name.");
        }

        private static void OrderAndDeterminism()
        {
            JsonAbilityDocument document = new JsonAbilityDocument
            {
                abilities = new[] { new JsonAbility { name = "z", actions = new[] { new JsonAction { actionType = 2 }, new JsonAction { actionType = 1 }, new JsonAction { actionType = 3 } }, manaCost = new JsonLevelValue { values = new[] { 30f, 10f, 20f } } }, new JsonAbility { name = "a" } }
            };
            string first = AbilityJsonCodec.Write(document);
            JsonAbilityDocument copy = Read(first);
            Require(first == AbilityJsonCodec.Write(document) && first == AbilityJsonCodec.Write(copy), "Canonical output was unstable.");
            Require(copy.abilities[0].name == "z" && copy.abilities[1].name == "a", "Definition arrays must not be sorted.");
            Require(copy.abilities[0].actions.Select(value => value.actionType).SequenceEqual(new[] { 2, 1, 3 }), "Action execution order changed.");
            Require(copy.abilities[0].manaCost.values.SequenceEqual(new[] { 30f, 10f, 20f }), "Level values must not be sorted.");
            Require(first.EndsWith("\n", StringComparison.Ordinal) && !first.Contains("\r"), "Canonical files use one LF ending.");
        }

        private static void CultureIndependentNumbers()
        {
            JsonAbilityDocument document = new JsonAbilityDocument { abilities = new[] { new JsonAbility { castPoint = new JsonLevelValue { values = new[] { float.Epsilon, 1.2345678f, -0.125f, float.MaxValue } } } } };
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
                string expected = AbilityJsonCodec.Write(document);
                foreach (string culture in new[] { "fr-FR", "de-DE", "tr-TR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    Require(AbilityJsonCodec.Write(document) == expected, "Culture changed JSON output: " + culture);
                    Equivalent(document, Read(expected), "$", false);
                }
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        private static void NonfiniteValuesAreRejected()
        {
            foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                JsonAbilityDocument document = new JsonAbilityDocument { modifiers = new[] { new JsonModifier { levelProperties = new[] { new JsonModifierLevelProperty { property = 6, value = new JsonLevelValue { values = new[] { 1f, value } } } } } } };
                Expect<ArgumentException>(() => AbilityJsonCodec.Write(document));
            }
        }

        private static void UnsupportedInputsAreRejected()
        {
            foreach (string json in new[] { "{\"schemaVersion\":2}", "{\"schemaVersion\":0}", "{\"abilities\":[],\"abilitys\":[]}", "{\"abilities\":[],\"abilities\":[]}", "{\"abilities\":[{\"manaCost\":{\"baseValue\":1e100}}]}", "{\"abilities\":", "null", "" })
            {
                bool success = AbilityJsonCodec.TryRead(json, out JsonAbilityDocument document, out string error);
                Require(!success && document == null && !string.IsNullOrWhiteSpace(error), "Unsupported input must retain an explicit read-only error: " + json);
            }
        }

        private static void InvalidBusinessDraftsRemainReadable()
        {
            JsonAbilityDocument document = Read("{\"abilities\":[{\"name\":\"\",\"maxLevel\":-2}]}");
            Require(document.abilities[0].maxLevel == -2, "Draft values cannot be silently repaired.");
            AbilityDefinitionRegistry registry = new AbilityDefinitionRegistry();
            registry.LoadProvider(new JsonAbilityDefinitionProvider(AbilityJsonCodec.Write(document), "draft.json"));
            Require(!registry.IsValid, "Domain validation still belongs to the production registry.");
        }

        private static void CloneDoesNotShareMutableData()
        {
            JsonAbilityDocument original = Read("{\"abilities\":[{\"name\":\"source\",\"actions\":[{\"duration\":{\"values\":[1,2]}}]}],\"modifiers\":[{\"levelProperties\":[{\"property\":6,\"value\":{\"values\":[3,6]}}]}]}");
            JsonAbilityDocument copy = AbilityJsonCodec.Clone(original);
            copy.abilities[0].name = "copy";
            copy.abilities[0].actions[0].duration.values[0] = 999f;
            copy.modifiers[0].levelProperties[0].value.values[0] = 999f;
            Require(original.abilities[0].name == "source" && original.abilities[0].actions[0].duration.values[0] == 1f && original.modifiers[0].levelProperties[0].value.values[0] == 3f, "Clone shares mutable fields.");
        }

        private static void HashIsStable()
        {
            Require(AbilityJsonCodec.Hash("abc") == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "Hash is not SHA-256 of exact UTF-8 bytes.");
            Require(AbilityJsonCodec.Hash("x") != AbilityJsonCodec.Hash("x\n"), "Whitespace differences must be detected for disk conflicts.");
            Expect<ArgumentNullException>(() => AbilityJsonCodec.Hash(null));
        }

        private static void InvalidExportArguments()
        {
            Expect<ArgumentNullException>(() => AbilityJsonCodec.Write(null));
            Expect<ArgumentNullException>(() => AbilityJsonCodec.Clone(null));
            Expect<ArgumentException>(() => AbilityJsonCodec.Write(new JsonAbilityDocument { schemaVersion = 2 }));
        }

        private static object Populate(Type type, string path, int depth)
        {
            if (type == typeof(string)) return path + " 中文 \\\"\n";
            if (type == typeof(int)) return depth + 2;
            if (type == typeof(float)) return depth * 0.125f + 0.12345678f;
            if (type == typeof(bool)) return true;
            if (type.IsArray)
            {
                Array values = Array.CreateInstance(type.GetElementType(), 2);
                for (int i = 0; i < values.Length; i++) values.SetValue(Populate(type.GetElementType(), path + "[" + i + "]", depth + i + 1), i);
                return values;
            }
            object result = Activator.CreateInstance(type);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(result, field.FieldType == typeof(bool) ? !(bool)field.GetValue(result) : Populate(field.FieldType, path + "." + field.Name, depth + 1));
            return result;
        }

        private static void Equivalent(object expected, object actual, string path, bool includeRuntimeCollections)
        {
            if (ReferenceEquals(expected, actual)) return;
            Require(expected != null && actual != null && expected.GetType() == actual.GetType(), "Null/type mismatch at " + path);
            Type type = expected.GetType();
            if (type.IsPrimitive || type.IsEnum || expected is string) { Require(expected.Equals(actual), "Value changed at " + path); return; }
            if (includeRuntimeCollections && expected is IDictionary leftDictionary && actual is IDictionary rightDictionary)
            {
                Require(leftDictionary.Count == rightDictionary.Count, "Dictionary count changed at " + path);
                foreach (object key in leftDictionary.Keys) { Require(rightDictionary.Contains(key), "Missing key at " + path); Equivalent(leftDictionary[key], rightDictionary[key], path + "[" + key + "]", true); }
                return;
            }
            if (expected is IEnumerable left && actual is IEnumerable right)
            {
                IEnumerator first = left.GetEnumerator();
                IEnumerator second = right.GetEnumerator();
                int index = 0;
                while (true)
                {
                    bool a = first.MoveNext(); bool b = second.MoveNext();
                    Require(a == b, "Array/list count changed at " + path);
                    if (!a) break;
                    Equivalent(first.Current, second.Current, path + "[" + index++ + "]", includeRuntimeCollections);
                }
                return;
            }
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Equivalent(field.GetValue(expected), field.GetValue(actual), path + "." + field.Name, includeRuntimeCollections);
        }

        private static JsonAbilityDocument Read(string text)
        {
            if (!AbilityJsonCodec.TryRead(text, out JsonAbilityDocument document, out string error)) throw new Exception(error);
            return document;
        }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name + ".");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Case(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS codec: " + name); }
            catch (Exception exception) { failed++; Console.WriteLine("FAIL codec: " + name + "\n" + exception); }
        }

        private static string FindSourceRoot()
        {
            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                for (DirectoryInfo directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                    if (Directory.Exists(Path.Combine(directory.FullName, "tools", "AbilityVerification", "Fixtures", "Data", "AbilityJsonSources", "Heroes"))) return Path.Combine(directory.FullName, "tools", "AbilityVerification", "Fixtures");
            throw new DirectoryNotFoundException("Codec fixtures require Data/AbilityJsonSources/Heroes in the source verification directory.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Game.Ability.Configuration;
using Game.Ability.Editor.Core;

public static class WorkspaceTests
{
    private static int failures;
    private static int passed;

    public static int Run()
    {
        failures = 0;
        passed = 0;
        Check("loads and sorts only JSON documents", directory =>
        {
            File.WriteAllText(Path.Combine(directory, "z.json"), Ability("z"));
            File.WriteAllText(Path.Combine(directory, "a.json"), Ability("a"));
            File.WriteAllText(Path.Combine(directory, "notes.txt"), "not a provider");
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            Require(workspace.Documents.Count == 2 && workspace.Documents[0].FilePath.EndsWith("a.json"), "Documents are sorted deterministically.");
            Require(!workspace.Documents.Any(session => session.IsDirty), "Loading does not change formatting or mark drafts dirty.");
            Require(!workspace.Validate().Any(IsError), "Real providers validate the workspace.");
        });
        Check("new draft writes only on explicit save", directory =>
        {
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Create("nested/hero.json");
            Require(session.IsDirty && !File.Exists(session.FilePath), "Create is an in-memory operation.");
            session.Document.abilities = new[] { new JsonAbility { name = "hero.cast", id = "hero.cast" } };
            JsonAbilityDocument original = session.Document;
            Require(workspace.TrySave(session, out string error), error);
            Require(File.Exists(session.FilePath) && !session.IsDirty && ReferenceEquals(original, session.Document), "Successful save updates the baseline without replacing the displayed DTO.");
        });
        Check("existing document save replaces safely and clears dirty", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            File.WriteAllText(path, Ability("hero.cast"));
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Documents.Single();
            session.Document.abilities[0].displayName = "冰霜 · 试验";
            Require(session.IsDirty, "DTO edits are reflected in dirty state.");
            Require(workspace.TrySave(session, out string error), error);
            Require(File.ReadAllText(path).Contains("冰霜 · 试验") && !session.IsDirty, "The edited value is committed.");
            Require(Directory.GetFiles(directory).Length == 1, "No successful-save staging files remain.");
        });
        Check("unknown schema field remains read-only and unchanged", directory =>
        {
            string path = Path.Combine(directory, "future.json");
            const string source = "{\"abilities\":[],\"unknownFutureField\":7}";
            File.WriteAllText(path, source);
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Documents.Single();
            Require(session.IsReadOnly && session.Document == null && session.ReadError != null, "Unknown fields do not produce an editable lossy DTO.");
            Require(!workspace.TrySave(session, out _) && File.ReadAllText(path) == source, "Saving cannot overwrite unknown input.");
            Require(workspace.Validate().Any(IsError), "The read error is shown in the diagnostic list.");
        });
        Check("unsupported schema cannot be converted through SetDraft", directory =>
        {
            File.WriteAllText(Path.Combine(directory, "future.json"), "{\"schemaVersion\":2}");
            AbilityDocumentSession session = new AbilityWorkspace(directory).Documents.Single();
            Require(session.IsReadOnly && !session.SetDraft(Ability("a"), out _), "Read-only future versions require an explicit migration, not an Undo mutation.");
        });
        Check("invalid UTF8 is retained as a read error", directory =>
        {
            string path = Path.Combine(directory, "invalid.json");
            byte[] source = { 0xff, 0xfe, 0xfd };
            File.WriteAllBytes(path, source);
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            Require(workspace.Documents.Single().IsReadOnly && workspace.Validate().Any(IsError), "Encoding errors are diagnostics.");
            Require(workspace.Documents.Single().ExistedAtLoad && workspace.Documents.Single().LoadedBytesHash != null, "A failed decode retains source existence and byte identity for recovery conflict detection.");
            Require(!workspace.TrySave(workspace.Documents.Single(), out _) && File.ReadAllBytes(path).SequenceEqual(source), "Encoding errors cannot erase bytes.");
        });
        Check("external file change blocks overwrite and keeps draft", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            File.WriteAllText(path, Ability("a"));
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Documents.Single();
            session.Document.abilities[0].displayName = "unsaved";
            string external = Ability("externally.changed");
            File.WriteAllText(path, external);
            Require(!workspace.TrySave(session, out string error) && error.Contains("changed on disk"), "External content has priority over stale drafts.");
            Require(File.ReadAllText(path) == external && session.Document.abilities[0].displayName == "unsaved", "Both versions remain available.");
        });
        Check("byte baseline detects BOM-only changes", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            string source = Ability("a");
            File.WriteAllText(path, source, new UTF8Encoding(false));
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            workspace.Documents.Single().Document.abilities[0].displayName = "edit";
            File.WriteAllText(path, source, new UTF8Encoding(true));
            Require(!workspace.TrySave(workspace.Documents.Single(), out _), "Conflict hashes compare exact bytes, not decoded text.");
        });
        Check("recovery baseline exposes original BOM bytes and save transitions", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            string source = Ability("a");
            File.WriteAllText(path, source, new UTF8Encoding(true));
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Documents.Single();
            string loadedHash = session.LoadedBytesHash;
            Require(session.ExistedAtLoad && loadedHash == AbilityJsonCodec.Hash("\ufeff" + source), "Recovery baseline includes the original UTF-8 BOM bytes.");
            Require(session.SourceText == source && loadedHash != AbilityJsonCodec.Hash(session.SourceText), "Decoded source text must not stand in for byte identity.");
            string snapshot = session.DraftText;
            session.Document.abilities[0].displayName = "changed";
            Require(session.LoadedBytesHash == loadedHash, "Editing does not move the disk baseline.");
            Require(workspace.TrySave(session, out string error), error);
            Require(session.ExistedAtLoad && session.LoadedBytesHash == AbilityJsonCodec.Hash(File.ReadAllText(path)) && session.LoadedBytesHash != loadedHash, "Successful save updates the baseline to committed UTF-8 bytes.");
            string committedHash = session.LoadedBytesHash;
            Require(session.SetDraft(snapshot, out _) && session.IsDirty && session.LoadedBytesHash == committedHash, "Undo changes the draft without rewriting its saved baseline.");
        });
        Check("new recovery baseline distinguishes never-saved files", directory =>
        {
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Create("new.json");
            Require(!session.ExistedAtLoad && session.LoadedBytesHash == null, "A missing source has no loaded bytes hash.");
            Require(session.SetDraft(Ability("new.cast"), out _) && !session.ExistedAtLoad && session.LoadedBytesHash == null, "Restoring a new draft does not claim a source exists.");
            Require(workspace.TrySave(session, out string error), error);
            Require(session.ExistedAtLoad && session.LoadedBytesHash == AbilityJsonCodec.Hash(File.ReadAllText(session.FilePath)), "First successful save establishes both existence and exact bytes.");
        });
        Check("deleted source blocks unexpected resurrection", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            File.WriteAllText(path, Ability("a"));
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            File.Delete(path);
            Require(!workspace.TrySave(workspace.Documents.Single(), out _) && !File.Exists(path), "A source removed externally is a conflict.");
        });
        Check("new target created externally is not overwritten", directory =>
        {
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Create("new.json");
            string external = Ability("external");
            File.WriteAllText(session.FilePath, external);
            Require(!workspace.TrySave(session, out _) && File.ReadAllText(session.FilePath) == external, "Create cannot race an external file into overwrite.");
        });
        Check("semantic validation failure leaves original file intact", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            string source = Ability("a");
            File.WriteAllText(path, source);
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Documents.Single();
            session.Document.abilities[0].cooldown = new JsonLevelValue { baseValue = -2 };
            Require(workspace.Validate().Any(IsError), "The production graph validator sees the invalid cooldown.");
            Require(!workspace.TrySave(session, out _) && File.ReadAllText(path) == source, "An invalid draft remains in memory.");
        });
        Check("nonfinite authoring values become diagnostics without crashing", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            string source = Ability("a");
            File.WriteAllText(path, source);
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Documents.Single();
            session.Document.abilities[0].cooldown = new JsonLevelValue { baseValue = float.NaN };
            Require(session.IsDirty && workspace.Validate().Any(IsError), "Writer errors surface as draft diagnostics.");
            Require(!workspace.TrySave(session, out _) && File.ReadAllText(path) == source, "NaN cannot be exported.");
        });
        Check("read-only file blocks atomic replacement", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            string source = Ability("a");
            File.WriteAllText(path, source);
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            workspace.Documents.Single().Document.abilities[0].displayName = "edit";
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
            try
            {
                Require(workspace.Documents.Single().IsReadOnly && !workspace.TrySave(workspace.Documents.Single(), out _), "A read-only file cannot be bypassed with rename.");
                Require(File.ReadAllText(path) == source, "Original bytes remain intact.");
            }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
        });
        Check("dirty reload is refused", directory =>
        {
            File.WriteAllText(Path.Combine(directory, "hero.json"), Ability("a"));
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Documents.Single();
            session.Document.abilities[0].displayName = "unsaved";
            RequireThrows<InvalidOperationException>(() => workspace.Reload());
            Require(ReferenceEquals(session, workspace.Documents.Single()) && session.IsDirty, "Reload cannot discard the open draft.");
        });
        Check("Undo snapshots restore clean state without writing", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            string source = Ability("a");
            File.WriteAllText(path, source);
            AbilityDocumentSession session = new AbilityWorkspace(directory).Documents.Single();
            string snapshot = session.DraftText;
            session.Document.abilities[0].displayName = "edit";
            Require(session.IsDirty && session.SetDraft(snapshot, out _), "Restoring the canonical snapshot succeeds.");
            Require(!session.IsDirty && File.ReadAllText(path) == source, "Undo updates draft state only.");
            Require(!session.SetDraft("{\"bad\":0}", out _) && !session.IsDirty, "Invalid snapshots do not destroy the valid draft.");
        });
        Check("path traversal and duplicate creation are rejected", directory =>
        {
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            RequireThrows<ArgumentException>(() => workspace.Create("../escape.json"));
            RequireThrows<ArgumentException>(() => workspace.Create("..\\escape.json"));
            RequireThrows<ArgumentException>(() => workspace.Create(Path.Combine(directory, "absolute.json")));
            RequireThrows<ArgumentException>(() => workspace.Create("notes.txt"));
            workspace.Create("a.json");
            RequireThrows<IOException>(() => workspace.Create("a.json"));
        });
        Check("existing cross-file reference validates and saves", directory =>
        {
            File.WriteAllText(Path.Combine(directory, "buff.json"), "{\"modifiers\":[{\"id\":\"shared.buff\",\"name\":\"shared.buff\"}]}");
            File.WriteAllText(Path.Combine(directory, "hero.json"), Passive("shared.buff"));
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            Require(!workspace.Validate().Any(IsError), "Definitions link across selected files.");
            AbilityDocumentSession hero = workspace.Documents.Single(session => session.FilePath.EndsWith("hero.json"));
            hero.Document.abilities[0].displayName = "renamed display";
            Require(workspace.TrySave(hero, out string error), error);
        });
        Check("save cannot depend on an unsaved sibling", directory =>
        {
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession buff = workspace.Create("buff.json");
            buff.Document.modifiers = new[] { new JsonModifier { id = "shared.buff", name = "shared.buff" } };
            AbilityDocumentSession hero = workspace.Create("hero.json");
            Require(hero.SetDraft(Passive("shared.buff"), out _), "A new draft may be replaced by an Undo-compatible snapshot.");
            Require(!workspace.Validate().Any(IsError), "The in-memory workspace resolves both drafts.");
            Require(!workspace.TrySave(hero, out _) && !File.Exists(hero.FilePath), "An incomplete disk graph is not published.");
            Require(workspace.TrySave(buff, out string buffError), buffError);
            Require(workspace.TrySave(hero, out string heroError), heroError);
        });
        Check("unrelated invalid draft does not corrupt saved view", directory =>
        {
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession unrelated = workspace.Create("unrelated.json");
            unrelated.Document.abilities = new[] { new JsonAbility { name = "bad", cooldown = new JsonLevelValue { baseValue = -5 } } };
            AbilityDocumentSession valid = workspace.Create("valid.json");
            valid.Document.abilities = new[] { new JsonAbility { name = "valid" } };
            Require(workspace.Validate().Any(IsError), "The workspace still reports the invalid draft.");
            Require(workspace.TrySave(valid, out string error), error);
            Require(!File.Exists(unrelated.FilePath), "Only the explicitly saved candidate is written.");
        });
        Check("external read-only provider resolves explicit references", directory =>
        {
            File.WriteAllText(Path.Combine(directory, "hero.json"), Passive("shared.buff"));
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            Require(workspace.Validate().Any(IsError), "Missing external providers are diagnosed.");
            workspace.ExternalProviders.Add(new SharedProvider());
            Require(!workspace.Validate().Any(IsError), "The explicitly supplied read-only provider participates in validation.");
            Require(workspace.TrySave(workspace.Documents.Single(), out string error), error);
        });
        Check("custom script declaration is a warning without factory execution", directory =>
        {
            File.WriteAllText(Path.Combine(directory, "scripted.json"), "{\"abilities\":[{\"name\":\"scripted\",\"requiresCustomScript\":true}]}");
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            Require(!workspace.Validate().Any(IsError) && workspace.Validate().Any(issue => issue.Code == "EDITOR_SCRIPT"), "Validation does not instantiate business scripts.");
            Require(workspace.TrySave(workspace.Documents.Single(), out string error), error);
        });
        Check("I/O failure preserves draft and blocking file", directory =>
        {
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Create("nested/new.json");
            string blocker = Path.Combine(directory, "nested");
            File.WriteAllText(blocker, "existing user file");
            Require(!workspace.TrySave(session, out string error) && !string.IsNullOrEmpty(error), "A staging directory failure is reported.");
            Require(session.IsDirty && File.ReadAllText(blocker) == "existing user file" && !File.Exists(session.FilePath), "Failure preserves both user file and draft.");
        });
        Check("save revalidates external dependency changes against disk", directory =>
        {
            string buffPath = Path.Combine(directory, "buff.json");
            File.WriteAllText(buffPath, "{\"modifiers\":[{\"name\":\"shared.buff\"}]}");
            string heroPath = Path.Combine(directory, "hero.json");
            string original = Passive("shared.buff");
            File.WriteAllText(heroPath, original);
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession hero = workspace.Documents.Single(session => session.FilePath == heroPath);
            hero.Document.abilities[0].displayName = "edit";
            File.WriteAllText(buffPath, "{\"modifiers\":[{\"name\":\"external.rename\"}]}");
            Require(!workspace.TrySave(hero, out _), "A stale in-memory dependency index cannot authorize saving.");
            Require(File.ReadAllText(heroPath) == original && hero.IsDirty, "The candidate file remains intact.");
        });
        Check("all delivered hero files form a real editable workspace", directory =>
        {
            string heroes = FindHeroDirectory();
            foreach (string source in Directory.GetFiles(heroes, "*.json", SearchOption.AllDirectories))
            {
                string target = Path.Combine(directory, Path.GetRelativePath(heroes, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(source, target);
            }
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            Require(workspace.Documents.Count == 7 && !workspace.Validate().Any(IsError), "All seven production hero documents validate together.");
            AbilityDocumentSession session = workspace.Documents.First();
            session.Document.abilities[0].displayName += " · Editor roundtrip";
            Require(workspace.TrySave(session, out string error), error);
            Require(!workspace.Validate().Any(IsError), "A real hero definition remains valid after editing and saving.");
        });
        Check("oversized modifier reference chain cannot overwrite a valid file", directory =>
        {
            string path = Path.Combine(directory, "hero.json");
            string original = Ability("hero.cast");
            File.WriteAllText(path, original);
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Documents.Single();
            session.Document.modifiers = Enumerable.Range(0, 300).Select(index => new JsonModifier
            {
                id = "long.chain." + index,
                name = "long.chain." + index,
                onCreated = index < 299 ? new[] { new JsonAction { actionType = 3, target = 5, modifierName = "long.chain." + (index + 1) } } : null
            }).ToArray();
            Require(workspace.Validate().Any(issue => issue.Code == "EDITOR_REFERENCE_COMPLEXITY" && IsError(issue)), "Authoring complexity is rejected before production graph validation.");
            Require(!workspace.TrySave(session, out string error) && error.Contains("EDITOR_REFERENCE_COMPLEXITY"), "Saving enforces the same preflight.");
            Require(session.IsDirty && File.ReadAllText(path) == original, "The valid source and oversized draft remain intact.");
        });
        Check("external providers participate in preflight without a second Load", directory =>
        {
            AbilityWorkspace workspace = new AbilityWorkspace(directory);
            AbilityDocumentSession session = workspace.Create("hero.json");
            CountingChainProvider provider = new CountingChainProvider();
            workspace.ExternalProviders.Add(provider);
            Require(workspace.Validate().Any(issue => issue.Code == "EDITOR_REFERENCE_COMPLEXITY"), "External definitions count toward the authoring budget.");
            Require(provider.LoadCount == 1, "Preflight reuses registrations rather than executing a provider twice.");
            Require(!workspace.TrySave(session, out _) && provider.LoadCount == 2 && !File.Exists(session.FilePath), "One additional validation pass loads once and refuses a new file.");
        });
        Console.WriteLine("Workspace checks: " + passed + " passed, " + failures + " failed.");
        return failures;
    }

    private static void Check(string name, Action<string> test)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ability-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { test(directory); passed++; Console.WriteLine("PASS workspace: " + name); }
        catch (Exception exception) { failures++; Console.WriteLine("FAIL workspace: " + name + "\n" + exception); }
        finally { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private static string Ability(string name) => "{\"schemaVersion\":1,\"abilities\":[{\"id\":\"" + name + "\",\"name\":\"" + name + "\"}]}";
    private static string Passive(string modifier) => "{\"abilities\":[{\"id\":\"hero.passive\",\"name\":\"hero.passive\",\"behavior\":2,\"intrinsicModifierName\":\"" + modifier + "\"}]}";
    private static bool IsError(AbilityEditorDiagnostic issue) => issue.Severity == AbilityValidationSeverity.Error;
    private static string FindHeroDirectory()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (DirectoryInfo directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            {
                string path = Path.Combine(directory.FullName, "tools", "AbilityVerification", "Fixtures", "Data", "AbilityJsonSources", "Heroes");
                if (Directory.Exists(path)) return path;
            }
        throw new DirectoryNotFoundException("The source verification bundle must include Data/AbilityJsonSources/Heroes.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void RequireThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
    }

    private sealed class SharedProvider : IAbilityDefinitionProvider
    {
        public string ProviderName => "Explicit test index";
        public AbilityDefinitionSourceType SourceType => AbilityDefinitionSourceType.Code;
        public AbilityDefinitionBundle Load()
        {
            AbilityDefinitionBundle bundle = new AbilityDefinitionBundle();
            bundle.Modifiers.Add(new ModifierDefinitionRegistration { Definition = new Game.Ability.ModifierDefinition { Name = "shared.buff" }, Origin = new AbilityDefinitionOrigin { StableId = "shared.buff", SourcePath = "external-index" } });
            return bundle;
        }
    }

    private sealed class CountingChainProvider : IAbilityDefinitionProvider
    {
        public int LoadCount;
        public string ProviderName => "Explicit long-chain index";
        public AbilityDefinitionSourceType SourceType => AbilityDefinitionSourceType.Code;
        public AbilityDefinitionBundle Load()
        {
            LoadCount++;
            AbilityDefinitionBundle bundle = new AbilityDefinitionBundle();
            for (int index = 0; index < 300; index++)
            {
                Game.Ability.ModifierDefinition modifier = new Game.Ability.ModifierDefinition { Name = "external.chain." + index };
                if (index < 299) modifier.OnCreatedActions.Add(new Game.Ability.ActionDefinition { ActionType = Game.Ability.ActionType.AddModifier, Target = Game.Ability.ActionTarget.Parent, ModifierName = "external.chain." + (index + 1) });
                bundle.Modifiers.Add(new ModifierDefinitionRegistration { Definition = modifier, Origin = new AbilityDefinitionOrigin { StableId = modifier.Name, SourcePath = "external-long-chain-index" } });
            }
            return bundle;
        }
    }
}

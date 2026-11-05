using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game.Ability.Configuration;

namespace Game.Ability.Editor.Core
{
    public sealed class AbilityEditorDiagnostic
    {
        public AbilityValidationSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string FilePath { get; set; }
        public string ReferenceChain { get; set; }
    }

    /// <summary>A draft owns authoring DTOs only. The loaded bytes remain the conflict baseline.</summary>
    public sealed class AbilityDocumentSession
    {
        private string savedCanonical;
        private bool existedAtLoad;
        private string loadedBytesHash;

        internal AbilityDocumentSession(string filePath, byte[] bytes, string readError = null)
        {
            FilePath = filePath;
            existedAtLoad = bytes != null || File.Exists(filePath);
            loadedBytesHash = bytes != null ? AbilityWorkspace.HashBytes(bytes) : null;
            SourceText = string.Empty;
            ReadError = readError;
            try { if (bytes != null) SourceText = Decode(bytes); }
            catch (DecoderFallbackException exception) { ReadError = exception.Message; return; }
            if (ReadError == null && bytes != null)
            {
                if (!AbilityJsonCodec.TryRead(SourceText, out JsonAbilityDocument document, out string error)) ReadError = error;
                else
                {
                    Document = document;
                    savedCanonical = AbilityJsonCodec.Write(document);
                }
            }
        }

        internal AbilityDocumentSession(string filePath, JsonAbilityDocument document)
        {
            FilePath = filePath;
            SourceText = string.Empty;
            Document = document;
        }

        public string FilePath { get; }
        public JsonAbilityDocument Document { get; private set; }
        public string SourceText { get; private set; }
        public string ReadError { get; private set; }
        public string LoadedBytesHash => loadedBytesHash;
        public bool ExistedAtLoad => existedAtLoad;
        public string DraftText => Document != null ? AbilityJsonCodec.Write(Document) : SourceText;

        public bool IsReadOnly
        {
            get
            {
                if (Document == null || !string.IsNullOrEmpty(ReadError)) return true;
                try { return File.Exists(FilePath) && (File.GetAttributes(FilePath) & FileAttributes.ReadOnly) != 0; }
                catch (IOException) { return true; }
                catch (UnauthorizedAccessException) { return true; }
            }
        }

        public bool IsDirty
        {
            get
            {
                if (Document == null) return false;
                if (!existedAtLoad) return true;
                try { return !string.Equals(savedCanonical, AbilityJsonCodec.Write(Document), StringComparison.Ordinal); }
                catch (ArgumentException) { return true; }
                catch (InvalidOperationException) { return true; }
            }
        }

        /// <summary>Restores an in-memory Undo snapshot; never writes to disk or changes the baseline.</summary>
        public bool SetDraft(string json, out string error)
        {
            if (IsReadOnly)
            {
                error = "This document is read-only. Preserve its original text and resolve its read/schema error first.";
                return false;
            }
            if (!AbilityJsonCodec.TryRead(json, out JsonAbilityDocument document, out error)) return false;
            Document = document;
            return true;
        }

        internal bool MatchesDisk(out string error)
        {
            try
            {
                bool exists = File.Exists(FilePath);
                if (exists != existedAtLoad || exists && !string.Equals(HashBytesOnDisk(FilePath), loadedBytesHash, StringComparison.Ordinal))
                {
                    error = "The file changed on disk since it was loaded. Keep this draft, reload or compare the external version before saving.";
                    return false;
                }
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                error = "Cannot inspect the file's conflict baseline: " + exception.Message;
                return false;
            }
        }

        internal void MarkSaved(string text, byte[] bytes)
        {
            SourceText = text;
            loadedBytesHash = AbilityWorkspace.HashBytes(bytes);
            savedCanonical = text;
            existedAtLoad = true;
            ReadError = null;
        }

        internal static string Decode(byte[] bytes)
        {
            int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }

        private static string HashBytesOnDisk(string filePath) => AbilityWorkspace.HashBytes(File.ReadAllBytes(filePath));
    }

    /// <summary>
    /// Explicit skill-directory workspace. No scene/global services or arbitrary script factories run here.
    /// Reload refuses dirty sessions. Single-file saves validate against other files on disk, so they
    /// cannot accidentally depend on an unsaved sibling draft. Multi-file transactions are separate work.
    /// </summary>
    public sealed class AbilityWorkspace
    {
        private readonly List<AbilityDocumentSession> documents = new List<AbilityDocumentSession>();
        private readonly List<AbilityEditorDiagnostic> discoveryIssues = new List<AbilityEditorDiagnostic>();
        private static readonly StringComparison PathComparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private const long MaximumDocumentBytes = 8 * 1024 * 1024;

        public AbilityWorkspace(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("Select a skill directory.", nameof(rootDirectory));
            string fullRoot = Path.GetFullPath(rootDirectory);
            RootDirectory = string.Equals(fullRoot, Path.GetPathRoot(fullRoot), PathComparison) ? fullRoot : fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(RootDirectory)) throw new DirectoryNotFoundException("The skill directory does not exist: " + RootDirectory);
            EnsureNoLinks(RootDirectory);
            Reload();
        }

        public string RootDirectory { get; }
        public IReadOnlyList<AbilityDocumentSession> Documents => documents;
        public IList<IAbilityDefinitionProvider> ExternalProviders { get; } = new List<IAbilityDefinitionProvider>();

        /// <summary>Throws if any draft is dirty. The caller may explicitly discard by opening a new workspace.</summary>
        public void Reload()
        {
            if (documents.Any(document => document.IsDirty)) throw new InvalidOperationException("Reload would discard unsaved drafts. Save, export or explicitly discard them first.");
            List<AbilityEditorDiagnostic> issues = new List<AbilityEditorDiagnostic>();
            List<AbilityDocumentSession> loaded = new List<AbilityDocumentSession>();
            foreach (string path in EnumerateFiles(issues)) loaded.Add(Load(path));
            documents.Clear();
            documents.AddRange(loaded);
            discoveryIssues.Clear();
            discoveryIssues.AddRange(issues);
        }

        public AbilityDocumentSession Create(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) throw new ArgumentException("A relative .json path inside the selected directory is required.", nameof(relativePath));
            string filePath = ContainedPath(relativePath.Replace('\\', Path.DirectorySeparatorChar));
            if (!string.Equals(Path.GetExtension(filePath), ".json", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Skill documents must use the .json extension.", nameof(relativePath));
            EnsureNoLinks(Path.GetDirectoryName(filePath));
            if (File.Exists(filePath) || Directory.Exists(filePath) || documents.Any(document => string.Equals(document.FilePath, filePath, PathComparison)))
                throw new IOException("A document already exists at this path; creating a draft will not overwrite it.");
            AbilityDocumentSession session = new AbilityDocumentSession(filePath, new JsonAbilityDocument { @namespace = "skills" });
            documents.Add(session);
            documents.Sort((left, right) => StringComparer.Ordinal.Compare(left.FilePath, right.FilePath));
            return session;
        }

        public IReadOnlyList<AbilityEditorDiagnostic> Validate()
        {
            List<AbilityEditorDiagnostic> result = new List<AbilityEditorDiagnostic>(discoveryIssues);
            AbilityDefinitionRegistry registry = new AbilityDefinitionRegistry();
            foreach (AbilityDocumentSession session in documents)
            {
                if (!string.IsNullOrEmpty(session.ReadError))
                {
                    result.Add(Error("EDITOR_READ", session.ReadError, session.FilePath));
                    continue;
                }
                try { registry.LoadProvider(new JsonAbilityDefinitionProvider(session.DraftText, session.FilePath)); }
                catch (Exception exception) when (IsAuthoringException(exception)) { result.Add(Error("EDITOR_DRAFT", exception.Message, session.FilePath)); }
            }
            CompleteValidation(registry, result);
            return result;
        }

        public bool TrySave(AbilityDocumentSession session, out string error)
        {
            error = null;
            string temporary = null;
            string backup = null;
            bool saved = false;
            try
            {
                if (session == null || !documents.Contains(session)) { error = "The document does not belong to this workspace."; return false; }
                ContainedPath(Path.GetRelativePath(RootDirectory, session.FilePath));
                EnsureNoLinks(session.FilePath);
                if (session.IsReadOnly) { error = session.ReadError ?? "The document is read-only."; return false; }
                if (!session.MatchesDisk(out error)) return false;
                string text = session.DraftText;
                byte[] bytes = new UTF8Encoding(false, true).GetBytes(text);
                if (bytes.LongLength > MaximumDocumentBytes) { error = "The draft exceeds the editor's 8 MiB file limit."; return false; }
                Dictionary<string, string> diskHashes = new Dictionary<string, string>(StringComparer.Ordinal);
                IReadOnlyList<AbilityEditorDiagnostic> diagnostics = ValidateSavedView(session, text, diskHashes);
                AbilityEditorDiagnostic failure = diagnostics.FirstOrDefault(issue => issue.Severity == AbilityValidationSeverity.Error);
                if (failure != null)
                {
                    error = "Save blocked by " + failure.Code + ": " + failure.Message + "\n" + failure.FilePath + "\nSave referenced dependency drafts first, then retry.";
                    return false;
                }

                string directory = Path.GetDirectoryName(session.FilePath);
                Directory.CreateDirectory(directory);
                EnsureNoLinks(directory);
                temporary = Path.Combine(directory, "." + Path.GetFileName(session.FilePath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (!AbilityJsonCodec.TryRead(AbilityDocumentSession.Decode(File.ReadAllBytes(temporary)), out _, out string readError))
                    throw new IOException("The staged file could not be read back: " + readError);
                if (!session.MatchesDisk(out error)) return false;
                EnsureNoLinks(session.FilePath);
                if (session.IsReadOnly) { error = "The file became read-only before commit."; return false; }
                foreach (KeyValuePair<string, string> source in diskHashes)
                    if (!File.Exists(source.Key) || HashBytes(File.ReadAllBytes(source.Key)) != source.Value)
                    {
                        error = "A dependency changed on disk during validation: " + source.Key + ". Retry validation before saving.";
                        return false;
                    }

                if (File.Exists(session.FilePath))
                {
                    backup = Path.Combine(directory, "." + Path.GetFileName(session.FilePath) + "." + Guid.NewGuid().ToString("N") + ".bak");
                    File.Replace(temporary, session.FilePath, backup);
                }
                else File.Move(temporary, session.FilePath);
                temporary = null;
                session.MarkSaved(text, bytes);
                saved = true;
                return true;
            }
            catch (Exception exception) when (IsAuthoringException(exception) || exception is NotSupportedException)
            {
                error = "The file was not saved: " + exception.Message;
                return false;
            }
            finally
            {
                DeleteTemporary(temporary);
                if (saved) DeleteTemporary(backup);
                else if (backup != null && File.Exists(backup)) error += "\nThe original recovery copy is retained at: " + backup;
            }
        }

        private IReadOnlyList<AbilityEditorDiagnostic> ValidateSavedView(AbilityDocumentSession candidate, string text, IDictionary<string, string> diskHashes)
        {
            List<AbilityEditorDiagnostic> result = new List<AbilityEditorDiagnostic>();
            AbilityDefinitionRegistry registry = new AbilityDefinitionRegistry();
            foreach (string path in EnumerateFiles(result))
            {
                if (string.Equals(path, candidate.FilePath, PathComparison)) continue;
                try
                {
                    byte[] bytes = ReadBytes(path);
                    diskHashes.Add(path, HashBytes(bytes));
                    registry.LoadProvider(new JsonAbilityDefinitionProvider(AbilityDocumentSession.Decode(bytes), path));
                }
                catch (Exception exception) when (IsAuthoringException(exception)) { result.Add(Error("EDITOR_READ", exception.Message, path)); }
            }
            registry.LoadProvider(new JsonAbilityDefinitionProvider(text, candidate.FilePath));
            CompleteValidation(registry, result);
            return result;
        }

        private void CompleteValidation(AbilityDefinitionRegistry registry, ICollection<AbilityEditorDiagnostic> result)
        {
            foreach (IAbilityDefinitionProvider provider in ExternalProviders) registry.LoadProvider(provider);
            AbilityDefinitionBundle merged = new AbilityDefinitionBundle();
            merged.Abilities.AddRange(registry.Abilities.Values);
            merged.Modifiers.AddRange(registry.Modifiers.Values);
            merged.Projectiles.AddRange(registry.Projectiles.Values);
            IReadOnlyList<AbilityEditorDiagnostic> preflight = AbilityReferencePreflight.ValidateBundles(new[] { merged });
            foreach (AbilityEditorDiagnostic diagnostic in preflight) result.Add(diagnostic);
            if (preflight.Any(issue => issue.Severity == AbilityValidationSeverity.Error)) return;
            foreach (AbilityValidationIssue issue in registry.Validation.Issues)
                result.Add(new AbilityEditorDiagnostic { Severity = issue.Severity, Code = issue.Code, Message = issue.Message, FilePath = issue.Source?.Path, ReferenceChain = issue.ReferenceChain });
            foreach (AbilityDefinitionRegistration definition in registry.Abilities.Values)
                if (definition.RequiresCustomScript) result.Add(ScriptRequired(definition.Definition.Name, definition.Origin?.SourcePath));
            foreach (ModifierDefinitionRegistration definition in registry.Modifiers.Values)
                if (definition.RequiresCustomScript) result.Add(ScriptRequired(definition.Definition.Name, definition.Origin?.SourcePath));
        }

        private AbilityDocumentSession Load(string path)
        {
            try { return new AbilityDocumentSession(path, ReadBytes(path)); }
            catch (Exception exception) when (IsAuthoringException(exception)) { return new AbilityDocumentSession(path, null, exception.Message); }
        }

        private IEnumerable<string> EnumerateFiles(ICollection<AbilityEditorDiagnostic> issues)
        {
            List<string> files = new List<string>();
            Collect(RootDirectory, files, issues);
            files.Sort(StringComparer.Ordinal);
            return files;
        }

        private void Collect(string directory, ICollection<string> files, ICollection<AbilityEditorDiagnostic> issues)
        {
            try
            {
                foreach (string path in Directory.GetFileSystemEntries(directory).OrderBy(value => value, StringComparer.Ordinal))
                {
                    FileAttributes attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        issues.Add(new AbilityEditorDiagnostic { Severity = AbilityValidationSeverity.Warning, Code = "EDITOR_LINK", Message = "Linked files/directories are excluded from this workspace.", FilePath = path });
                        continue;
                    }
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal)) Collect(path, files, issues);
                    }
                    else if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)) files.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception exception) when (IsAuthoringException(exception)) { issues.Add(Error("EDITOR_DIRECTORY", exception.Message, directory)); }
        }

        private string ContainedPath(string relativePath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(RootDirectory, relativePath));
            string prefix = RootDirectory.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? RootDirectory : RootDirectory + Path.DirectorySeparatorChar;
            if (string.Equals(fullPath, RootDirectory, PathComparison) || !fullPath.StartsWith(prefix, PathComparison)) throw new ArgumentException("The document path must remain inside the selected skill directory.");
            return fullPath;
        }

        private static byte[] ReadBytes(string path)
        {
            if (new FileInfo(path).Length > MaximumDocumentBytes) throw new IOException("The document exceeds the editor's 8 MiB file limit.");
            return File.ReadAllBytes(path);
        }

        private static void EnsureNoLinks(string path)
        {
            for (string current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked paths are not supported for document writes: " + current);
        }

        internal static string HashBytes(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool IsAuthoringException(Exception exception) => exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is InvalidOperationException;
        private static AbilityEditorDiagnostic Error(string code, string message, string path) => new AbilityEditorDiagnostic { Severity = AbilityValidationSeverity.Error, Code = code, Message = message, FilePath = path };
        private static AbilityEditorDiagnostic ScriptRequired(string name, string path) => new AbilityEditorDiagnostic { Severity = AbilityValidationSeverity.Warning, Code = "EDITOR_SCRIPT", Message = "Business must register the declared custom script factory before execution: " + name, FilePath = path, ReferenceChain = name };

        private static void DeleteTemporary(string path)
        {
            if (path == null) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

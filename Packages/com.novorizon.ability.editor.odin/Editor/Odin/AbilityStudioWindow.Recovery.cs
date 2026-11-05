#if UNITY_EDITOR && ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Ability.Configuration;
using Game.Ability.Editor.Core;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Ability.Editor.Odin
{
    public sealed partial class AbilityStudioWindow
    {
        private void PersistRecovery()
        {
            if (workspace == null) return;
            List<RecoveryEntry> entries = new List<RecoveryEntry>(retainedRecovery.Values);
            foreach (AbilityDocumentSession document in workspace.Documents.Where(value => value.IsDirty && value.Document != null))
                try
                {
                    RecoveryEntry original = null;
                    bool hasOriginal = recoveryConflicts.Contains(document.FilePath) && recoveryOrigins.TryGetValue(document.FilePath, out original);
                    string sourceHash = hasOriginal ? original.sourceHash : document.LoadedBytesHash;
                    bool sourceExisted = hasOriginal ? original.sourceExisted : document.ExistedAtLoad;
                    entries.RemoveAll(value => value.path == document.FilePath);
                    entries.Add(new RecoveryEntry { path = document.FilePath, sourceHash = sourceHash, sourceExisted = sourceExisted, json = document.DraftText });
                }
                catch (ArgumentException) { }
            if (entries.Count == 0) SessionState.EraseString(RecoveryKey);
            else SessionState.SetString(RecoveryKey, JsonUtility.ToJson(new RecoveryData { root = workspace.RootDirectory, entries = entries.ToArray() }));
        }

        private void RestoreRecovery()
        {
            string text = SessionState.GetString(RecoveryKey, string.Empty);
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                RecoveryData data = JsonUtility.FromJson<RecoveryData>(text);
                if (data == null || !string.Equals(data.root, workspace.RootDirectory, StringComparison.OrdinalIgnoreCase)) return;
                foreach (RecoveryEntry entry in data.entries ?? Array.Empty<RecoveryEntry>())
                {
                    retainedRecovery[entry.path] = entry;
                    try
                    {
                        AbilityDocumentSession document = workspace.Documents.FirstOrDefault(value => value.FilePath == entry.path);
                        if (document == null) document = workspace.Create(Relative(workspace.RootDirectory, entry.path));
                        recoveryOrigins[document.FilePath] = entry;
                        if (document.ExistedAtLoad != entry.sourceExisted || document.LoadedBytesHash != entry.sourceHash) recoveryConflicts.Add(document.FilePath);
                        if (!document.SetDraft(entry.json, out string error)) { Notify("恢复草稿已保留，请另存副本：" + error, true); continue; }
                        retainedRecovery.Remove(entry.path);
                    }
                    catch (Exception exception) { Notify("恢复草稿已保留：" + exception.Message, true); }
                }
                hasUnsavedChanges = workspace.Documents.Any(value => value.IsDirty) || retainedRecovery.Count > 0;
            }
            catch (Exception exception) { Notify("草稿恢复未完成，原文件未改动：" + exception.Message, true); }
        }

    }
}
#endif

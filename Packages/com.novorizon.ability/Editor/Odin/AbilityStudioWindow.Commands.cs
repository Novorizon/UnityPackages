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
    /// <summary>技能编辑工作台对应的窗口。提供此模块的 Unity 编辑器窗口。</summary>
    public sealed partial class AbilityStudioWindow
    {
        /// <summary>选择工作区。</summary>
        private void ChooseWorkspace()
        {
            string folder = EditorUtility.OpenFolderPanel("选择技能 JSON 目录", workspace?.RootDirectory ?? Application.dataPath, string.Empty);
            if (string.IsNullOrEmpty(folder) || !ConfirmLeave()) return;
            LoadWorkspace(folder, false);
        }

        /// <summary>确认 Leave。</summary>
        private bool ConfirmLeave()
        {
            if (workspace == null || !workspace.Documents.Any(value => value.IsDirty) && retainedRecovery.Count == 0) return true;
            int choice = EditorUtility.DisplayDialogComplex("有未保存的技能草稿", "切换或刷新会替换当前技能库。请保存或明确放弃草稿。", "全部保存", "取消", "放弃草稿");
            if (choice == 1) return false;
            return choice == 2 || SaveAll();
        }

        /// <summary>加载工作区。</summary>
        private void LoadWorkspace(string folder, bool restoreRecovery)
        {
            try
            {
                AbilityWorkspace next = new AbilityWorkspace(folder);
                inspector?.Dispose();
                inspector = null;
                foreach (AbilityStudioDraft state in undoStates.Values)
                    if (state != null) DestroyImmediate(state);
                undoStates.Clear();
                recoveryConflicts.Clear();
                retainedRecovery.Clear();
                recoveryOrigins.Clear();
                workspace = next;
                EditorPrefs.SetString(SettingsKey, folder);
                selected = null;
                sandboxReport = null;
                notice = string.Empty;
                if (restoreRecovery) RestoreRecovery();
                else SessionState.EraseString(RecoveryKey);
                RefreshLibrary(true);
                hasUnsavedChanges = libraryFiles.Any(value => value.Dirty) || retainedRecovery.Count > 0;
                if (workspace.Documents.Count > 0) Select(workspace.Documents[0], EntryKind.Document);
                ValidateWorkspace();
            }
            catch (Exception exception) { Notify("无法打开技能库：" + exception.Message, true); }
        }

        /// <summary>刷新工作区。</summary>
        private void RefreshWorkspace()
        {
            if (workspace != null && ConfirmLeave()) LoadWorkspace(workspace.RootDirectory, false);
        }

        /// <summary>选择技能编辑工作台窗口。</summary>
        private void Select(AbilityDocumentSession document, EntryKind kind, int index = 0)
        {
            selected = document;
            selectedKind = kind;
            selectedIndex = index;
            inspectorScroll = Vector2.zero;
            jsonScroll = Vector2.zero;
            notice = string.Empty;
            if (kind == EntryKind.Ability && SelectedObject() is JsonAbility ability)
            {
                sandboxAbility = ability.name;
                sandboxLevel = Math.Max(1, Math.Min(sandboxLevel, ability.maxLevel));
            }
            RebuildInspector();
        }

        /// <summary>重建检视面板。</summary>
        private void RebuildInspector()
        {
            inspector?.Dispose();
            inspector = null;
            NormalizeSelection();
            if (selected?.Document != null)
            {
                inspector = new AbilityInspectorPanel();
                inspector.SetTarget(SelectedObject(), ReferenceOptions);
                StateFor(selected);
            }
            UpdateJsonPreview();
        }

        /// <summary>根据传入参数取得状态 For。</summary>
        private AbilityStudioDraft StateFor(AbilityDocumentSession document)
        {
            if (!undoStates.TryGetValue(document.FilePath, out AbilityStudioDraft state) || state == null)
            {
                state = CreateInstance<AbilityStudioDraft>();
                state.hideFlags = HideFlags.HideAndDontSave;
                state.Json = document.DraftText;
                undoStates[document.FilePath] = state;
            }
            return state;
        }

        /// <summary>采集变更。</summary>
        private void CaptureChanges(AbilityStudioDraft state)
        {
            try
            {
                state.Json = selected.DraftText;
                EditorUtility.SetDirty(state);
                DraftChanged();
            }
            catch (Exception exception)
            {
                selected.SetDraft(state.Json, out _);
                RebuildInspector();
                Notify("该修改无法导出，已恢复上一个有效草稿：" + exception.Message, true);
            }
        }

        /// <summary>调用 StateFor 完成Mutate。</summary>
        private void Mutate(string label, Action action)
        {
            if (selected == null || selected.IsReadOnly) return;
            AbilityStudioDraft state = StateFor(selected);
            Undo.RecordObject(state, label);
            action();
            CaptureChanges(state);
            RebuildInspector();
        }

        /// <summary>调用 RefreshLibrary 完成草稿变化。</summary>
        private void DraftChanged()
        {
            validationAt = EditorApplication.timeSinceStartup + 0.35;
            RefreshLibrary(false);
            UpdateJsonPreview();
            hasUnsavedChanges = libraryFiles.Any(value => value.Dirty) || retainedRecovery.Count > 0;
            saveChangesMessage = "技能工作台中有未保存的 JSON 草稿。";
            PersistRecovery();
            Repaint();
        }

        /// <summary>响应 UndoRedo。</summary>
        private void OnUndoRedo()
        {
            if (workspace == null) return;
            foreach (AbilityDocumentSession document in workspace.Documents)
                if (undoStates.TryGetValue(document.FilePath, out AbilityStudioDraft state) && state != null && document.Document != null && !string.Equals(document.DraftText, state.Json, StringComparison.Ordinal))
                    document.SetDraft(state.Json, out _);
            RefreshLibrary(true);
            RebuildInspector();
            DraftChanged();
        }

        /// <summary>响应编辑器更新。</summary>
        private void OnEditorUpdate()
        {
            if (validationAt >= 0 && EditorApplication.timeSinceStartup >= validationAt)
            {
                validationAt = -1;
                ValidateWorkspace();
                Repaint();
            }
        }

        /// <summary>响应 Play Mode 变化。</summary>
        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            PersistRecovery();
            sandboxReport = null;
        }

        /// <summary>验证工作区。</summary>
        private void ValidateWorkspace()
        {
            try { diagnostics = workspace?.Validate() ?? Array.Empty<AbilityEditorDiagnostic>(); }
            catch (Exception exception) { Notify("校验未完成：" + exception.Message, true); }
            validationAt = -1;
            Repaint();
        }

        /// <summary>保存当前。</summary>
        private void SaveCurrent()
        {
            if (selected == null || workspace == null) return;
            if (recoveryConflicts.Contains(selected.FilePath)) { Notify("请先处理恢复草稿与磁盘的冲突，或另存副本。", true); return; }
            if (!workspace.TrySave(selected, out string error)) { Notify("保存未完成：" + error, true); ValidateWorkspace(); return; }
            if (undoStates.TryGetValue(selected.FilePath, out AbilityStudioDraft state)) state.Json = selected.DraftText;
            AssetDatabase.Refresh();
            Notify("已保存 " + Path.GetFileName(selected.FilePath), false);
            DraftChanged();
            ValidateWorkspace();
        }

        /// <summary>保存全部。</summary>
        private bool SaveAll()
        {
            if (workspace == null) return true;
            if (retainedRecovery.Count > 0) { Notify("有尚未恢复的草稿，请先另存恢复副本或明确放弃。", true); return false; }
            List<AbilityDocumentSession> remaining = workspace.Documents.Where(value => value.IsDirty).ToList();
            string error = string.Empty;
            while (remaining.Count > 0)
            {
                bool progress = false;
                foreach (AbilityDocumentSession document in remaining.ToArray())
                {
                    if (recoveryConflicts.Contains(document.FilePath)) { error = "恢复草稿存在磁盘冲突。"; continue; }
                    if (!workspace.TrySave(document, out string failure)) { error = failure; continue; }
                    remaining.Remove(document);
                    progress = true;
                }
                if (!progress) { Notify("部分文件未保存，请先修正引用或冲突：" + error, true); RefreshLibrary(true); DraftChanged(); return false; }
            }
            AssetDatabase.Refresh();
            RefreshLibrary(true);
            DraftChanged();
            return true;
        }

        /// <summary>保存变更。</summary>
        public override void SaveChanges()
        {
            if (SaveAll()) base.SaveChanges();
        }

        /// <summary>调用 LoadWorkspace 完成Discard 变更。</summary>
        public override void DiscardChanges()
        {
            if (workspace != null) LoadWorkspace(workspace.RootDirectory, false);
            base.DiscardChanges();
        }

        /// <summary>创建文档。</summary>
        private void CreateDocument()
        {
            if (workspace == null) return;
            string file = EditorUtility.SaveFilePanel("新建技能 JSON", workspace.RootDirectory, "new_skills", "json");
            if (string.IsNullOrEmpty(file)) return;
            try
            {
                AbilityDocumentSession document = workspace.Create(Relative(workspace.RootDirectory, file));
                Select(document, EntryKind.Document);
                DraftChanged();
            }
            catch (Exception exception) { Notify("无法创建配置：" + exception.Message, true); }
        }

        /// <summary>添加条目。</summary>
        private void AddEntry(EntryKind kind)
        {
            Mutate("新增配置条目", () =>
            {
                string name = UniqueName((selected.Document.@namespace ?? "my") + "." + kind.ToString().ToLowerInvariant());
                if (kind == EntryKind.Ability)
                {
                    selected.Document.abilities = Append(selected.Document.abilities, new JsonAbility { id = name, name = name, displayName = "新技能" });
                    selectedIndex = selected.Document.abilities.Length - 1;
                }
                else if (kind == EntryKind.Modifier)
                {
                    selected.Document.modifiers = Append(selected.Document.modifiers, new JsonModifier { id = name, name = name, displayName = "新效果", duration = 5f });
                    selectedIndex = selected.Document.modifiers.Length - 1;
                }
                else
                {
                    selected.Document.projectiles = Append(selected.Document.projectiles, new JsonProjectile { id = name, name = name });
                    selectedIndex = selected.Document.projectiles.Length - 1;
                }
                selectedKind = kind;
            });
        }

        /// <summary>调用 Mutate 完成重复项条目。</summary>
        private void DuplicateEntry()
        {
            Mutate("复制配置条目", () =>
            {
                JsonAbilityDocument clone = AbilityJsonCodec.Clone(selected.Document);
                string name = UniqueName(SelectedName() + "_copy");
                if (selectedKind == EntryKind.Ability)
                {
                    JsonAbility copy = clone.abilities[selectedIndex];
                    copy.id = copy.name = name;
                    copy.displayName = (copy.displayName ?? copy.name) + " 副本";
                    selected.Document.abilities = Append(selected.Document.abilities, copy);
                    selectedIndex = selected.Document.abilities.Length - 1;
                }
                else if (selectedKind == EntryKind.Modifier)
                {
                    JsonModifier copy = clone.modifiers[selectedIndex];
                    copy.id = copy.name = name;
                    copy.displayName = (copy.displayName ?? copy.name) + " 副本";
                    selected.Document.modifiers = Append(selected.Document.modifiers, copy);
                    selectedIndex = selected.Document.modifiers.Length - 1;
                }
                else if (selectedKind == EntryKind.Projectile)
                {
                    JsonProjectile copy = clone.projectiles[selectedIndex];
                    copy.id = copy.name = name;
                    selected.Document.projectiles = Append(selected.Document.projectiles, copy);
                    selectedIndex = selected.Document.projectiles.Length - 1;
                }
            });
        }

        /// <summary>删除条目。</summary>
        private void DeleteEntry()
        {
            if (!EditorUtility.DisplayDialog("删除配置条目", "从当前草稿移除「" + SelectedTitle() + "」？仍然引用它的配置会显示校验错误。", "删除", "取消")) return;
            Mutate("删除配置条目", () =>
            {
                if (selectedKind == EntryKind.Ability) selected.Document.abilities = RemoveAt(selected.Document.abilities, selectedIndex);
                else if (selectedKind == EntryKind.Modifier) selected.Document.modifiers = RemoveAt(selected.Document.modifiers, selectedIndex);
                else if (selectedKind == EntryKind.Projectile) selected.Document.projectiles = RemoveAt(selected.Document.projectiles, selectedIndex);
                selectedKind = EntryKind.Document;
            });
        }

        /// <summary>保存 RecoveredCopy。</summary>
        private void SaveRecoveredCopy()
        {
            if (selected == null) return;
            string originalPath = selected.FilePath;
            string text = retainedRecovery.TryGetValue(originalPath, out RecoveryEntry recovery) ? recovery.json : selected.Document != null ? selected.DraftText : null;
            if (text == null) { Notify("没有可导出的恢复草稿。", true); return; }
            string path = EditorUtility.SaveFilePanel("另存恢复草稿", workspace.RootDirectory, Path.GetFileNameWithoutExtension(selected.FilePath) + "_recovered", "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                AbilityDocumentSession copy = workspace.Create(Relative(workspace.RootDirectory, path));
                if (!copy.SetDraft(text, out string error)) { Notify("恢复副本仍无法读取：" + error, true); return; }
                retainedRecovery.Remove(originalPath);
                Select(copy, EntryKind.Document);
                DraftChanged();
                Notify("已建立恢复副本草稿。请修改重复的名称和 ID 后保存。", false);
            }
            catch (Exception exception) { Notify(exception.Message, true); }
        }

    }
}
#endif

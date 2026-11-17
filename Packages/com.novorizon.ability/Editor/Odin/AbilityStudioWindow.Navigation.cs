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
        /// <summary>根据传入参数取得引用选项。</summary>
        private IEnumerable<string> ReferenceOptions(string kind)
        {
            if (workspace == null) return Array.Empty<string>();
            IEnumerable<JsonAbilityDocument> data = workspace.Documents.Where(value => value.Document != null).Select(value => value.Document);
            if (kind == "modifier") return data.SelectMany(value => Items(value.modifiers)).Select(value => value.name).Where(value => !string.IsNullOrEmpty(value)).Distinct();
            if (kind == "projectile") return data.SelectMany(value => Items(value.projectiles)).Select(value => value.name).Where(value => !string.IsNullOrEmpty(value)).Distinct();
            if (kind == "special" && SelectedObject() is JsonAbility ability) return Items(ability.specialValues).Select(value => value.name);
            return Array.Empty<string>();
        }

        /// <summary>调用 workspace.Documents.Where 完成Navigate 引用。</summary>
        private void NavigateReference(string kind, string name)
        {
            if (workspace == null) return;
            foreach (AbilityDocumentSession document in workspace.Documents.Where(value => value.Document != null))
            {
                if (kind == "modifier")
                {
                    int index = Array.FindIndex(document.Document.modifiers ?? Array.Empty<JsonModifier>(), value => value != null && value.name == name);
                    if (index >= 0) { Select(document, EntryKind.Modifier, index); centerTab = 0; return; }
                }
                if (kind == "projectile")
                {
                    int index = Array.FindIndex(document.Document.projectiles ?? Array.Empty<JsonProjectile>(), value => value != null && value.name == name);
                    if (index >= 0) { Select(document, EntryKind.Projectile, index); centerTab = 0; return; }
                }
            }
            Notify(kind == "special" ? "Special 属于当前技能，请在配置的特殊值分组中编辑。" : "未找到引用的定义，请检查名称和技能库范围。", true);
        }

        /// <summary>调用 .Documents.FirstOrDefault 完成Navigate 诊断。</summary>
        private void NavigateDiagnostic(AbilityEditorDiagnostic diagnostic)
        {
            AbilityDocumentSession document = workspace?.Documents.FirstOrDefault(value => string.Equals(value.FilePath, diagnostic.FilePath, StringComparison.OrdinalIgnoreCase));
            if (document == null) return;
            Select(document, EntryKind.Document);
            string chain = diagnostic.ReferenceChain ?? string.Empty;
            for (int i = 0; i < (document.Document?.abilities?.Length ?? 0); i++)
                if (!string.IsNullOrEmpty(document.Document.abilities[i]?.name) && chain.Contains(document.Document.abilities[i].name)) { Select(document, EntryKind.Ability, i); return; }
            for (int i = 0; i < (document.Document?.modifiers?.Length ?? 0); i++)
                if (!string.IsNullOrEmpty(document.Document.modifiers[i]?.name) && chain.Contains(document.Document.modifiers[i].name)) { Select(document, EntryKind.Modifier, i); return; }
            for (int i = 0; i < (document.Document?.projectiles?.Length ?? 0); i++)
                if (!string.IsNullOrEmpty(document.Document.projectiles[i]?.name) && chain.Contains(document.Document.projectiles[i].name)) { Select(document, EntryKind.Projectile, i); return; }
        }

        /// <summary>更新 JSON 预览。</summary>
        private void UpdateJsonPreview()
        {
            try { cachedJson = selected?.DraftText ?? string.Empty; }
            catch (Exception exception) { cachedJson = "当前草稿不能导出：" + exception.Message; }
        }

        /// <summary>取得已选对象。</summary>
        private object SelectedObject()
        {
            JsonAbilityDocument document = selected?.Document;
            if (document == null) return null;
            if (selectedKind == EntryKind.Ability && selectedIndex >= 0 && selectedIndex < (document.abilities?.Length ?? 0)) return document.abilities[selectedIndex];
            if (selectedKind == EntryKind.Modifier && selectedIndex >= 0 && selectedIndex < (document.modifiers?.Length ?? 0)) return document.modifiers[selectedIndex];
            if (selectedKind == EntryKind.Projectile && selectedIndex >= 0 && selectedIndex < (document.projectiles?.Length ?? 0)) return document.projectiles[selectedIndex];
            return document;
        }

        /// <summary>取得已选标题。</summary>
        private string SelectedTitle()
        {
            object item = SelectedObject();
            if (item is JsonAbility ability) return string.IsNullOrEmpty(ability.displayName) ? ability.name : ability.displayName;
            if (item is JsonModifier modifier) return string.IsNullOrEmpty(modifier.displayName) ? modifier.name : modifier.displayName;
            if (item is JsonProjectile projectile) return projectile.name;
            return Path.GetFileNameWithoutExtension(selected.FilePath);
        }

        /// <summary>取得已选名称。</summary>
        private string SelectedName()
        {
            object item = SelectedObject();
            if (item is JsonAbility ability) return ability.name;
            if (item is JsonModifier modifier) return modifier.name;
            if (item is JsonProjectile projectile) return projectile.name;
            return selected?.Document?.@namespace ?? "JSON DOCUMENT";
        }

        /// <summary>根据传入参数取得唯一名称。</summary>
        private string UniqueName(string basis)
        {
            HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
            foreach (AbilityDocumentSession document in workspace.Documents.Where(value => value.Document != null))
            {
                foreach (JsonAbility value in Items(document.Document.abilities)) { used.Add(value.name); used.Add(value.id); }
                foreach (JsonModifier value in Items(document.Document.modifiers)) { used.Add(value.name); used.Add(value.id); }
                foreach (JsonProjectile value in Items(document.Document.projectiles)) { used.Add(value.name); used.Add(value.id); }
            }
            string name = basis;
            int suffix = 2;
            while (used.Contains(name)) name = basis + "_" + suffix++;
            return name;
        }

    }
}
#endif

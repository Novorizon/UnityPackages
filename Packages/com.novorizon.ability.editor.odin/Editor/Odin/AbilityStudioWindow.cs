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
    /// <summary>A project-independent JSON workspace. Scene objects and business scripts never enter this window.</summary>
    public sealed partial class AbilityStudioWindow : OdinEditorWindow
    {
        private enum EntryKind { Document, Ability, Modifier, Projectile }
        private AbilityWorkspace workspace;
        private AbilityDocumentSession selected;
        private EntryKind selectedKind;
        private int selectedIndex;
        private AbilityInspectorPanel inspector;
        private AbilityStudioTheme theme;
        private readonly Dictionary<string, AbilityStudioDraft> undoStates = new Dictionary<string, AbilityStudioDraft>(StringComparer.Ordinal);
        private readonly HashSet<string> closedFiles = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> recoveryConflicts = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, RecoveryEntry> retainedRecovery = new Dictionary<string, RecoveryEntry>(StringComparer.Ordinal);
        private readonly Dictionary<string, RecoveryEntry> recoveryOrigins = new Dictionary<string, RecoveryEntry>(StringComparer.Ordinal);
        private readonly List<LibraryFile> libraryFiles = new List<LibraryFile>();
        private int totalAbilities, totalModifiers, totalProjectiles;
        private string[] sandboxNames = Array.Empty<string>();
        private IReadOnlyList<AbilityEditorDiagnostic> diagnostics = Array.Empty<AbilityEditorDiagnostic>();
        private string search = string.Empty, notice = string.Empty, cachedJson = string.Empty;
        private bool noticeIsError, initialized;
        private double validationAt = -1;
        private int centerTab, toolTab;
        private Vector2 libraryScroll, inspectorScroll, toolScroll, jsonScroll;
        private string sandboxAbility = string.Empty;
        private int sandboxLevel = 1;
        private AbilitySandboxOptions sandboxOptions = new AbilitySandboxOptions();
        private AbilitySandboxReport sandboxReport;

        private string SettingsKey => "AbilityStudio.Workspace." + Application.dataPath;
        private string RecoveryKey => "AbilityStudio.Recovery." + Application.dataPath;

        [MenuItem("Tools/Ability/Skill Studio", priority = 100)]
        public static void Open()
        {
            AbilityStudioWindow window = GetWindow<AbilityStudioWindow>();
            window.titleContent = new GUIContent("技能工作台");
            window.minSize = new Vector2(1040, 660);
            window.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            initialized = false;
            Undo.undoRedoPerformed += OnUndoRedo;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        protected override void OnDisable()
        {
            PersistRecovery();
            Undo.undoRedoPerformed -= OnUndoRedo;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            inspector?.Dispose();
            inspector = null;
            foreach (AbilityStudioDraft state in undoStates.Values)
                if (state != null) { Undo.ClearUndo(state); DestroyImmediate(state); }
            undoStates.Clear();
            theme?.Dispose();
            theme = null;
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            foreach (AbilityStudioDraft state in undoStates.Values)
                if (state != null) DestroyImmediate(state);
            undoStates.Clear();
            base.OnDestroy();
        }

        protected override void DrawEditors()
        {
            EnsureInitialized();
            if (theme == null || theme.Dark != EditorGUIUtility.isProSkin)
            {
                theme?.Dispose();
                theme = new AbilityStudioTheme();
            }
            Rect background = new Rect(0, 0, position.width, position.height);
            EditorGUI.DrawRect(background, theme.Background);
            DrawHeader();
            DrawWorkspaceBar();
            theme.Rule();
            float bodyHeight = Mathf.Max(330, position.height - 126);
            EditorGUILayout.BeginHorizontal(GUILayout.Height(bodyHeight));
            GUILayout.Space(14);
            DrawLibrary(232, bodyHeight);
            GUILayout.Space(12);
            DrawCenter(bodyHeight);
            GUILayout.Space(12);
            DrawTools(294, bodyHeight);
            GUILayout.Space(14);
            EditorGUILayout.EndHorizontal();
            DrawFooter();
            HandleShortcuts();
        }

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            string folder = EditorPrefs.GetString(SettingsKey, string.Empty);
            if (!Directory.Exists(folder))
            {
                string project = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
                folder = Path.Combine(project, "Data", "AbilityJsonSources", "Heroes");
            }
            if (Directory.Exists(folder)) LoadWorkspace(folder, true);
        }

        private void DrawHeader()
        {
            GUILayout.Space(10);
            EditorGUILayout.BeginHorizontal(GUILayout.Height(47));
            GUILayout.Space(16);
            Rect logo = GUILayoutUtility.GetRect(36, 36, GUILayout.Width(36));
            EditorGUI.DrawRect(logo, theme.Raised);
            GUIStyle monogram = new GUIStyle(theme.Title) { alignment = TextAnchor.MiddleCenter };
            monogram.normal.textColor = theme.Accent;
            GUI.Label(logo, "A", monogram);
            GUILayout.Space(12);
            EditorGUILayout.BeginVertical();
            GUILayout.Label("技能工作台", theme.Title);
            GUILayout.Label("ABILITY STUDIO  /  技能 · 效果 · 弹道", theme.Eyebrow);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("选择技能库", theme.Button, GUILayout.Width(106))) ChooseWorkspace();
            using (new EditorGUI.DisabledScope(workspace == null))
            {
                if (GUILayout.Button("新建 JSON", theme.Button, GUILayout.Width(98))) CreateDocument();
                if (GUILayout.Button("校验全部", theme.Button, GUILayout.Width(88))) ValidateWorkspace();
            }
            using (new EditorGUI.DisabledScope(selected == null || selected.IsReadOnly || !CachedDirty(selected)))
                if (GUILayout.Button("保存", theme.PrimaryButton, GUILayout.Width(76))) SaveCurrent();
            GUILayout.Space(14);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawWorkspaceBar()
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(29));
            GUILayout.Space(17);
            GUILayout.Label(workspace != null ? workspace.RootDirectory : "选择一个只包含技能 JSON 的数据目录", theme.Small);
            GUILayout.FlexibleSpace();
            if (selected != null)
                theme.Badge(selected.IsReadOnly ? "只读" : CachedDirty(selected) ? "未保存" : "已保存", selected.IsReadOnly ? theme.Warning : CachedDirty(selected) ? theme.Warning : theme.Accent);
            if (workspace != null && GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(46))) RefreshWorkspace();
            GUILayout.Space(14);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLibrary(float width, float height)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(height));
            GUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("技能库", theme.Heading);
            GUILayout.FlexibleSpace();
            GUILayout.Label(workspace != null ? workspace.Documents.Count + " 个文件" : "未选择", theme.Small);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4);
            search = EditorGUILayout.TextField(search, theme.Search);
            libraryScroll = EditorGUILayout.BeginScrollView(libraryScroll);
            if (workspace == null || workspace.Documents.Count == 0)
            {
                GUILayout.Space(22);
                GUILayout.Label("还没有配置文件", theme.Heading);
                GUILayout.Label("选择已有技能目录，或创建第一份 JSON。", theme.Small);
            }
            else foreach (LibraryFile document in libraryFiles) DrawLibraryDocument(document);
            EditorGUILayout.EndScrollView();
            GUILayout.Space(6);
            GUILayout.Label("独立条目 · 名称引用 · 按需展开", theme.Small);
            EditorGUILayout.EndVertical();
        }

        private void DrawLibraryDocument(LibraryFile file)
        {
            AbilityDocumentSession document = file.Session;
            bool fileMatch = Matches(Path.GetFileName(document.FilePath));
            bool anyChild = file.Entries.Any(value => Matches(value.Name) || Matches(value.Id) || Matches(value.Label));
            if (!fileMatch && !anyChild) return;
            EditorGUILayout.BeginHorizontal();
            bool expanded = !closedFiles.Contains(document.FilePath);
            Rect foldRect = EditorGUILayout.GetControlRect(false, 29, GUILayout.Width(15));
            bool next = EditorGUI.Foldout(foldRect, expanded, GUIContent.none, true);
            if (next != expanded) { if (next) closedFiles.Remove(document.FilePath); else closedFiles.Add(document.FilePath); }
            string title = Path.GetFileNameWithoutExtension(document.FilePath) + (file.Dirty ? "  ·" : string.Empty);
            if (GUILayout.Button(title, selected == document && selectedKind == EntryKind.Document ? theme.TreeSelected : theme.TreeRow, GUILayout.Height(29))) Select(document, EntryKind.Document);
            EditorGUILayout.EndHorizontal();
            if (!expanded || document.Document == null) return;
            DrawEntries(file, fileMatch);
            GUILayout.Space(5);
        }

        private void DrawEntries(LibraryFile file, bool showAll)
        {
            foreach (LibraryEntry entry in file.Entries)
            {
                if (!showAll && !Matches(entry.Label) && !Matches(entry.Name) && !Matches(entry.Id)) continue;
                Color color = entry.Kind == EntryKind.Ability ? theme.Accent : entry.Kind == EntryKind.Modifier ? theme.Purple : theme.Warning;
                string symbol = entry.Kind == EntryKind.Ability ? "技" : entry.Kind == EntryKind.Modifier ? "效" : "弹";
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(22);
                GUIStyle symbolStyle = new GUIStyle(theme.Small) { alignment = TextAnchor.MiddleCenter };
                symbolStyle.normal.textColor = color;
                GUILayout.Label(symbol, symbolStyle, GUILayout.Width(19), GUILayout.Height(26));
                string label = string.IsNullOrEmpty(entry.Label) ? "未命名" : entry.Label;
                bool active = selected == file.Session && selectedKind == entry.Kind && selectedIndex == entry.Index;
                if (GUILayout.Button(new GUIContent(label, entry.Name), active ? theme.TreeSelected : theme.TreeRow, GUILayout.Height(27))) Select(file.Session, entry.Kind, entry.Index);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawCenter(float height)
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.Height(height));
            GUILayout.Space(12);
            if (selected == null)
            {
                DrawWelcome();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(Path.GetFileName(selected.FilePath), theme.Small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("定位文件", EditorStyles.miniButton, GUILayout.Width(72))) EditorUtility.RevealInFinder(selected.FilePath);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical(theme.Card);
            GUILayout.Label(SelectedTitle(), theme.Title);
            GUILayout.Space(3);
            GUILayout.Label(SelectedName(), theme.Small);
            GUILayout.Space(10);
            DrawSelectionSummary();
            EditorGUILayout.EndVertical();
            EditorGUILayout.BeginHorizontal();
            string[] tabs = { "配置", "JSON 预览", "引用" };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], centerTab == i ? theme.ActiveTab : theme.Tab, GUILayout.Width(i == 1 ? 100 : 72))) centerTab = i;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(9);
            if (!string.IsNullOrEmpty(notice)) EditorGUILayout.HelpBox(notice, noticeIsError ? MessageType.Error : MessageType.Info);
            if (recoveryConflicts.Contains(selected.FilePath) || retainedRecovery.ContainsKey(selected.FilePath))
            {
                EditorGUILayout.HelpBox("已恢复未保存草稿，但磁盘文件发生变化。请另存副本或刷新后重新编辑，当前文件暂不覆盖。", MessageType.Warning);
                if (GUILayout.Button("将恢复草稿另存为新文件", theme.Button)) SaveRecoveredCopy();
            }
            if (selected.IsReadOnly) EditorGUILayout.HelpBox(selected.ReadError ?? "文件只读。可查看原文，不会覆盖未知或不支持的配置。", MessageType.Warning);
            if (centerTab == 0 && selected.Document != null) DrawInspector();
            else if (centerTab == 2 && selected.Document != null) DrawReferences();
            else DrawJson();
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectionSummary()
        {
            EditorGUILayout.BeginHorizontal();
            theme.Badge(KindLabel(selectedKind), selectedKind == EntryKind.Modifier ? theme.Purple : theme.Accent, 64);
            object target = SelectedObject();
            if (target is JsonAbility ability)
            {
                GUILayout.Space(8);
                GUILayout.Label(ability.maxLevel + " 级", theme.Body);
                GUILayout.Space(14);
                GUILayout.Label("动作 " + (ability.actions?.Length ?? 0), theme.Small);
                GUILayout.Space(12);
                GUILayout.Label("命中 " + (ability.onProjectileHit?.Length ?? 0), theme.Small);
            }
            else if (target is JsonModifier modifier)
            {
                GUILayout.Space(8);
                GUILayout.Label(modifier.isDebuff ? "负面效果" : "增益效果", theme.Body);
                GUILayout.Space(14);
                GUILayout.Label("最大层数 " + modifier.maxStack, theme.Small);
            }
            else if (target is JsonProjectile projectile)
            {
                GUILayout.Space(8);
                GUILayout.Label("速度 " + projectile.speed.ToString("0.##"), theme.Body);
                GUILayout.Space(14);
                GUILayout.Label("射程 " + projectile.distance.ToString("0.##"), theme.Small);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawInspector()
        {
            using (new EditorGUI.DisabledScope(selected.IsReadOnly || EditorApplication.isCompiling))
            {
                JsonAbilityDocument data = selected.Document;
                bool hasNulls = data.abilities != null && data.abilities.Any(value => value == null) || data.modifiers != null && data.modifiers.Any(value => value == null) || data.projectiles != null && data.projectiles.Any(value => value == null);
                if (hasNulls)
                {
                    EditorGUILayout.HelpBox("文件包含空定义条目。可清理空项并保留其它条目的顺序。", MessageType.Warning);
                    if (GUILayout.Button("移除空条目", theme.Button))
                        Mutate("移除空定义", () => { data.abilities = data.abilities?.Where(value => value != null).ToArray(); data.modifiers = data.modifiers?.Where(value => value != null).ToArray(); data.projectiles = data.projectiles?.Where(value => value != null).ToArray(); });
                }
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ 技能", theme.Button)) AddEntry(EntryKind.Ability);
                if (GUILayout.Button("+ 效果", theme.Button)) AddEntry(EntryKind.Modifier);
                if (GUILayout.Button("+ 弹道", theme.Button)) AddEntry(EntryKind.Projectile);
                if (selectedKind != EntryKind.Document)
                {
                    if (GUILayout.Button(new GUIContent("复制", "复制当前条目并生成新名称；依赖引用继续指向现有定义。"), theme.Button, GUILayout.Width(58))) DuplicateEntry();
                    if (GUILayout.Button("删除", theme.Button, GUILayout.Width(58))) DeleteEntry();
                }
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(7);
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                AbilityStudioDraft state = StateFor(selected);
                Undo.RecordObject(state, "编辑技能配置");
                EditorGUI.BeginChangeCheck();
                inspector?.Draw();
                if (EditorGUI.EndChangeCheck()) CaptureChanges(state);
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawJson()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(selected.IsReadOnly ? "原始文件 · 只读" : "导出预览 · 只读", theme.Small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("复制 JSON", EditorStyles.miniButton, GUILayout.Width(82))) EditorGUIUtility.systemCopyBuffer = cachedJson;
            EditorGUILayout.EndHorizontal();
            jsonScroll = EditorGUILayout.BeginScrollView(jsonScroll);
            int lines = cachedJson.Count(character => character == '\n') + 1;
            EditorGUILayout.SelectableLabel(cachedJson, theme.Code, GUILayout.Height(Mathf.Max(250, lines * 15)), GUILayout.ExpandWidth(true));
            EditorGUILayout.EndScrollView();
        }

        private void DrawReferences()
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
            List<KeyValuePair<string, string>> references = new List<KeyValuePair<string, string>>();
            CollectReferences(SelectedObject(), references);
            GUILayout.Label("直接引用", theme.Heading);
            GUILayout.Label("只展示链接；Modifier 引用不会递归展开。", theme.Small);
            GUILayout.Space(9);
            if (references.Count == 0) GUILayout.Label("当前条目没有命名引用。", theme.Small);
            foreach (KeyValuePair<string, string> reference in references.Distinct())
            {
                EditorGUILayout.BeginVertical(theme.Card);
                GUILayout.Label(reference.Key, theme.Eyebrow);
                if (GUILayout.Button(reference.Value, theme.TreeRow, GUILayout.Height(28))) NavigateReference(reference.Key, reference.Value);
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawTools(float width, float height)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width), GUILayout.Height(height));
            GUILayout.Space(12);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("诊断", toolTab == 0 ? theme.ActiveTab : theme.Tab)) toolTab = 0;
            if (GUILayout.Button("试运行", toolTab == 1 ? theme.ActiveTab : theme.Tab)) toolTab = 1;
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(9);
            toolScroll = EditorGUILayout.BeginScrollView(toolScroll);
            if (toolTab == 0) DrawDiagnostics();
            else DrawSandbox();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawDiagnostics()
        {
            int errors = diagnostics.Count(value => value.Severity == AbilityValidationSeverity.Error);
            int warnings = diagnostics.Count(value => value.Severity == AbilityValidationSeverity.Warning);
            EditorGUILayout.BeginVertical(theme.Card);
            GUILayout.Label("配置与引用", theme.Heading);
            GUILayout.Space(9);
            EditorGUILayout.BeginHorizontal();
            theme.Badge(errors + " 错误", errors > 0 ? theme.Error : theme.Accent, 90);
            GUILayout.Space(8);
            theme.Badge(warnings + " 提醒", warnings > 0 ? theme.Warning : theme.Muted, 90);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(9);
            GUILayout.Label("检查当前技能库中的定义、创建环和跨文件引用。运行预算请使用试运行。", theme.Small);
            if (GUILayout.Button("重新校验", theme.Button)) ValidateWorkspace();
            EditorGUILayout.EndVertical();
            if (workspace != null && diagnostics.Count == 0)
            {
                EditorGUILayout.BeginVertical(theme.Card);
                GUILayout.Label("静态校验通过", theme.Heading);
                GUILayout.Label("配置结构与引用有效；这不代表所有战斗路径均已验证。", theme.Small);
                EditorGUILayout.EndVertical();
            }
            foreach (AbilityEditorDiagnostic diagnostic in diagnostics)
            {
                Color color = diagnostic.Severity == AbilityValidationSeverity.Error ? theme.Error : diagnostic.Severity == AbilityValidationSeverity.Warning ? theme.Warning : theme.Muted;
                EditorGUILayout.BeginVertical(theme.Card);
                GUIStyle code = new GUIStyle(theme.Eyebrow);
                code.normal.textColor = color;
                GUILayout.Label(diagnostic.Code ?? diagnostic.Severity.ToString(), code);
                GUILayout.Space(5);
                GUIStyle message = new GUIStyle(theme.Body) { wordWrap = true };
                GUILayout.Label(diagnostic.Message ?? string.Empty, message);
                if (!string.IsNullOrEmpty(diagnostic.ReferenceChain)) GUILayout.Label(diagnostic.ReferenceChain, theme.Small);
                if (!string.IsNullOrEmpty(diagnostic.FilePath) && GUILayout.Button("定位配置", EditorStyles.miniButton)) NavigateDiagnostic(diagnostic);
                EditorGUILayout.EndVertical();
            }
        }

        private void DrawSandbox()
        {
            EditorGUILayout.BeginVertical(theme.Card);
            GUILayout.Label("隔离试运行", theme.Heading);
            GUILayout.Label("手动运行一次测试路径，不访问场景单位或执行业务脚本。", theme.Small);
            GUILayout.Space(9);
            string[] names = sandboxNames;
            if (selectedKind == EntryKind.Ability && SelectedObject() is JsonAbility selectedAbility && string.IsNullOrEmpty(sandboxAbility)) sandboxAbility = selectedAbility.name;
            if (names.Length > 0)
            {
                int index = Math.Max(0, Array.IndexOf(names, sandboxAbility));
                index = EditorGUILayout.Popup("技能", index, names);
                sandboxAbility = names[index];
            }
            else GUILayout.Label("请先添加或选择技能。", theme.Small);
            sandboxLevel = EditorGUILayout.IntSlider("技能等级", sandboxLevel, 1, Math.Max(1, SandboxMaxLevel()));
            sandboxOptions.TargetCount = EditorGUILayout.IntSlider("测试目标", sandboxOptions.TargetCount, 1, 32);
            sandboxOptions.SimulationSeconds = EditorGUILayout.Slider("模拟秒数", sandboxOptions.SimulationSeconds, 0.1f, 10f);
            GUILayout.Space(5);
            GUILayout.Label("执行预算", theme.Eyebrow);
            sandboxOptions.MaxExecutionDepth = EditorGUILayout.IntField(new GUIContent("调用深度", "实际执行调用层数，不等于 Modifier 个数。"), sandboxOptions.MaxExecutionDepth);
            sandboxOptions.MaxExecutionOperations = EditorGUILayout.IntField("操作次数", sandboxOptions.MaxExecutionOperations);
            using (new EditorGUI.DisabledScope(names.Length == 0 || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("开始试运行", theme.PrimaryButton)) RunSandbox();
            EditorGUILayout.EndVertical();
            if (sandboxReport == null) return;
            EditorGUILayout.BeginVertical(theme.Card);
            GUILayout.Label(sandboxReport.ExecutionAborted ? "执行预算中止" : sandboxReport.HasPendingWork ? "仍有未完成过程" : sandboxReport.Success ? "本次运行完成" : "本次运行未通过", theme.Heading);
            GUILayout.Space(5);
            GUILayout.Label(sandboxReport.Message ?? string.Empty, theme.Small);
            GUILayout.Label("实际伤害  " + sandboxReport.DamageTotal.ToString("0.##"), theme.Body);
            GUILayout.Label("实际治疗  " + sandboxReport.HealingTotal.ToString("0.##"), theme.Body);
            GUILayout.Label("清理后对象  " + sandboxReport.RemainingRuntimeObjects, theme.Small);
            foreach (string message in sandboxReport.BudgetMessages) EditorGUILayout.HelpBox(message, MessageType.Error);
            foreach (string message in sandboxReport.ValidationMessages) EditorGUILayout.HelpBox(message, MessageType.Warning);
            EditorGUILayout.EndVertical();
            GUILayout.Label("事件记录", theme.Heading);
            foreach (string entry in sandboxReport.Events) GUILayout.Label(entry, theme.Small);
            if (sandboxReport.LogTruncated) GUILayout.Label("事件记录已按上限截断。", theme.Small);
        }

        private void DrawWelcome()
        {
            GUILayout.Space(45);
            EditorGUILayout.BeginVertical(theme.Card);
            GUILayout.Label("把技能配置变成可维护的作品", theme.Title);
            GUILayout.Space(9);
            GUILayout.Label("从左侧选择技能、效果或弹道。通过等级表、动作和引用完成配置，再校验与试运行。", new GUIStyle(theme.Body) { wordWrap = true });
            GUILayout.Space(14);
            if (GUILayout.Button("打开技能 JSON 目录", theme.PrimaryButton, GUILayout.Width(180))) ChooseWorkspace();
            GUILayout.Space(8);
            GUILayout.Label("目录应只包含技能 JSON。现有英雄样例位于 Data/AbilityJsonSources/Heroes。", theme.Small);
            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            theme.Rule();
            EditorGUILayout.BeginHorizontal(GUILayout.Height(23));
            GUILayout.Space(13);
            GUILayout.Label(totalAbilities + " 技能  /  " + totalModifiers + " 效果  /  " + totalProjectiles + " 弹道", theme.Small);
            GUILayout.FlexibleSpace();
            GUILayout.Label("JSON 数据源  ·  Ctrl / Cmd + S 保存当前文件", theme.Small);
            GUILayout.Space(13);
            EditorGUILayout.EndHorizontal();
        }

        private void RunSandbox()
        {
            try
            {
                List<IAbilityDefinitionProvider> providers = new List<IAbilityDefinitionProvider>();
                foreach (AbilityDocumentSession document in workspace.Documents)
                    providers.Add(new JsonAbilityDefinitionProvider(document.DraftText, document.FilePath));
                sandboxReport = AbilitySandbox.Run(providers, sandboxAbility, sandboxLevel, sandboxOptions);
            }
            catch (Exception exception) { Notify("试运行未完成：" + exception.Message, true); }
        }

        private int SandboxMaxLevel()
        {
            JsonAbility ability = workspace?.Documents.Where(value => value.Document != null).SelectMany(value => Items(value.Document.abilities)).FirstOrDefault(value => value.name == sandboxAbility);
            return ability?.maxLevel ?? 1;
        }

        private void HandleShortcuts()
        {
            Event current = Event.current;
            if (current.type == EventType.KeyDown && (current.control || current.command) && current.keyCode == KeyCode.S)
            {
                GUI.FocusControl(null);
                if (current.shift) SaveAll(); else SaveCurrent();
                current.Use();
            }
        }

        private void Notify(string text, bool error)
        {
            notice = text;
            noticeIsError = error;
            Repaint();
        }

        private static string Relative(string root, string path)
        {
            string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(prefix, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                throw new InvalidOperationException("请选择当前技能库目录内的 JSON 文件。");
            return full.Substring(prefix.Length);
        }

        private static void CollectReferences(object value, ICollection<KeyValuePair<string, string>> references)
        {
            if (value == null || value is string || value.GetType().IsPrimitive) return;
            if (value is Array array) { foreach (object item in array) CollectReferences(item, references); return; }
            foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                object item = field.GetValue(value);
                string kind = field.Name == "modifierName" || field.Name == "intrinsicModifierName" || field.Name == "auraModifierName" ? "modifier" : field.Name == "projectileName" ? "projectile" : field.Name == "valueSpecialName" || field.Name == "durationSpecialName" ? "special" : null;
                if (kind != null && item is string name && !string.IsNullOrEmpty(name)) references.Add(new KeyValuePair<string, string>(kind, name));
                else if (item != null && !(item is string) && !field.FieldType.IsPrimitive) CollectReferences(item, references);
            }
        }

        private static IEnumerable<T> Items<T>(T[] values) where T : class => (values ?? Array.Empty<T>()).Where(value => value != null);
        private bool Matches(string value) => string.IsNullOrWhiteSpace(search) || value != null && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        private void NormalizeSelection()
        {
            object value = SelectedObject();
            if (selectedKind == EntryKind.Ability && !(value is JsonAbility) || selectedKind == EntryKind.Modifier && !(value is JsonModifier) || selectedKind == EntryKind.Projectile && !(value is JsonProjectile))
            {
                selectedKind = EntryKind.Document;
                selectedIndex = 0;
            }
        }
        private bool CachedDirty(AbilityDocumentSession session) => libraryFiles.FirstOrDefault(value => value.Session == session)?.Dirty ?? false;

        private void RefreshLibrary(bool all)
        {
            if (workspace == null) { libraryFiles.Clear(); return; }
            foreach (AbilityDocumentSession session in workspace.Documents)
            {
                LibraryFile file = libraryFiles.FirstOrDefault(value => value.Session == session);
                if (file != null && !all && session != selected) continue;
                if (file == null) { file = new LibraryFile { Session = session }; libraryFiles.Add(file); }
                file.Dirty = session.IsDirty;
                file.Entries.Clear();
                JsonAbilityDocument data = session.Document;
                for (int i = 0; i < (data?.abilities?.Length ?? 0); i++)
                    if (data.abilities[i] != null) file.Entries.Add(new LibraryEntry { Kind = EntryKind.Ability, Index = i, Id = data.abilities[i].id, Name = data.abilities[i].name, Label = string.IsNullOrEmpty(data.abilities[i].displayName) ? data.abilities[i].name : data.abilities[i].displayName });
                for (int i = 0; i < (data?.modifiers?.Length ?? 0); i++)
                    if (data.modifiers[i] != null) file.Entries.Add(new LibraryEntry { Kind = EntryKind.Modifier, Index = i, Id = data.modifiers[i].id, Name = data.modifiers[i].name, Label = string.IsNullOrEmpty(data.modifiers[i].displayName) ? data.modifiers[i].name : data.modifiers[i].displayName });
                for (int i = 0; i < (data?.projectiles?.Length ?? 0); i++)
                    if (data.projectiles[i] != null) file.Entries.Add(new LibraryEntry { Kind = EntryKind.Projectile, Index = i, Id = data.projectiles[i].id, Name = data.projectiles[i].name, Label = data.projectiles[i].name });
            }
            libraryFiles.RemoveAll(value => !workspace.Documents.Contains(value.Session));
            totalAbilities = libraryFiles.Sum(value => value.Entries.Count(entry => entry.Kind == EntryKind.Ability));
            totalModifiers = libraryFiles.Sum(value => value.Entries.Count(entry => entry.Kind == EntryKind.Modifier));
            totalProjectiles = libraryFiles.Sum(value => value.Entries.Count(entry => entry.Kind == EntryKind.Projectile));
            sandboxNames = libraryFiles.SelectMany(value => value.Entries).Where(value => value.Kind == EntryKind.Ability && !string.IsNullOrEmpty(value.Name)).Select(value => value.Name).Distinct().ToArray();
        }

        private sealed class LibraryFile
        {
            public AbilityDocumentSession Session;
            public bool Dirty;
            public readonly List<LibraryEntry> Entries = new List<LibraryEntry>();
        }
        private sealed class LibraryEntry { public EntryKind Kind; public int Index; public string Id; public string Name; public string Label; }
        private static T[] Append<T>(T[] values, T value) => (values ?? Array.Empty<T>()).Concat(new[] { value }).ToArray();
        private static T[] RemoveAt<T>(T[] values, int index) => values.Where((value, i) => i != index).ToArray();
        private static string KindLabel(EntryKind kind) => kind == EntryKind.Ability ? "技能" : kind == EntryKind.Modifier ? "效果" : kind == EntryKind.Projectile ? "弹道" : "文档";

        [Serializable] private sealed class RecoveryData { public string root; public RecoveryEntry[] entries; }
        [Serializable] private sealed class RecoveryEntry { public string path; public string sourceHash; public bool sourceExisted; public string json; }
    }
}
#endif

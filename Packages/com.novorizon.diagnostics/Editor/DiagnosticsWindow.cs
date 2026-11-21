using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Diagnostics.Editor
{
    /// <summary>诊断对应的窗口。提供此模块的 Unity 编辑器窗口。</summary>
    public sealed class DiagnosticsWindow : EditorWindow
    {
        private const int PageSize = 100;

        /// <summary>保存显示行所需的数据与配套操作。</summary>
        private struct DisplayRow
        {
            internal string label;
            internal string value;
        }

        private DiagnosticSnapshot snapshot, baseline;
        private DiagnosticDifference difference;
        private readonly List<DisplayRow> matchingRows = new List<DisplayRow>();
        private double nextSample;
        private bool paused, compareBaseline, rowsDirty = true;
        private int page;
        private string search = string.Empty;
        private Vector2 scroll;

        /// <summary>打开诊断窗口。</summary>
        [MenuItem("Tools/Novorizon/Diagnostics")]
        public static void Open() => GetWindow<DiagnosticsWindow>("Novorizon Diagnostics");

        /// <summary>响应 Unity 组件启用。</summary>
        private void OnEnable()
        {
            EditorApplication.update += Tick;
            nextSample = 0;
            rowsDirty = true;
        }

        /// <summary>响应 Unity 组件停用。</summary>
        private void OnDisable() => EditorApplication.update -= Tick;

        /// <summary>推进诊断窗口。</summary>
        private void Tick()
        {
            if (paused || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextSample)
            {
                return;
            }
            nextSample = EditorApplication.timeSinceStartup + .5;
            Capture();
        }

        /// <summary>采集诊断窗口。</summary>
        private void Capture()
        {
            snapshot = PackageDiagnostics.Capture();
            difference = baseline == null ? null : DiagnosticSnapshotComparer.Compare(baseline, snapshot);
            rowsDirty = true;
            Repaint();
        }

        /// <summary>绘制当前界面并响应对应的 GUI 事件。</summary>
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Read-only public diagnostics. Sampling never creates services.", MessageType.Info);
            paused = EditorGUILayout.Toggle("Pause sampling", paused);
            bool nextComparison = EditorGUILayout.Toggle("Compare baseline", compareBaseline);
            string nextSearch = EditorGUILayout.TextField("Filter metrics", search);
            if (compareBaseline != nextComparison || !string.Equals(search, nextSearch, StringComparison.Ordinal))
            {
                compareBaseline = nextComparison;
                search = nextSearch;
                InvalidateRows();
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Capture now"))
            {
                Capture();
            }
            if (GUILayout.Button("Set baseline"))
            {
                if (snapshot == null)
                {
                    Capture();
                }
                baseline = snapshot;
                difference = DiagnosticSnapshotComparer.Compare(baseline, snapshot);
                InvalidateRows();
            }
            if (GUILayout.Button("Clear baseline"))
            {
                baseline = null;
                difference = null;
                InvalidateRows();
            }
            if (GUILayout.Button("Export JSON"))
            {
                Export();
            }
            EditorGUILayout.EndHorizontal();
            if (snapshot == null)
            {
                return;
            }
            EditorGUILayout.LabelField("Captured UTC", snapshot.capturedUtc);
            if (baseline != null)
            {
                EditorGUILayout.LabelField("Baseline UTC", baseline.capturedUtc);
            }
            if (rowsDirty)
            {
                RebuildMatchingRows();
            }
            DrawPagination();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            int start = page * PageSize;
            int end = start + Math.Min(PageSize, matchingRows.Count - start);
            for (int index = start; index < end; index++)
            {
                DisplayRow row = matchingRows[index];
                EditorGUILayout.LabelField(row.label, row.value);
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>使失效行。</summary>
        private void InvalidateRows()
        {
            rowsDirty = true;
            page = 0;
            scroll = Vector2.zero;
        }

        /// <summary>重建 Matching 行。</summary>
        private void RebuildMatchingRows()
        {
            matchingRows.Clear();
            if (compareBaseline && difference != null)
            {
                foreach (var metric in difference.metrics)
                {
                    if (Matches(metric.name, metric.before, metric.after))
                    {
                        matchingRows.Add(new DisplayRow
                        {
                            label = metric.kind + ": " + metric.name,
                            value = (metric.before ?? "—") + " → " + (metric.after ?? "—")
                        });
                    }
                }
            }
            else
            {
                foreach (var metric in snapshot.metrics)
                {
                    if (Matches(metric.name, metric.value))
                    {
                        matchingRows.Add(new DisplayRow { label = metric.name, value = metric.value });
                    }
                }
            }
            rowsDirty = false;
            page = Math.Max(0, Math.Min(page, (Math.Max(1, matchingRows.Count) - 1) / PageSize));
        }

        /// <summary>绘制 Pagination。</summary>
        private void DrawPagination()
        {
            int pages = (Math.Max(1, matchingRows.Count) - 1) / PageSize + 1;
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(page == 0))
            {
                if (GUILayout.Button("Previous"))
                {
                    page--;
                    scroll = Vector2.zero;
                }
            }
            EditorGUILayout.LabelField("Page " + (page + 1) + " / " + pages + " · " + matchingRows.Count + " matching records");
            using (new EditorGUI.DisabledScope(page + 1 >= pages))
            {
                if (GUILayout.Button("Next"))
                {
                    page++;
                    scroll = Vector2.zero;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>判断是否匹配诊断窗口。</summary>
        private bool Matches(
            string first,
            string second,
            string third = null)
        {
            if (string.IsNullOrEmpty(search))
            {
                return true;
            }
            return ContainsSearch(first) || ContainsSearch(second) || ContainsSearch(third);
        }

        /// <summary>检查是否包含搜索。</summary>
        private bool ContainsSearch(string value) => value != null && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>导出诊断窗口。</summary>
        private void Export()
        {
            if (snapshot == null)
            {
                Capture();
            }
            string path = EditorUtility.SaveFilePanel("Export diagnostic snapshot and baseline", "", "novorizon-diagnostics.json", "json");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(new DiagnosticExport
                {
                    snapshot = snapshot,
                    baseline = baseline,
                    difference = difference
                }, true));
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
    }
}

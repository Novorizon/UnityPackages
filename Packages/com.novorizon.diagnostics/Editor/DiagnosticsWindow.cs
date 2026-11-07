using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Diagnostics.Editor
{
    public sealed class DiagnosticsWindow : EditorWindow
    {
        private DiagnosticSnapshot snapshot, baseline;
        private DiagnosticDifference difference;
        private double nextSample;
        private bool paused, compareBaseline;
        private string search = string.Empty;
        private Vector2 scroll;

        [MenuItem("Tools/Novorizon/Diagnostics")]
        public static void Open() => GetWindow<DiagnosticsWindow>("Novorizon Diagnostics");

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            nextSample = 0;
        }

        private void OnDisable() => EditorApplication.update -= Tick;

        private void Tick()
        {
            if (paused || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextSample)
            {
                return;
            }
            nextSample = EditorApplication.timeSinceStartup + .5;
            Capture();
        }

        private void Capture()
        {
            snapshot = PackageDiagnostics.Capture();
            difference = baseline == null ? null : DiagnosticSnapshotComparer.Compare(baseline, snapshot);
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Read-only public diagnostics. Sampling never creates services. Lease rows describe managed independent acquisitions, including native I/O awaiting release; they are not memory bytes or native reference counts.", MessageType.Info);
            paused = EditorGUILayout.Toggle("Pause sampling", paused);
            compareBaseline = EditorGUILayout.Toggle("Compare baseline", compareBaseline);
            search = EditorGUILayout.TextField("Filter owner / scene / resource", search);
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
            }
            if (GUILayout.Button("Clear baseline"))
            {
                baseline = null;
                difference = null;
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
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (compareBaseline && difference != null)
            {
                DrawDifference();
            }
            else
            {
                foreach (var metric in snapshot.metrics)
                {
                    if (Matches(metric.name, metric.value))
                    {
                        EditorGUILayout.LabelField(metric.name, metric.value);
                    }
                }
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Independent resource acquisitions", EditorStyles.boldLabel);
                foreach (var lease in snapshot.leases)
                {
                    DrawLease(lease, lease.state);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawDifference()
        {
            foreach (var metric in difference.metrics)
            {
                if (Matches(metric.name, metric.before, metric.after))
                {
                    EditorGUILayout.LabelField(metric.kind + ": " + metric.name, (metric.before ?? "—") + " → " + (metric.after ?? "—"));
                }
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lease changes since baseline", EditorStyles.boldLabel);
            foreach (var change in difference.leases)
            {
                string states = change.before == null ? change.after.state : change.after == null ?
                    change.before.state : change.before.state + " → " + change.after.state;
                DrawLease(change.after ?? change.before, change.kind + " · " + states);
            }
        }

        private void DrawLease(DiagnosticLease lease, string status)
        {
            if (!Matches(lease.owner, lease.location, lease.sceneName, lease.assetType, lease.state, status))
            {
                return;
            }
            string owner = lease.owner + " · scope " + lease.scopeId + " · lease " + lease.leaseId;
            EditorGUILayout.LabelField(new GUIContent(status + ": " + owner, lease.creationStack ?? "Creation stack not captured"), new GUIContent(lease.location));
            EditorGUILayout.LabelField("", lease.assetType + (lease.sceneHandle == 0 ? "" : " · scene " + lease.sceneName + " (" + lease.sceneHandle + ")"));
        }

        private bool Matches(params string[] values)
        {
            if (string.IsNullOrEmpty(search))
            {
                return true;
            }
            foreach (string value in values)
            {
                if (value != null && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

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

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Game.Framework.Diagnostics.Editor
{
    public sealed class DiagnosticsWindow : EditorWindow
    {
        private DiagnosticSnapshot snapshot;
        private double nextSample;
        private bool paused;
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
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Read-only package counters. Missing or released services are never started by this window. Sampling: 0.5 seconds while open.", MessageType.Info);
            paused = EditorGUILayout.Toggle("Pause sampling", paused);
            search = EditorGUILayout.TextField("Filter", search);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Capture now"))
            {
                Capture();
            }

            if (GUILayout.Button("Export JSON"))
            {
                if (snapshot == null)
                {
                    Capture();
                }

                string path = EditorUtility.SaveFilePanel("Export diagnostic snapshot", "", "novorizon-diagnostics.json", "json");
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        File.WriteAllText(path, JsonUtility.ToJson(snapshot, true));
                    }
                    catch (Exception error)
                    {
                        Debug.LogException(error);
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
            if (snapshot == null)
            {
                return;
            }

            EditorGUILayout.LabelField("Captured UTC", snapshot.capturedUtc);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var metric in snapshot.metrics)
            {
                if (string.IsNullOrEmpty(search) || metric.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    EditorGUILayout.LabelField(metric.name, metric.value);
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}

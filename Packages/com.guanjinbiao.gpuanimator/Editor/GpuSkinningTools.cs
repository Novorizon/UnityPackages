using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class GpuSkinningTools : EditorWindow
{
    public static readonly string DEFAULT_FBX_PATH = "Assets/Artworks/GpuRole/";
    public static readonly string DEFAULT_OUTPUT_PATH = "Assets/Main/Prefabs/GpuRole/";
    public static readonly string DEFAULT_PBR_SHADER_NAME = "Custom/GpuSkinningAnimationPBR";
    public static readonly string DEFAULT_SHADER_NAME = "Custom/GpuSkinningAnimation";
    public static string DEFAULT_USE_SHADER_NAME = DEFAULT_SHADER_NAME;
    public static readonly string DEFAULT_SAVE_MESH_NAME = "_Mesh.asset";
    public static readonly string DEFAULT_SAVE_PREFAB_NAME = "_DynPre.prefab";
    public static readonly string DEFAULT_SAVE_MATERIAL_NAME = "_DynMat.mat";
    public static readonly string DEFAULT_SAVE_DEFAULT_MATERIAL_NAME = "_Default_DynMat.mat";

    private enum ShaderMode { Unlit, PBR }
    private static readonly int[] TextureSizes = { 256, 512, 1024, 2048, 4096, 8192, 16384 };
    private static readonly string[] TextureSizeLabels = { "256", "512", "1024", "2048", "4096", "8192", "16384" };

    [SerializeField] private string sourceDirectory = DEFAULT_FBX_PATH;
    [SerializeField] private string outputDirectory = DEFAULT_OUTPUT_PATH;
    [SerializeField] private ShaderMode shaderMode;
    [SerializeField] private int maximumTextureSize = 2048;
    private readonly List<ReportEntry> report = new List<ReportEntry>();
    private Vector2 reportScroll;
    private string summary;
    private bool running;

    private sealed class ReportEntry
    {
        public string Message;
        public MessageType Type;
    }

    [MenuItem("GpuSkinning/Baker")]
    private static void ShowWindow()
    {
        GpuSkinningTools window = GetWindow<GpuSkinningTools>();
        window.titleContent = new GUIContent("GPU Animation Baker");
        window.minSize = new Vector2(520, 420);
        window.Show();
    }

    [MenuItem("GpuSkinning/No PBR")]
    public static void GPUSkinning() { BakeDefaults(ShaderMode.Unlit); }

    [MenuItem("GpuSkinning/PBR")]
    public static void GenGPUSkinningPBR() { BakeDefaults(ShaderMode.PBR); }

    private static void BakeDefaults(ShaderMode mode)
    {
        ShowWindow();
        GpuSkinningTools window = GetWindow<GpuSkinningTools>();
        window.sourceDirectory = DEFAULT_FBX_PATH;
        window.outputDirectory = DEFAULT_OUTPUT_PATH;
        window.shaderMode = mode;
        window.maximumTextureSize = 2048;
        window.RunBatch(false);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("GPU Animation Baker", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Each source subfolder is one role. Validate samples every clip without saving assets. Bake writes the generated assets for each valid role.", MessageType.Info);
        using (new EditorGUI.DisabledScope(running))
        {
            DrawDirectoryField("Source directory", ref sourceDirectory);
            DrawDirectoryField("Output directory", ref outputDirectory);
            shaderMode = (ShaderMode)EditorGUILayout.EnumPopup("Shader", shaderMode);
            maximumTextureSize = EditorGUILayout.IntPopup("Maximum texture size", maximumTextureSize, TextureSizeLabels, TextureSizes);
        }
        bool editorBusy = EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating;
        if (editorBusy)
            EditorGUILayout.HelpBox("Wait until the Editor has finished importing or compiling, and exit Play Mode before baking.", MessageType.Warning);
        string pathError = GetDirectoryError();
        if (pathError != null)
            EditorGUILayout.HelpBox(pathError, MessageType.Error);
        using (new EditorGUI.DisabledScope(running || editorBusy || pathError != null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate all roles")) RunBatch(true);
            if (GUILayout.Button("Bake all roles")) RunBatch(false);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.Space();
        if (!string.IsNullOrEmpty(summary))
            EditorGUILayout.LabelField(summary, EditorStyles.wordWrappedLabel);
        reportScroll = EditorGUILayout.BeginScrollView(reportScroll);
        foreach (ReportEntry entry in report)
            EditorGUILayout.HelpBox(entry.Message, entry.Type);
        EditorGUILayout.EndScrollView();
    }

    private static void DrawDirectoryField(string label, ref string value)
    {
        EditorGUILayout.BeginHorizontal();
        value = EditorGUILayout.TextField(label, value);
        if (GUILayout.Button("Browse", GUILayout.Width(65)))
        {
            string initialDirectory = Application.dataPath;
            try
            {
                string candidate = GpuAnimationBakeDiscovery.ToAbsolutePath(value);
                if (Directory.Exists(candidate)) initialDirectory = candidate;
            }
            catch (ArgumentException) { }
            string selection = EditorUtility.OpenFolderPanel(label, initialDirectory, string.Empty);
            if (!string.IsNullOrEmpty(selection))
            {
                // Keep invalid selections visible so the path error explains why they cannot be used.
                try { value = GpuAnimationBakeDiscovery.NormalizeAssetPath(selection); }
                catch (ArgumentException) { value = selection; }
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    private string GetDirectoryError()
    {
        try
        {
            GpuAnimationBakeDiscovery.ValidateDirectories(sourceDirectory, outputDirectory);
            return null;
        }
        catch (Exception exception) { return exception.Message; }
    }

    private void RunBatch(bool validateOnly)
    {
        if (running) return;
        report.Clear();
        summary = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            AddReport("The Editor must be idle and outside Play Mode before baking.", MessageType.Error);
            return;
        }
        running = true;
        int succeeded = 0;
        int failed = 0;
        int processed = 0;
        bool cancelled = false;
        bool couldNotStart = false;
        List<GpuAnimationBakeCandidate> candidates = null;
        string action = validateOnly ? "Validation" : "Bake";
        try
        {
            string shaderName = shaderMode == ShaderMode.PBR ? DEFAULT_PBR_SHADER_NAME : DEFAULT_SHADER_NAME;
            candidates = GpuAnimationBakeDiscovery.Discover(sourceDirectory, outputDirectory, shaderName, maximumTextureSize);
            if (candidates.Count == 0)
            {
                AddReport("No role subfolders were found in " + GpuAnimationBakeDiscovery.NormalizeAssetPath(sourceDirectory) + ".", MessageType.Warning);
                return;
            }
            for (int index = 0; index < candidates.Count; index++)
            {
                GpuAnimationBakeCandidate candidate = candidates[index];
                int roleIndex = index;
                try
                {
                    if (EditorUtility.DisplayCancelableProgressBar("GPU Animation " + action, candidate.Name, (float)index / candidates.Count))
                        throw new OperationCanceledException();
                    if (!string.IsNullOrEmpty(candidate.Error))
                        throw new InvalidOperationException(candidate.Error);
                    using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(candidate.Request, (progress, message) =>
                        EditorUtility.DisplayCancelableProgressBar("GPU Animation " + action, candidate.Name + ": " + message,
                            (roleIndex + Mathf.Clamp01(progress)) / candidates.Count)))
                    {
                        foreach (string warning in result.Warnings)
                            AddReport(candidate.Name + ": " + warning, MessageType.Warning);
                        string prefabPath = null;
                        if (validateOnly)
                            GpuAnimationAssetWriter.Validate(candidate.Request, result);
                        else
                            prefabPath = GpuAnimationAssetWriter.Save(candidate.Request, result);
                        succeeded++;
                        AddReport(candidate.Name + (validateOnly ? ": validation passed." : ": saved " + prefabPath), MessageType.Info);
                    }
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    AddReport(action + " cancelled at " + candidate.Name + ". Completed roles are preserved.", MessageType.Warning);
                    break;
                }
                catch (Exception exception)
                {
                    failed++;
                    AddReport(candidate.Name + ": " + exception.Message, MessageType.Error);
                }
                processed++;
            }
        }
        catch (Exception exception)
        {
            couldNotStart = true;
            AddReport(action + " could not start: " + exception.Message, MessageType.Error);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            running = false;
            int remaining = candidates == null ? 0 : candidates.Count - processed;
            summary = couldNotStart ? action + " could not start. See the error above." :
                action + ": " + succeeded + " succeeded, " + failed + " failed" +
                (cancelled ? ", " + remaining + " not completed (cancelled)." : ".");
            Debug.Log("[GpuAnimator] " + summary);
            Repaint();
        }
    }

    private void AddReport(string message, MessageType type)
    {
        report.Add(new ReportEntry { Message = message, Type = type });
        if (type == MessageType.Error) Debug.LogError("[GpuAnimator] " + message);
        else if (type == MessageType.Warning) Debug.LogWarning("[GpuAnimator] " + message);
        else Debug.Log("[GpuAnimator] " + message);
    }
}

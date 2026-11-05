using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

internal sealed class GpuAnimationBakeCandidate
{
    public string Name;
    public GpuAnimationBakeRequest Request;
    public string Error;
}

internal static class GpuAnimationBakeDiscovery
{
    internal static string NormalizeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Choose a directory under this project's Assets directory.");
        string normalized = path.Trim().Replace('\\', '/').TrimEnd('/');
        foreach (string segment in normalized.Split('/'))
            if (segment == "." || segment == "..")
                throw new ArgumentException("Directory paths cannot contain '.' or '..' segments.");
        if (Path.IsPathRooted(normalized))
        {
            string absolute = Path.GetFullPath(normalized).Replace('\\', '/').TrimEnd('/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (string.Equals(absolute, assets, comparison)) normalized = "Assets";
            else if (absolute.StartsWith(assets + "/", comparison)) normalized = "Assets" + absolute.Substring(assets.Length);
            else throw new ArgumentException("Source and output directories must be inside this project's Assets directory.");
        }
        if (normalized != "Assets" && !normalized.StartsWith("Assets/", StringComparison.Ordinal))
            throw new ArgumentException("Use a project path beginning with 'Assets/'.");
        if (normalized.IndexOf("//", StringComparison.Ordinal) >= 0 || normalized.IndexOf(':') >= 0)
            throw new ArgumentException("The Assets directory path is invalid.");
        return normalized;
    }

    internal static string ToAbsolutePath(string assetPath)
    {
        string normalized = NormalizeAssetPath(assetPath);
        DirectoryInfo projectRoot = Directory.GetParent(Application.dataPath);
        if (projectRoot == null) throw new ArgumentException("The Unity project directory could not be located.");
        return Path.GetFullPath(Path.Combine(projectRoot.FullName, normalized));
    }

    internal static void ValidateDirectories(string sourceDirectory, string outputDirectory)
    {
        string source = NormalizeAssetPath(sourceDirectory);
        string output = NormalizeAssetPath(outputDirectory);
        if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(output + "/", StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(source + "/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Source and output directories must be separate; neither may contain the other.");
        if (!Directory.Exists(ToAbsolutePath(source)))
            throw new ArgumentException("The source directory does not exist: " + source);
        if (File.Exists(ToAbsolutePath(output)))
            throw new ArgumentException("The output path is a file, not a directory: " + output);
    }

    internal static List<GpuAnimationBakeCandidate> Discover(string sourceDirectory, string outputDirectory, string shaderName, int maximumTextureSize)
    {
        ValidateDirectories(sourceDirectory, outputDirectory);
        string source = NormalizeAssetPath(sourceDirectory);
        string output = NormalizeAssetPath(outputDirectory);
        DirectoryInfo[] directories = new DirectoryInfo(ToAbsolutePath(source)).GetDirectories();
        Array.Sort(directories, (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (DirectoryInfo directory in directories)
        {
            int count;
            nameCounts.TryGetValue(directory.Name, out count);
            nameCounts[directory.Name] = count + 1;
        }
        var candidates = new List<GpuAnimationBakeCandidate>(directories.Length);
        foreach (DirectoryInfo directory in directories)
        {
            var candidate = new GpuAnimationBakeCandidate { Name = directory.Name };
            try
            {
                if (nameCounts[directory.Name] != 1)
                    throw new InvalidOperationException("Role folder names must be unique ignoring letter case.");
                candidate.Request = CreateRequest(directory, source + "/" + directory.Name,
                    output + "/" + directory.Name, shaderName, maximumTextureSize);
            }
            catch (Exception exception) { candidate.Error = exception.Message; }
            candidates.Add(candidate);
        }
        return candidates;
    }

    private static GpuAnimationBakeRequest CreateRequest(DirectoryInfo directory, string assetDirectory,
        string outputDirectory, string shaderName, int maximumTextureSize)
    {
        ValidateRoleName(directory.Name);
        FileInfo[] files = directory.GetFiles();
        FileInfo model = FindSingleFile(files, file => string.Equals(file.Extension, ".fbx", StringComparison.OrdinalIgnoreCase), "one FBX model", true);
        DirectoryInfo resources = null;
        foreach (DirectoryInfo child in directory.GetDirectories())
        {
            if (!string.Equals(child.Name, "res", StringComparison.OrdinalIgnoreCase)) continue;
            if (resources != null)
                throw new InvalidOperationException("More than one 'res' directory matches ignoring letter case.");
            resources = child;
        }
        if (resources == null) throw new InvalidOperationException("Missing res directory.");
        FileInfo[] resourceFiles = resources.GetFiles();
        FileInfo mainTexture = FindSingleFile(resourceFiles,
            file => string.Equals(file.Name, directory.Name + ".png", StringComparison.OrdinalIgnoreCase), directory.Name + ".png", true);
        FileInfo maskTexture = FindSingleFile(resourceFiles,
            file => string.Equals(file.Name, directory.Name + "_mask.png", StringComparison.OrdinalIgnoreCase), directory.Name + "_mask.png", false);
        Array.Sort(resourceFiles, (left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        string resourceAssetPath = assetDirectory + "/" + resources.Name;
        var clips = new List<AnimationClip>();
        foreach (FileInfo file in resourceFiles)
            if (string.Equals(file.Extension, ".anim", StringComparison.OrdinalIgnoreCase))
                clips.Add(LoadAsset<AnimationClip>(resourceAssetPath + "/" + file.Name));
        if (clips.Count == 0)
            throw new InvalidOperationException("No external .anim clips were found directly in res.");
        return new GpuAnimationBakeRequest
        {
            Name = directory.Name,
            Model = LoadAsset<GameObject>(assetDirectory + "/" + model.Name),
            Clips = clips.ToArray(),
            MainTexture = LoadAsset<Texture2D>(resourceAssetPath + "/" + mainTexture.Name),
            MaskTexture = maskTexture == null ? null : LoadAsset<Texture2D>(resourceAssetPath + "/" + maskTexture.Name),
            OutputDirectory = outputDirectory,
            ShaderName = shaderName,
            MaximumTextureSize = maximumTextureSize
        };
    }

    private static FileInfo FindSingleFile(FileInfo[] files, Predicate<FileInfo> matches, string description, bool required)
    {
        FileInfo match = null;
        foreach (FileInfo file in files)
        {
            if (!matches(file)) continue;
            if (match != null) throw new InvalidOperationException("Expected " + description + ", but multiple files match.");
            match = file;
        }
        if (match == null && required) throw new InvalidOperationException("Missing " + description + ".");
        return match;
    }

    private static T LoadAsset<T>(string assetPath) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset == null)
            throw new InvalidOperationException("Cannot load " + typeof(T).Name + " at " + assetPath + ". Check its Unity import errors.");
        return asset;
    }

    private static void ValidateRoleName(string name)
    {
        if (name.IndexOfAny(new[] { '<', '>', ':', '"', '/', '\\', '|', '?', '*' }) >= 0 ||
            name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(" ", StringComparison.Ordinal))
            throw new InvalidOperationException("Role folder names must be valid portable asset names (no <>:\"/\\|?*, trailing dot or space).");
        foreach (char character in name)
            if (char.IsControl(character))
                throw new InvalidOperationException("Role folder names cannot contain control characters.");
    }
}

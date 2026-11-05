using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Publishes a completely prepared bake while preserving the identities of existing assets.
/// The caller retains ownership of the request and the transient bake result.
/// </summary>
internal static class GpuAnimationAssetWriter
{
    private const ImportAssetOptions SynchronousImport =
        ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport;

    internal static string Save(GpuAnimationBakeRequest request, GpuAnimationBakeResult result)
    {
        return Save(request, result, null);
    }

    internal static void Validate(GpuAnimationBakeRequest request, GpuAnimationBakeResult result)
    {
        using (Prepare(request, result)) { }
    }

    // A per-call observer permits cancellation and deterministic rollback tests without global state.
    internal static string Save(GpuAnimationBakeRequest request, GpuAnimationBakeResult result,
        Action<string> beforeWriteAsset)
    {
        using (SavePlan plan = Prepare(request, result))
        using (AssetTransaction transaction = new AssetTransaction(plan.AssetPaths, plan.Folders))
        {
            try
            {
                EnsureFolders(plan.Folders);

                Observe(beforeWriteAsset, plan.MainTexturePath);
                WriteBytes(plan.MainTexturePath, plan.MainTextureBytes);
                Texture2D mainTexture = RequireAsset<Texture2D>(plan.MainTexturePath);

                Texture2D maskTexture = null;
                if (plan.MaskTexturePath != null)
                {
                    Observe(beforeWriteAsset, plan.MaskTexturePath);
                    WriteBytes(plan.MaskTexturePath, plan.MaskTextureBytes);
                    maskTexture = RequireAsset<Texture2D>(plan.MaskTexturePath);
                }

                Observe(beforeWriteAsset, plan.AnimationTexturePath);
                WriteBytes(plan.AnimationTexturePath, plan.AnimationTextureBytes);
                ConfigureAnimationTexture(plan);
                Texture2D animationTexture = RequireAsset<Texture2D>(plan.AnimationTexturePath);

                Observe(beforeWriteAsset, plan.MetadataPath);
                WriteBytes(plan.MetadataPath, plan.MetadataBytes);
                RequireAsset<TextAsset>(plan.MetadataPath);

                Observe(beforeWriteAsset, plan.MeshPath);
                Mesh mesh = SaveNativeAsset(plan.Mesh, plan.MeshPath);

                plan.Material.SetTexture("_MainTex", mainTexture);
                plan.Material.SetTexture("_Mask", maskTexture);
                plan.Material.SetTexture("_AnimationTex", animationTexture);
                Observe(beforeWriteAsset, plan.MaterialPath);
                Material material = SaveNativeAsset(plan.Material, plan.MaterialPath);
                // Flush only generated native assets; SaveAssets would also save unrelated dirty source assets.
                AssetDatabase.ForceReserializeAssets(new[] { plan.MeshPath, plan.MaterialPath },
                    ForceReserializeAssetsOptions.ReserializeAssets);
                if (EditorUtility.IsDirty(mesh) || EditorUtility.IsDirty(material) ||
                    !File.Exists(AbsolutePath(plan.MeshPath)) || !File.Exists(AbsolutePath(plan.MaterialPath)))
                    throw new IOException("生成网格或材质未能写入磁盘，请检查输出文件是否可写。");

                plan.Prefab.GetComponent<MeshFilter>().sharedMesh = mesh;
                plan.Prefab.GetComponent<MeshRenderer>().sharedMaterial = material;
                plan.Prefab.hideFlags = HideFlags.None;
                Observe(beforeWriteAsset, plan.PrefabPath);
                bool success;
                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(plan.Prefab, plan.PrefabPath, out success);
                if (!success || savedPrefab == null)
                    throw new IOException("无法保存生成的 prefab：" + plan.PrefabPath);

                AssetDatabase.ImportAsset(plan.PrefabPath, SynchronousImport);
                RequireAsset<GameObject>(plan.PrefabPath);
                return plan.PrefabPath;
            }
            catch (Exception saveException)
            {
                try
                {
                    transaction.Restore();
                }
                catch (Exception restoreException)
                {
                    throw new AggregateException("保存失败，恢复旧资源时也发生错误。备份目录：" +
                        transaction.BackupDirectory, saveException, restoreException);
                }
                throw;
            }
        }
    }

    private static void Observe(Action<string> observer, string assetPath)
    {
        if (observer != null)
            observer(assetPath);
    }

    private static SavePlan Prepare(GpuAnimationBakeRequest request, GpuAnimationBakeResult result)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (result == null)
            throw new ArgumentNullException(nameof(result));
        ValidateFileName(request.Name);
        string outputDirectory = NormalizeOutputDirectory(request.OutputDirectory);
        ValidateTextureSizeLimit(request.MaximumTextureSize);
        ValidateBakeData(result.Data, request.MaximumTextureSize);
        if (result.Mesh == null)
            throw new ArgumentException("烘焙结果没有网格。", nameof(result));

        string prefix = outputDirectory + "/" + request.Name;
        SavePlan plan = new SavePlan();
        try
        {
            plan.PrefabPath = prefix + "_DynPre.prefab";
            plan.MaterialPath = prefix + "_DynMat.mat";
            plan.AnimationTexturePath = prefix + "_Data.png";
            plan.MetadataPath = prefix + "_Data.bytes";
            plan.MainTexturePath = prefix + ".png";
            plan.MaskTexturePath = request.MaskTexture != null ? prefix + "_mask.png" : null;
            plan.Width = result.Data.texWidth;
            plan.Height = result.Data.texHeight;
            plan.MaximumTextureSize = request.MaximumTextureSize;

            ValidateExistingAsset<GameObject>(plan.PrefabPath);
            GameObject oldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(plan.PrefabPath);
            plan.MeshPath = ExistingMeshPath(oldPrefab, outputDirectory) ?? prefix + "_Mesh.asset";
            ValidateExistingAsset<Mesh>(plan.MeshPath);
            ValidateExistingAsset<Material>(plan.MaterialPath);
            ValidateExistingAsset<Texture2D>(plan.AnimationTexturePath);
            ValidateExistingAsset<Texture2D>(plan.MainTexturePath);
            ValidateExistingAsset<TextAsset>(plan.MetadataPath);
            if (plan.MaskTexturePath != null)
                ValidateExistingAsset<Texture2D>(plan.MaskTexturePath);

            RejectInputOverlap(request.Model, outputDirectory);
            if (request.Clips != null)
                foreach (AnimationClip clip in request.Clips)
                    RejectInputOverlap(clip, outputDirectory);
            RejectInputOverlap(request.MainTexture, outputDirectory);
            RejectInputOverlap(request.MaskTexture, outputDirectory);

            plan.MainTextureBytes = ReadPngAsset(request.MainTexture, "主贴图");
            if (request.MaskTexture != null)
                plan.MaskTextureBytes = ReadPngAsset(request.MaskTexture, "遮罩贴图");
            if (result.AnimationTexturePng == null)
                throw new ArgumentException("烘焙结果没有动画 PNG。", nameof(result));
            plan.AnimationTextureBytes = (byte[])result.AnimationTexturePng.Clone();
            ValidateAnimationPng(plan.AnimationTextureBytes, plan.Width, plan.Height);
            using (MemoryStream stream = new MemoryStream())
            {
                new BinaryFormatter().Serialize(stream, result.Data);
                plan.MetadataBytes = stream.ToArray();
            }

            Shader shader = string.IsNullOrEmpty(request.ShaderName) ? null : Shader.Find(request.ShaderName);
            if (shader == null)
                throw new ArgumentException("找不到输出 Shader：" + request.ShaderName, nameof(request));
            Material oldMaterial = AssetDatabase.LoadAssetAtPath<Material>(plan.MaterialPath);
            plan.Material = oldMaterial != null ? Object.Instantiate(oldMaterial) : new Material(shader);
            plan.Material.hideFlags = HideFlags.None;
            plan.Material.shader = shader;
            string[] controlledProperties = { "_MainTex", "_Mask", "_AnimationTex", "_BoneNum", "_AnimationTexSize" };
            foreach (string property in controlledProperties)
                if (!plan.Material.HasProperty(property))
                    throw new ArgumentException("输出 Shader 缺少动画属性：" + property, nameof(request));
            plan.Material.name = request.Name + "_DynMat";
            plan.Material.enableInstancing = true;
            plan.Material.SetInt("_BoneNum", result.Data.totalBoneNum);
            plan.Material.SetVector("_AnimationTexSize", new Vector4(plan.Width, plan.Height, 0, 0));

            plan.Mesh = Object.Instantiate(result.Mesh);
            plan.Mesh.hideFlags = HideFlags.None;
            plan.Mesh.name = Path.GetFileNameWithoutExtension(plan.MeshPath);

            plan.PreviewScene = EditorSceneManager.NewPreviewScene();
            // Prefab overwrites match objects by name; retain old roots to keep local file IDs stable.
            plan.Prefab = EditorUtility.CreateGameObjectWithHideFlags(
                oldPrefab != null ? oldPrefab.name : request.Name + "_DynPre", HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(plan.Prefab, plan.PreviewScene);
            plan.Prefab.hideFlags = HideFlags.HideAndDontSave;
            plan.Prefab.AddComponent<MeshFilter>().sharedMesh = plan.Mesh;
            MeshRenderer renderer = plan.Prefab.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = plan.Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            plan.Prefab.AddComponent<GpuAnimator>();

            plan.AssetPaths.Add(plan.MainTexturePath);
            if (plan.MaskTexturePath != null)
                plan.AssetPaths.Add(plan.MaskTexturePath);
            plan.AssetPaths.Add(plan.AnimationTexturePath);
            plan.AssetPaths.Add(plan.MetadataPath);
            plan.AssetPaths.Add(plan.MeshPath);
            plan.AssetPaths.Add(plan.MaterialPath);
            plan.AssetPaths.Add(plan.PrefabPath);
            foreach (string path in plan.AssetPaths)
                AddFolders(plan.Folders, Path.GetDirectoryName(path).Replace('\\', '/'));
            return plan;
        }
        catch
        {
            plan.Dispose();
            throw;
        }
    }

    private static void ValidateBakeData(AnimationData data, int maximumTextureSize)
    {
        if (data == null || data.texWidth <= 0 || data.texHeight <= 0 || data.totalBoneNum <= 0 ||
            data.totalFrame <= 0 || data.clips == null || data.clips.Length == 0)
            throw new ArgumentException("烘焙元数据不完整。");
        if (data.texWidth > maximumTextureSize || data.texHeight > maximumTextureSize)
            throw new ArgumentException("动画纹理超出选择的最大尺寸，不会缩小数值纹理。");
        if (SystemInfo.maxTextureSize > 0 &&
            (data.texWidth > SystemInfo.maxTextureSize || data.texHeight > SystemInfo.maxTextureSize))
            throw new ArgumentException("当前图形设备不能支持动画纹理的尺寸。");
        long requiredPixels = checked((long)data.totalBoneNum * data.totalFrame * 8);
        if (requiredPixels > (long)data.texWidth * data.texHeight)
            throw new ArgumentException("动画纹理容量不足以容纳全部骨骼帧。");
        foreach (AnimationClipData clip in data.clips)
            if (clip == null || string.IsNullOrEmpty(clip.name) || clip.startFrame < 0 ||
                clip.endFrame < clip.startFrame || clip.endFrame >= data.totalFrame ||
                clip.frameRate <= 0 || float.IsNaN(clip.frameRate) || float.IsInfinity(clip.frameRate))
                throw new ArgumentException("烘焙元数据包含无效动画片段。");
    }

    private static void ValidateTextureSizeLimit(int maximumTextureSize)
    {
        if (maximumTextureSize < 32 || maximumTextureSize > 16384 ||
            (maximumTextureSize & (maximumTextureSize - 1)) != 0)
            throw new ArgumentException("最大纹理尺寸必须为 32..16384 内的二次幂。");
    }

    private static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name == "." || name == ".." ||
            name.EndsWith(".", StringComparison.Ordinal))
            throw new ArgumentException("角色名称必须是有效的文件名。");
        const string invalidCharacters = "<>:\"/\\|?*";
        foreach (char character in name)
            if (char.IsControl(character) || invalidCharacters.IndexOf(character) >= 0 ||
                Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0)
                throw new ArgumentException("角色名称包含无效文件名字符。");
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '1' && stem[3] <= '9'))
            throw new ArgumentException("角色名称不能使用操作系统保留的文件名。");
    }

    private static string NormalizeOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("请选择 Assets 下的输出目录。");
        path = path.Replace('\\', '/').TrimEnd('/');
        if (!path.StartsWith("Assets/", StringComparison.Ordinal))
            throw new ArgumentException("输出目录必须位于 Assets 下。");
        string[] segments = path.Split('/');
        for (int index = 1; index < segments.Length; index++)
            ValidateFileName(segments[index]);
        string folder = "Assets";
        for (int index = 1; index < segments.Length; index++)
        {
            folder += "/" + segments[index];
            if (File.Exists(AbsolutePath(folder)))
                throw new ArgumentException("输出目录与现有文件冲突：" + folder);
            Object existing = AssetDatabase.LoadMainAssetAtPath(folder);
            if (existing != null && !string.Equals(AssetDatabase.GetAssetPath(existing), folder, StringComparison.Ordinal))
                throw new ArgumentException("输出目录的大小写与现有资源不一致：" + folder);
        }
        return path;
    }

    private static void RejectInputOverlap(Object input, string outputDirectory)
    {
        if (input == null)
            return;
        string path = AssetDatabase.GetAssetPath(input).Replace('\\', '/');
        if (IsWithin(path, outputDirectory))
            throw new ArgumentException("输出目录不能包含本次烘焙的输入资源：" + path);
        if (!string.IsNullOrEmpty(path))
            foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                if (IsWithin(dependency, outputDirectory))
                    throw new ArgumentException("输出目录不能覆盖输入资源的依赖：" + dependency);
    }

    private static bool IsWithin(string assetPath, string directory)
    {
        return string.Equals(assetPath, directory, StringComparison.OrdinalIgnoreCase) ||
            assetPath.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExistingMeshPath(GameObject prefab, string outputDirectory)
    {
        if (prefab == null)
            return null;
        MeshFilter filter = prefab.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return null;
        string path = AssetDatabase.GetAssetPath(filter.sharedMesh).Replace('\\', '/');
        return IsWithin(path, outputDirectory) && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
            AssetDatabase.LoadMainAssetAtPath(path) is Mesh ? path : null;
    }

    private static void ValidateExistingAsset<T>(string assetPath) where T : Object
    {
        ValidateWritableFile(AbsolutePath(assetPath));
        ValidateWritableFile(AbsolutePath(assetPath) + ".meta");
        if (Directory.Exists(AbsolutePath(assetPath)))
            throw new ArgumentException("输出文件路径与现有目录冲突：" + assetPath);
        Object existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
        if (existing != null)
        {
            if (!(existing is T))
                throw new ArgumentException("输出路径已被其他类型资源占用：" + assetPath);
            if (!string.Equals(AssetDatabase.GetAssetPath(existing), assetPath, StringComparison.Ordinal))
                throw new ArgumentException("输出文件的大小写与现有资源不一致：" + assetPath);
        }
        else if (File.Exists(AbsolutePath(assetPath)))
        {
            throw new ArgumentException("输出路径存在无法正确导入的文件：" + assetPath);
        }
    }

    private static void ValidateWritableFile(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
            throw new ArgumentException("输出文件为只读，请先使其可写：" + path);
    }

    private static byte[] ReadPngAsset(Texture2D texture, string label)
    {
        if (texture == null)
            throw new ArgumentException("请指定" + label + "。");
        string assetPath = AssetDatabase.GetAssetPath(texture).Replace('\\', '/');
        if (!assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(label + "必须是已导入的 PNG 资源。");
        string absolutePath = AbsolutePath(assetPath);
        if (!File.Exists(absolutePath) && assetPath.StartsWith("Packages/", StringComparison.Ordinal))
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (package != null)
                absolutePath = Path.Combine(package.resolvedPath,
                    assetPath.Substring(package.assetPath.Length).TrimStart('/'));
        }
        if (!File.Exists(absolutePath))
            throw new ArgumentException("找不到" + label + "的源文件：" + assetPath);
        return File.ReadAllBytes(absolutePath);
    }

    private static void ValidateAnimationPng(byte[] bytes, int width, int height)
    {
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes.Length < 33)
            throw new ArgumentException("动画 PNG 不完整。");
        for (int index = 0; index < signature.Length; index++)
            if (bytes[index] != signature[index])
                throw new ArgumentException("烘焙结果不是有效 PNG。");
        if (bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R' ||
            ReadBigEndianInt(bytes, 16) != width || ReadBigEndianInt(bytes, 20) != height ||
            bytes[24] != 8 || bytes[25] != 6)
            throw new ArgumentException("动画 PNG 必须为与元数据尺寸一致的 8 位 RGBA 图像。");
        Texture2D validationTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        try
        {
            if (!ImageConversion.LoadImage(validationTexture, bytes, false) ||
                validationTexture.width != width || validationTexture.height != height)
                throw new ArgumentException("动画 PNG 无法完整解码。");
        }
        finally
        {
            Object.DestroyImmediate(validationTexture);
        }
    }

    private static int ReadBigEndianInt(byte[] bytes, int offset)
    {
        return (bytes[offset] << 24) | (bytes[offset + 1] << 16) |
            (bytes[offset + 2] << 8) | bytes[offset + 3];
    }

    private static void ConfigureAnimationTexture(SavePlan plan)
    {
        TextureImporter importer = AssetImporter.GetAtPath(plan.AnimationTexturePath) as TextureImporter;
        if (importer == null)
            throw new IOException("动画 PNG 没有 TextureImporter：" + plan.AnimationTexturePath);
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = false;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.anisoLevel = 0;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.maxTextureSize = plan.MaximumTextureSize;

        TextureImporterPlatformSettings defaults = importer.GetDefaultPlatformTextureSettings();
        SetNumericTextureSettings(defaults, plan.MaximumTextureSize);
        importer.SetPlatformTextureSettings(defaults);
        HashSet<string> platforms = new HashSet<string>(StringComparer.Ordinal);
        foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
        {
            if (group == BuildTargetGroup.Unknown)
                continue;
            TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(group.ToString());
            if (!settings.overridden || !platforms.Add(settings.name))
                continue;
            SetNumericTextureSettings(settings, plan.MaximumTextureSize);
            importer.SetPlatformTextureSettings(settings);
        }
        importer.SaveAndReimport();
        Texture2D texture = RequireAsset<Texture2D>(plan.AnimationTexturePath);
        if (texture.width != plan.Width || texture.height != plan.Height || texture.format != TextureFormat.RGBA32)
            throw new IOException("动画纹理导入时发生尺寸或格式变化；已拒绝使用缩小或压缩的数值纹理。");
    }

    private static void SetNumericTextureSettings(TextureImporterPlatformSettings settings, int maximumTextureSize)
    {
        settings.maxTextureSize = maximumTextureSize;
        settings.format = TextureImporterFormat.RGBA32;
        settings.textureCompression = TextureImporterCompression.Uncompressed;
        settings.crunchedCompression = false;
    }

    private static void WriteBytes(string assetPath, byte[] bytes)
    {
        File.WriteAllBytes(AbsolutePath(assetPath), bytes);
        AssetDatabase.ImportAsset(assetPath, SynchronousImport);
    }

    private static T SaveNativeAsset<T>(T staged, string assetPath) where T : Object
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (existing != null)
        {
            EditorUtility.CopySerialized(staged, existing);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(staged, assetPath);
        return RequireAsset<T>(assetPath);
    }

    private static T RequireAsset<T>(string assetPath) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (asset == null)
            throw new IOException("资源未能按预期类型导入：" + assetPath);
        return asset;
    }

    private static void AddFolders(List<string> folders, string directory)
    {
        string[] segments = directory.Split('/');
        string folder = segments[0];
        for (int index = 1; index < segments.Length; index++)
        {
            folder += "/" + segments[index];
            if (!folders.Contains(folder))
                folders.Add(folder);
        }
    }

    private static void EnsureFolders(List<string> folders)
    {
        foreach (string folder in folders)
        {
            if (AssetDatabase.IsValidFolder(folder))
                continue;
            if (Directory.Exists(AbsolutePath(folder)))
            {
                AssetDatabase.ImportAsset(folder, SynchronousImport);
            }
            else
            {
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
            if (!AssetDatabase.IsValidFolder(folder))
                throw new IOException("无法创建输出目录：" + folder);
        }
    }

    private static string AbsolutePath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
    }

    private sealed class SavePlan : IDisposable
    {
        public string PrefabPath;
        public string MeshPath;
        public string MaterialPath;
        public string AnimationTexturePath;
        public string MetadataPath;
        public string MainTexturePath;
        public string MaskTexturePath;
        public int Width;
        public int Height;
        public int MaximumTextureSize;
        public byte[] MainTextureBytes;
        public byte[] MaskTextureBytes;
        public byte[] AnimationTextureBytes;
        public byte[] MetadataBytes;
        public Mesh Mesh;
        public Material Material;
        public GameObject Prefab;
        public Scene PreviewScene;
        public readonly List<string> AssetPaths = new List<string>();
        public readonly List<string> Folders = new List<string>();

        public void Dispose()
        {
            try
            {
                if (Prefab != null)
                    Object.DestroyImmediate(Prefab);
            }
            finally
            {
                if (PreviewScene.IsValid())
                    EditorSceneManager.ClosePreviewScene(PreviewScene);
                DestroyTransient(Mesh);
                DestroyTransient(Material);
            }
        }

        private static void DestroyTransient(Object asset)
        {
            if (asset != null && !EditorUtility.IsPersistent(asset))
                Object.DestroyImmediate(asset);
        }
    }

    private sealed class AssetTransaction : IDisposable
    {
        private readonly List<FileSnapshot> files = new List<FileSnapshot>();
        private readonly List<NativeSnapshot> nativeAssets = new List<NativeSnapshot>();
        private readonly List<string> assetPaths;
        private readonly List<string> createdFolders = new List<string>();
        private bool retainBackup;
        public string BackupDirectory { get; private set; }

        public AssetTransaction(List<string> assetPaths, List<string> folders)
        {
            this.assetPaths = new List<string>(assetPaths);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            BackupDirectory = Path.Combine(projectRoot, "Library", "GpuAnimatorBakeBackup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(BackupDirectory);
            try
            {
                foreach (string assetPath in assetPaths)
                {
                    Capture(AbsolutePath(assetPath));
                    Capture(AbsolutePath(assetPath) + ".meta");
                    Object existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    if (existing is Mesh || existing is Material)
                        nativeAssets.Add(new NativeSnapshot
                        {
                            AssetPath = assetPath,
                            Target = existing,
                            Value = Object.Instantiate(existing),
                            WasDirty = EditorUtility.IsDirty(existing)
                        });
                }
                foreach (string folder in folders)
                {
                    string absoluteFolder = AbsolutePath(folder);
                    if (!Directory.Exists(absoluteFolder))
                        createdFolders.Add(absoluteFolder);
                    Capture(absoluteFolder + ".meta");
                }
                WriteManifest(projectRoot);
            }
            catch
            {
                DisposeNativeSnapshots();
                Directory.Delete(BackupDirectory, true);
                throw;
            }
        }

        private void Capture(string path)
        {
            FileSnapshot snapshot = new FileSnapshot { Path = path, Existed = File.Exists(path) };
            if (snapshot.Existed)
            {
                snapshot.Attributes = File.GetAttributes(path);
                snapshot.BackupPath = System.IO.Path.Combine(BackupDirectory, files.Count.ToString());
                File.Copy(path, snapshot.BackupPath);
                File.SetAttributes(snapshot.BackupPath, FileAttributes.Normal);
            }
            files.Add(snapshot);
        }

        private void WriteManifest(string projectRoot)
        {
            BackupManifest manifest = new BackupManifest
            {
                projectRoot = projectRoot,
                createdFolders = createdFolders.ToArray()
            };
            foreach (FileSnapshot snapshot in files)
                manifest.files.Add(new BackupManifestFile
                {
                    originalPath = snapshot.Path,
                    existed = snapshot.Existed,
                    backupPath = snapshot.BackupPath,
                    attributes = (int)snapshot.Attributes
                });
            // Persist the path map before any asset write so retained backups remain recoverable
            // after a rollback failure or an interrupted Editor process. JsonUtility escapes paths.
            File.WriteAllText(System.IO.Path.Combine(BackupDirectory, "manifest.json"),
                JsonUtility.ToJson(manifest, true));
        }

        public void Restore()
        {
            retainBackup = true;
            try
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (FileSnapshot snapshot in files)
                    {
                        if (File.Exists(snapshot.Path))
                            File.SetAttributes(snapshot.Path, File.GetAttributes(snapshot.Path) & ~FileAttributes.ReadOnly);
                        if (snapshot.Existed)
                        {
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(snapshot.Path));
                            File.Copy(snapshot.BackupPath, snapshot.Path, true);
                            File.SetAttributes(snapshot.Path, snapshot.Attributes);
                        }
                        else if (File.Exists(snapshot.Path))
                        {
                            File.Delete(snapshot.Path);
                        }
                    }
                    for (int index = createdFolders.Count - 1; index >= 0; index--)
                    {
                        string directory = createdFolders[index];
                        if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
                            Directory.Delete(directory);
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                AssetDatabase.Refresh(SynchronousImport);
                foreach (string path in assetPaths)
                    if (File.Exists(AbsolutePath(path)))
                        AssetDatabase.ImportAsset(path, SynchronousImport);
            }
            finally
            {
                // Dirty native objects must not later overwrite the restored files with failed-bake data.
                foreach (NativeSnapshot snapshot in nativeAssets)
                {
                    Object target = snapshot.Target != null ? snapshot.Target :
                        AssetDatabase.LoadMainAssetAtPath(snapshot.AssetPath);
                    if (target == null)
                        throw new IOException("无法恢复资源的内存状态：" + snapshot.AssetPath);
                    EditorUtility.CopySerialized(snapshot.Value, target);
                    if (snapshot.WasDirty)
                        EditorUtility.SetDirty(target);
                    else
                        EditorUtility.ClearDirty(target);
                }
            }
            retainBackup = false;
        }

        public void Dispose()
        {
            DisposeNativeSnapshots();
            if (!retainBackup && Directory.Exists(BackupDirectory))
            {
                try
                {
                    Directory.Delete(BackupDirectory, true);
                }
                catch (Exception exception)
                {
                    // Cleanup must not hide the original save/restore failure or a completed save.
                    Debug.LogWarning("无法清理烘焙临时备份：" + BackupDirectory + "\n" + exception.Message);
                }
            }
        }

        private void DisposeNativeSnapshots()
        {
            foreach (NativeSnapshot snapshot in nativeAssets)
                if (snapshot.Value != null)
                    Object.DestroyImmediate(snapshot.Value);
            nativeAssets.Clear();
        }

        private sealed class NativeSnapshot
        {
            public string AssetPath;
            public Object Target;
            public Object Value;
            public bool WasDirty;
        }

        private sealed class FileSnapshot
        {
            public string Path;
            public bool Existed;
            public string BackupPath;
            public FileAttributes Attributes;
        }

        [Serializable]
        private sealed class BackupManifest
        {
            public int formatVersion = 1;
            public string projectRoot;
            public List<BackupManifestFile> files = new List<BackupManifestFile>();
            public string[] createdFolders;
        }

        [Serializable]
        private sealed class BackupManifestFile
        {
            public string originalPath;
            public bool existed;
            public string backupPath;
            public int attributes;
        }
    }
}

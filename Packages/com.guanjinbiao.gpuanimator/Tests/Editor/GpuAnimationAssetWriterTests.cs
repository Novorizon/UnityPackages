using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GpuAnimatorTests
{
    public sealed class GpuAnimationAssetWriterTests : GpuAnimationTestFixture
    {
        private string testRoot;

        [SetUp]
        public void CreateOwnedAssetDirectory()
        {
            testRoot = "Assets/__GpuAnimatorWriterTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(testRoot));
            string mainPath = testRoot + "/SourceMain.png";
            File.WriteAllBytes(AbsolutePath(mainPath), MainTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(mainPath, ImportAssetOptions.ForceSynchronousImport);
            MainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(mainPath);
            Assert.That(MainTexture, Is.Not.Null);
        }

        [TearDown]
        public void DeleteOnlyOwnedAssetDirectory()
        {
            if (!string.IsNullOrEmpty(testRoot) && AssetDatabase.IsValidFolder(testRoot))
                AssetDatabase.DeleteAsset(testRoot);
        }

        [Test]
        public void RepeatedSavePreservesGuidsAndDoesNotConsumeBakeResultMesh()
        {
            GpuAnimationBakeRequest request = WriterRequest(Clip("Move", "m_LocalPosition.x", 2));
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(request))
                GpuAnimationAssetWriter.Save(request, result);
            string[] paths = OutputPaths(request);
            Dictionary<string, string> guids = paths.ToDictionary(p => p, AssetDatabase.AssetPathToGUID);
            Assert.That(guids.Values.All(g => !string.IsNullOrEmpty(g)), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(paths[0]), Is.Not.Null, "Disposing the bake result must not destroy the saved mesh.");
            GameObject originalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[2]);
            long filterId = LocalIdentifier(originalPrefab.GetComponent<MeshFilter>());
            long rendererId = LocalIdentifier(originalPrefab.GetComponent<Renderer>());
            Material originalMaterial = AssetDatabase.LoadAssetAtPath<Material>(paths[1]);
            Color userColor = new Color(0.2f, 0.4f, 0.6f, 0.8f);
            originalMaterial.SetColor("_Color", userColor);
            EditorUtility.SetDirty(originalMaterial);
            AssetDatabase.ForceReserializeAssets(new[] { paths[1] }, ForceReserializeAssetsOptions.ReserializeAssets);

            request.Clips = new[] { Clip("Move", "m_LocalPosition.x", 5) };
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(request))
                GpuAnimationAssetWriter.Save(request, result);
            foreach (string path in paths)
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guids[path]), path);
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(paths[0]).bounds.max.x, Is.GreaterThanOrEqualTo(6));

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[2]);
            Assert.That(LocalIdentifier(prefab.GetComponent<MeshFilter>()), Is.EqualTo(filterId));
            Assert.That(LocalIdentifier(prefab.GetComponent<Renderer>()), Is.EqualTo(rendererId));
            Assert.That(AssetDatabase.GetAssetPath(prefab.GetComponent<MeshFilter>().sharedMesh), Is.EqualTo(paths[0]));
            Material material = prefab.GetComponent<Renderer>().sharedMaterial;
            Assert.That(material.enableInstancing, Is.True);
            Assert.That(material.GetColor("_Color"), Is.EqualTo(userColor));
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_AnimationTex")), Is.EqualTo(paths[3]));
        }

        [Test]
        public void AnimationTextureKeepsExactDimensionsBytesAndLinearImportSettings()
        {
            GpuAnimationBakeRequest request = WriterRequest(Clip("Move", "m_LocalPosition.x", 2));
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(request))
            {
                GpuAnimationAssetWriter.Save(request, result);
                string path = request.OutputDirectory + "/" + request.Name + "_Data.png";
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.That(importer, Is.Not.Null);
                Assert.That(importer.sRGBTexture, Is.False);
                Assert.That(importer.mipmapEnabled, Is.False);
                Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Point));
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
                Assert.That(importer.alphaIsTransparency, Is.False);
                Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput));
                Assert.That(importer.maxTextureSize, Is.EqualTo(request.MaximumTextureSize));
                Texture2D imported = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.That(imported.width, Is.EqualTo(result.Data.texWidth));
                Assert.That(imported.height, Is.EqualTo(result.Data.texHeight));
                Assert.That(imported.format, Is.EqualTo(TextureFormat.RGBA32));
                CollectionAssert.AreEqual(result.AnimationTexturePng, File.ReadAllBytes(AbsolutePath(path)));
                AnimationData decoded = AnimationData.FromBytes(File.ReadAllBytes(AbsolutePath(request.OutputDirectory + "/" + request.Name + "_Data.bytes")));
                Assert.That(decoded.totalFrame, Is.EqualTo(result.Data.totalFrame));
                Assert.That(decoded.clips[0].name, Is.EqualTo("Move"));
            }
        }

        [Test]
        public void WrongNativeAssetTypeCannotOverwriteExistingOutputs()
        {
            GpuAnimationBakeRequest request = WriterRequest(Clip("Move", "m_LocalPosition.x", 2));
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(request))
            {
                GpuAnimationAssetWriter.Save(request, result);
                string meshPath = request.OutputDirectory + "/" + request.Name + "_Mesh.asset";
                AssetDatabase.DeleteAsset(meshPath);
                Material wrongType = new Material(Shader.Find(request.ShaderName));
                AssetDatabase.CreateAsset(wrongType, meshPath);
                AssetDatabase.ForceReserializeAssets(new[] { meshPath }, ForceReserializeAssetsOptions.ReserializeAssets);
                Dictionary<string, byte[]> before = Snapshot();
                Assert.That(() => GpuAnimationAssetWriter.Save(request, result), Throws.Exception);
                AssertSnapshot(before);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(meshPath), Is.TypeOf<Material>());
            }
        }

        [Test]
        public void FailureAfterMeshAndMaterialUpdatesRestoresEveryFileAndGuid()
        {
            GpuAnimationBakeRequest request = WriterRequest(Clip("Move", "m_LocalPosition.x", 2));
            using (GpuAnimationBakeResult initial = GpuSkinningGenerator.Bake(request))
                GpuAnimationAssetWriter.Save(request, initial);
            Dictionary<string, byte[]> before = Snapshot();
            request.Clips = new[] { Clip("Move", "m_LocalPosition.x", 5) };
            using (GpuAnimationBakeResult changed = GpuSkinningGenerator.Bake(request))
            {
                bool faultReached = false;
                Assert.That(() => GpuAnimationAssetWriter.Save(request, changed, path =>
                {
                    if (path.EndsWith("_DynPre.prefab", StringComparison.Ordinal))
                    {
                        faultReached = true;
                        throw new IOException("Injected test failure before the final prefab write.");
                    }
                }), Throws.Exception);
                Assert.That(faultReached, Is.True, "The test must fail after earlier asset writes, not during validation.");
            }
            AssertSnapshot(before);
            string meshPath = request.OutputDirectory + "/" + request.Name + "_Mesh.asset";
            Assert.That(AssetDatabase.LoadAssetAtPath<Mesh>(meshPath).bounds.max.x, Is.LessThan(4));
        }

        [Test]
        public void FailedFirstSaveRemovesNewFilesAndDirectories()
        {
            GpuAnimationBakeRequest request = WriterRequest(Clip("Move", "m_LocalPosition.x", 2));
            Dictionary<string, byte[]> before = Snapshot();
            bool faultReached = false;
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(request))
                Assert.That(() => GpuAnimationAssetWriter.Save(request, result, path =>
                {
                    if (path.EndsWith("_DynPre.prefab", StringComparison.Ordinal))
                    {
                        faultReached = true;
                        throw new IOException("Injected first-save failure.");
                    }
                }), Throws.Exception);
            Assert.That(faultReached, Is.True, "The test must exercise rollback after writes, not fail during validation.");
            AssertSnapshot(before);
            Assert.That(AssetDatabase.IsValidFolder(request.OutputDirectory), Is.False);
        }

        [Test]
        public void ExistingPlatformOverridesCannotRecompressOrDownsizeAnimationData()
        {
            GpuAnimationBakeRequest request = WriterRequest(Clip("Move", "m_LocalPosition.x", 2));
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(request))
            {
                GpuAnimationAssetWriter.Save(request, result);
                string path = request.OutputDirectory + "/" + request.Name + "_Data.png";
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                TextureImporterPlatformSettings platform = importer.GetPlatformTextureSettings("Android");
                platform.name = "Android";
                platform.overridden = true;
                platform.maxTextureSize = 32;
                platform.format = TextureImporterFormat.Automatic;
                platform.textureCompression = TextureImporterCompression.Compressed;
                importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
                GpuAnimationAssetWriter.Save(request, result);
                importer = (TextureImporter)AssetImporter.GetAtPath(path);
                platform = importer.GetPlatformTextureSettings("Android");
                Assert.That(platform.format, Is.EqualTo(TextureImporterFormat.RGBA32));
                Assert.That(platform.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
                Assert.That(platform.maxTextureSize, Is.EqualTo(request.MaximumTextureSize));
            }
        }

        [Test]
        public void SavingBakeDoesNotSaveOrClearUnrelatedDirtyAssets()
        {
            GpuAnimationBakeRequest request = WriterRequest(Clip("Move", "m_LocalPosition.x", 2));
            string unrelatedPath = testRoot + "/Unrelated.mat";
            Material unrelated = new Material(Shader.Find(request.ShaderName));
            unrelated.SetColor("_Color", new Color(0.1f, 0.2f, 0.3f, 1));
            AssetDatabase.CreateAsset(unrelated, unrelatedPath);
            AssetDatabase.ForceReserializeAssets(new[] { unrelatedPath }, ForceReserializeAssetsOptions.ReserializeAssets);
            byte[] originalFile = File.ReadAllBytes(AbsolutePath(unrelatedPath));

            Color unsavedColor = new Color(0.9f, 0.8f, 0.7f, 0.6f);
            unrelated.SetColor("_Color", unsavedColor);
            EditorUtility.SetDirty(unrelated);
            Assert.That(EditorUtility.IsDirty(unrelated), Is.True);

            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(request))
                GpuAnimationAssetWriter.Save(request, result);

            CollectionAssert.AreEqual(originalFile, File.ReadAllBytes(AbsolutePath(unrelatedPath)), "The bake must not persist unrelated unsaved edits.");
            Assert.That(EditorUtility.IsDirty(unrelated), Is.True, "The unrelated asset must remain dirty for its owner to save.");
            Assert.That(unrelated.GetColor("_Color"), Is.EqualTo(unsavedColor), "The unrelated in-memory edit must remain intact.");
        }

        [Test]
        public void BackupManifestMapsExistingFilesAndNewFoldersBeforeFirstAssetWrite()
        {
            GpuAnimationBakeRequest request = WriterRequest(Clip("Move", "m_LocalPosition.x", 2));
            string existingOutput = request.OutputDirectory;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string backupRoot = Path.Combine(projectRoot, "Library", "GpuAnimatorBakeBackup");
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(request))
            {
                GpuAnimationAssetWriter.Save(request, result);
                Dictionary<string, byte[]> originalOutputs = Snapshot();
                for (int scenario = 0; scenario < 2; scenario++)
                {
                    bool existingFiles = scenario == 0;
                    request.OutputDirectory = existingFiles ? existingOutput : testRoot + "/NewNested/Output";
                    HashSet<string> previousBackups = new HashSet<string>(
                        Directory.Exists(backupRoot) ? Directory.GetDirectories(backupRoot) : Array.Empty<string>(), StringComparer.Ordinal);
                    string observedBackup = null;
                    bool observedBeforeWrite = false;
                    Assert.That(() => GpuAnimationAssetWriter.Save(request, result, path =>
                    {
                        observedBeforeWrite = true;
                        observedBackup = Directory.GetDirectories(backupRoot).Single(directory => !previousBackups.Contains(directory));
                        string manifestPath = Path.Combine(observedBackup, "manifest.json");
                        Assert.That(File.Exists(manifestPath), Is.True);
                        Assert.That(Path.GetFullPath(AbsolutePath(path)), Is.EqualTo(Path.GetFullPath(AbsolutePath(request.OutputDirectory + "/" + request.Name + ".png"))));
                        Assert.That(File.Exists(AbsolutePath(path)), Is.EqualTo(existingFiles), "The first asset write has not happened yet.");
                        BackupManifest manifest = JsonUtility.FromJson<BackupManifest>(File.ReadAllText(manifestPath));
                        Assert.That(manifest.formatVersion, Is.EqualTo(1));
                        Assert.That(Path.GetFullPath(manifest.projectRoot), Is.EqualTo(Path.GetFullPath(projectRoot)));
                        Assert.That(manifest.files, Is.Not.Null);
                        Assert.That(manifest.createdFolders, Is.Not.Null);
                        string meshPath = Path.GetFullPath(AbsolutePath(request.OutputDirectory + "/" + request.Name + "_Mesh.asset"));
                        BackupManifestFile meshFile = manifest.files.Single(file => Path.GetFullPath(file.originalPath) == meshPath);
                        Assert.That(meshFile.existed, Is.EqualTo(existingFiles));
                        if (existingFiles)
                        {
                            Assert.That(Path.IsPathRooted(meshFile.backupPath), Is.True);
                            Assert.That(File.Exists(meshFile.backupPath), Is.True);
                            CollectionAssert.AreEqual(File.ReadAllBytes(meshPath), File.ReadAllBytes(meshFile.backupPath));
                            Assert.That(meshFile.attributes, Is.EqualTo((int)File.GetAttributes(meshPath)));
                            Assert.That(manifest.createdFolders.Length, Is.Zero);
                        }
                        else
                        {
                            Assert.That(string.IsNullOrEmpty(meshFile.backupPath), Is.True);
                            Assert.That(meshFile.attributes, Is.Zero);
                            CollectionAssert.AreEquivalent(new[]
                            {
                                Path.GetFullPath(AbsolutePath(testRoot + "/NewNested")),
                                Path.GetFullPath(AbsolutePath(request.OutputDirectory))
                            }, manifest.createdFolders.Select(Path.GetFullPath));
                            Assert.That(File.Exists(meshPath), Is.False, "The observer must inspect the manifest before generated files are written.");
                        }
                        throw new IOException("Stop after verifying the persisted pre-write backup manifest.");
                    }), Throws.TypeOf<IOException>());
                    Assert.That(observedBeforeWrite, Is.True);
                    Assert.That(Directory.Exists(observedBackup), Is.False, "A fully rolled-back transaction must remove its backup manifest and copies.");
                }
                AssertSnapshot(originalOutputs);
            }
        }

        private GpuAnimationBakeRequest WriterRequest(params AnimationClip[] clips)
        {
            GpuAnimationBakeRequest request = Request(clips);
            request.OutputDirectory = testRoot + "/Output";
            return request;
        }

        private static string[] OutputPaths(GpuAnimationBakeRequest request)
        {
            string prefix = request.OutputDirectory + "/" + request.Name;
            return new[] { prefix + "_Mesh.asset", prefix + "_DynMat.mat", prefix + "_DynPre.prefab", prefix + "_Data.png", prefix + "_Data.bytes", prefix + ".png" };
        }

        private static long LocalIdentifier(UnityEngine.Object asset)
        {
            Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long identifier), Is.True);
            Assert.That(guid, Is.Not.Empty);
            return identifier;
        }

        [Serializable]
        private sealed class BackupManifest
        {
            public int formatVersion = 0;
            public string projectRoot = null;
            public List<BackupManifestFile> files = null;
            public string[] createdFolders = null;
        }

        [Serializable]
        private sealed class BackupManifestFile
        {
            public string originalPath = null;
            public bool existed = false;
            public string backupPath = null;
            public int attributes = 0;
        }

        private Dictionary<string, byte[]> Snapshot()
        {
            return Directory.GetFiles(AbsolutePath(testRoot), "*", SearchOption.AllDirectories)
                .ToDictionary(path => path.Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
        }

        private static string AbsolutePath(string assetPath)
        {
            return Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath);
        }

        private void AssertSnapshot(Dictionary<string, byte[]> expected)
        {
            Dictionary<string, byte[]> actual = Snapshot();
            CollectionAssert.AreEquivalent(expected.Keys, actual.Keys);
            foreach (string path in expected.Keys)
                CollectionAssert.AreEqual(expected[path], actual[path], path);
        }
    }
}

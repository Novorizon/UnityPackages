using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GpuAnimatorTests
{
    public sealed class GpuAnimatorPlaybackTests : GpuAnimationTestFixture
    {
        [Test]
        public void BuiltinBakeSaveDecodeInjectPlayAndClear()
        {
            Assert.That(GraphicsSettings.currentRenderPipeline, Is.Null, "This acceptance fixture targets the Built-in Render Pipeline.");
            string root = "Assets/__GpuAnimatorPlaybackTests_" + Guid.NewGuid().ToString("N");
            GameObject instance = null;
            try
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(root));
                string texturePath = root + "/Source.png";
                File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, texturePath), MainTexture.EncodeToPNG());
                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                MainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                var request = Request(Clip("Move", "m_LocalPosition.x", 2));
                request.OutputDirectory = root + "/Baked";
                string prefabPath;
                using (var result = GpuSkinningGenerator.Bake(request)) prefabPath = GpuAnimationAssetWriter.Save(request, result);
                var metadata = AssetDatabase.LoadAssetAtPath<TextAsset>(request.OutputDirectory + "/" + request.Name + "_Data.bytes");
                AnimationData shared = AnimationData.FromBytes(metadata.bytes);
                instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
                var renderer = instance.GetComponent<Renderer>();
                var player = instance.GetComponent<GpuAnimator>();
                Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo(request.ShaderName));
                Assert.That(renderer.sharedMaterial.GetTexture("_AnimationTex"), Is.Not.Null);
                Assert.That(player.Initialize(renderer, shared), Is.True);
                player.AutomaticUpdate = false;
                int completed = 0;
                Assert.That(player.TryPlay("Move", (p, value) => completed++, loop: false), Is.True);
                player.Tick(2);
                player.Tick(2);
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(player.CurrentFrame, Is.EqualTo(shared.clips[0].endFrame));
                Assert.That(player.TryPlay("Move", (p, value) => completed++, loop: false), Is.True);
                player.Clear();
                player.Tick(2);
                Assert.That(player.IsInitialized, Is.False);
                Assert.That(player.IsPlaying, Is.False);
                Assert.That(player.CurrentClipName, Is.Null);
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(player.Initialize(renderer, shared), Is.True);
                Assert.That(player.TryPlay("Move", loop: true), Is.True);
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                if (AssetDatabase.IsValidFolder(root)) AssetDatabase.DeleteAsset(root);
            }
        }
    }
}

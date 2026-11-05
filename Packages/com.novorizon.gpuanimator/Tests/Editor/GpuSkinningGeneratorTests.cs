using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GpuAnimatorTests
{
    public sealed class GpuSkinningGeneratorTests : GpuAnimationTestFixture
    {
        [Test]
        public void OneShotEndpointIsEncodedAndIncludedInAnimationBounds()
        {
            AnimationClip clip = Clip("Move", "m_LocalPosition.x", 2);
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(Request(clip)))
            {
                Assert.That(result.Data.totalBoneNum, Is.EqualTo(1));
                Assert.That(result.Data.clips[0].Length(), Is.EqualTo(3));
                Color32[] pixels = Pixels(result);
                int finalFrame = result.Data.clips[0].endFrame;
                Assert.That(GpuAnimationBakeUtilityTests.Decode(pixels[finalFrame * 8 + 4]), Is.EqualTo(2).Within(0.00001f));
                Assert.That(result.Mesh.bounds.max.x, Is.GreaterThanOrEqualTo(3));
                Assert.That(result.Mesh.bounds.min.x, Is.LessThanOrEqualTo(0));
                Assert.That(result.Mesh.bounds.max.y, Is.GreaterThanOrEqualTo(1));
            }
        }

        [Test]
        public void PartialTracksStartFromSameRestPoseRegardlessOfClipInputOrder()
        {
            AnimationClip x = Clip("A_MoveX", "m_LocalPosition.x", 2);
            AnimationClip y = Clip("B_MoveY", "m_LocalPosition.y", 3);
            using (GpuAnimationBakeResult first = GpuSkinningGenerator.Bake(Request(x, y)))
            using (GpuAnimationBakeResult second = GpuSkinningGenerator.Bake(Request(y, x)))
            {
                CollectionAssert.AreEqual(first.AnimationTexturePng, second.AnimationTexturePng);
                CollectionAssert.AreEqual(new[] { "A_MoveX", "B_MoveY" }, second.Data.clips.Select(c => c.name));
                AnimationClipData yData = first.Data.clips.Single(c => c.name == "B_MoveY");
                Color32[] pixels = Pixels(first);
                Assert.That(GpuAnimationBakeUtilityTests.Decode(pixels[yData.endFrame * 8 + 4]), Is.Zero);
                Assert.That(GpuAnimationBakeUtilityTests.Decode(pixels[yData.endFrame * 8 + 5]), Is.EqualTo(3).Within(0.00001f));
            }
        }

        [Test]
        public void BakingLeavesSourceHierarchyMeshAndClipUnmodified()
        {
            Model.transform.position = new Vector3(4, 5, 6);
            Model.transform.rotation = Quaternion.Euler(0, 35, 0);
            Bone.localPosition = new Vector3(0.25f, 0.5f, 0.75f);
            AnimationClip clip = Clip("Move", "m_LocalPosition.x", 2);
            Vector3 rootPosition = Model.transform.position;
            Quaternion rootRotation = Model.transform.rotation;
            Vector3 rest = Bone.localPosition;
            Vector3[] vertices = SourceMesh.vertices;
            EditorCurveBinding binding = AnimationUtility.GetCurveBindings(clip).Single();
            Keyframe[] originalKeys = AnimationUtility.GetEditorCurve(clip, binding).keys;
            int previewScenes = EditorSceneManager.previewSceneCount;
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(Request(clip)))
                Assert.That(result.Mesh, Is.Not.SameAs(SourceMesh));
            Assert.That(Model.transform.position, Is.EqualTo(rootPosition));
            Assert.That(Model.transform.rotation, Is.EqualTo(rootRotation));
            Assert.That(Bone.localPosition, Is.EqualTo(rest));
            CollectionAssert.AreEqual(vertices, SourceMesh.vertices);
            CollectionAssert.AreEqual(originalKeys, AnimationUtility.GetEditorCurve(clip, binding).keys);
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previewScenes));
        }

        [Test]
        public void CancellationLeavesSourceAndProjectUntouched()
        {
            GpuAnimationBakeRequest request = Request(Clip("Move", "m_LocalPosition.x", 2));
            request.OutputDirectory = "Assets/__GpuAnimatorCanceled_" + Guid.NewGuid().ToString("N");
            Vector3 rest = Bone.localPosition;
            int previewScenes = EditorSceneManager.previewSceneCount;
            bool canceledWithPreviewOpen = false;
            Assert.That(() => GpuSkinningGenerator.Bake(request, (progress, message) =>
            {
                if (progress < 0.2f)
                    return false;
                canceledWithPreviewOpen = EditorSceneManager.previewSceneCount > previewScenes;
                return true;
            }), Throws.TypeOf<OperationCanceledException>());
            Assert.That(canceledWithPreviewOpen, Is.True, "Cancellation must exercise cleanup after sampling has begun.");
            Assert.That(Bone.localPosition, Is.EqualTo(rest));
            string outputPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, request.OutputDirectory);
            Assert.That(Directory.Exists(outputPath), Is.False);
            Assert.That(EditorSceneManager.previewSceneCount, Is.EqualTo(previewScenes));
        }

        [Test]
        public void DuplicateNamesMissingUvsAndMultipleSubmeshesAreRejected()
        {
            AnimationClip a = Clip("SameName", "m_LocalPosition.x", 1);
            AnimationClip b = Clip("SameName", "m_LocalPosition.y", 1);
            Assert.That(() => GpuSkinningGenerator.Bake(Request(a, b)), Throws.InstanceOf<ArgumentException>());
            SourceMesh.uv = Array.Empty<Vector2>();
            Assert.That(() => GpuSkinningGenerator.Bake(Request(a)), Throws.InstanceOf<ArgumentException>());
            SourceMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            SourceMesh.subMeshCount = 2;
            Assert.That(() => GpuSkinningGenerator.Bake(Request(a)), Throws.InstanceOf<ArgumentException>());
        }

        [Test]
        public void UnrepresentableAnimatedScaleFailsWithoutMutatingSource()
        {
            AnimationClip clip = Clip("ScaleOnlyX", "m_LocalScale.x", 2);
            Assert.That(() => GpuSkinningGenerator.Bake(Request(clip)), Throws.InstanceOf<InvalidOperationException>());
            Assert.That(Bone.localScale, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void LoopClipDoesNotBakeDuplicateEndpoint()
        {
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(Request(Clip("Loop", "m_LocalPosition.x", 2, true))))
            {
                Assert.That(result.Data.clips[0].loop, Is.True);
                Assert.That(result.Data.clips[0].Length(), Is.EqualTo(2));
                Color32[] pixels = Pixels(result);
                Assert.That(GpuAnimationBakeUtilityTests.Decode(pixels[8 + 4]), Is.EqualTo(1).Within(0.00001f));
            }
        }

        [Test]
        public void FiveActualBoneInfluencesAreRejectedBeforeLegacyWeightTruncation()
        {
            Transform[] bones = new Transform[5];
            bones[0] = Bone;
            for (int i = 1; i < bones.Length; i++)
            {
                bones[i] = new GameObject("ExtraBone" + i).transform;
                bones[i].SetParent(Model.transform, false);
            }
            Model.GetComponent<SkinnedMeshRenderer>().bones = bones;
            SourceMesh.bindposes = bones.Select(b => b.worldToLocalMatrix * Model.transform.localToWorldMatrix).ToArray();
            NativeArray<byte> counts = new NativeArray<byte>(new byte[] { 5, 5, 5 }, Allocator.Temp);
            NativeArray<BoneWeight1> weights = new NativeArray<BoneWeight1>(15, Allocator.Temp);
            try
            {
                for (int vertex = 0; vertex < 3; vertex++)
                    for (int bone = 0; bone < 5; bone++)
                        weights[vertex * 5 + bone] = new BoneWeight1 { boneIndex = bone, weight = 0.2f };
                SourceMesh.SetBoneWeights(counts, weights);
            }
            finally
            {
                weights.Dispose();
                counts.Dispose();
            }
            Assert.That(() => GpuSkinningGenerator.Bake(Request(Clip("Move", "m_LocalPosition.x", 1))), Throws.InstanceOf<ArgumentException>());
        }

        [TestCase(50f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ActiveOrNonfiniteDefaultBlendShapesAreRejected(float weight)
        {
            AddUnusedBlendShape();
            SkinnedMeshRenderer renderer = Model.GetComponent<SkinnedMeshRenderer>();
            renderer.SetBlendShapeWeight(0, weight);
            try
            {
                Assert.That(() => GpuSkinningGenerator.Bake(Request(Clip("Move", "m_LocalPosition.x", 1))), Throws.InstanceOf<ArgumentException>());
            }
            finally
            {
                renderer.SetBlendShapeWeight(0, 0);
            }
        }

        [Test]
        public void UnusedZeroWeightBlendShapesDoNotRejectValidBaseMesh()
        {
            AddUnusedBlendShape();
            Model.GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(0, 0);
            using (GpuAnimationBakeResult result = GpuSkinningGenerator.Bake(Request(Clip("Move", "m_LocalPosition.x", 1))))
            {
                Assert.That(result.Data.totalBoneNum, Is.EqualTo(1));
                CollectionAssert.AreEqual(SourceMesh.vertices, result.Mesh.vertices);
            }
        }

        [Test]
        public void MeshWithVerticesButNoTrianglesCannotProduceAnEmptyPrefab()
        {
            SourceMesh.triangles = Array.Empty<int>();
            Assert.That(SourceMesh.vertexCount, Is.EqualTo(3));
            Assert.That(SourceMesh.GetIndexCount(0), Is.Zero);
            Assert.That(() => GpuSkinningGenerator.Bake(Request(Clip("Move", "m_LocalPosition.x", 1))), Throws.InstanceOf<ArgumentException>());
        }

        private void AddUnusedBlendShape()
        {
            SourceMesh.AddBlendShapeFrame("Expression", 100,
                new[] { Vector3.right * 0.5f, Vector3.zero, Vector3.zero },
                new Vector3[3], new Vector3[3]);
        }
    }
}

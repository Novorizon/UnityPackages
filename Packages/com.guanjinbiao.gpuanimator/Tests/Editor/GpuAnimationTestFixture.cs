using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GpuAnimatorTests
{
    public abstract class GpuAnimationTestFixture
    {
        private readonly List<Object> ownedObjects = new List<Object>();
        protected GameObject Model;
        protected Transform Bone;
        protected Mesh SourceMesh;
        protected Texture2D MainTexture;

        [SetUp]
        public void CreateSyntheticModel()
        {
            Model = Own(new GameObject("SyntheticRole"));
            Bone = new GameObject("Bone").transform;
            Bone.SetParent(Model.transform, false);
            SourceMesh = Own(new Mesh { name = "SyntheticMesh" });
            SourceMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            SourceMesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            SourceMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            SourceMesh.triangles = new[] { 0, 1, 2 };
            SourceMesh.bindposes = new[] { Bone.worldToLocalMatrix * Model.transform.localToWorldMatrix };
            SourceMesh.boneWeights = new[] { Weight(), Weight(), Weight() };
            SourceMesh.RecalculateBounds();
            SkinnedMeshRenderer renderer = Model.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = SourceMesh;
            renderer.rootBone = Bone;
            renderer.bones = new[] { Bone };
            renderer.localBounds = SourceMesh.bounds;
            MainTexture = Own(new Texture2D(1, 1, TextureFormat.RGBA32, false));
            MainTexture.SetPixel(0, 0, Color.white);
            MainTexture.Apply();
        }

        [TearDown]
        public void DestroyOwnedObjects()
        {
            for (int i = ownedObjects.Count - 1; i >= 0; i--)
                if (ownedObjects[i] != null)
                    Object.DestroyImmediate(ownedObjects[i]);
            ownedObjects.Clear();
        }

        protected T Own<T>(T value) where T : Object
        {
            ownedObjects.Add(value);
            return value;
        }

        protected AnimationClip Clip(string name, string property, float endValue, bool loop = false)
        {
            AnimationClip clip = Own(new AnimationClip { name = name, frameRate = 2 });
            clip.SetCurve("Bone", typeof(Transform), property, AnimationCurve.Linear(0, 0, 1, endValue));
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        internal GpuAnimationBakeRequest Request(params AnimationClip[] clips)
        {
            return new GpuAnimationBakeRequest
            {
                Name = "SyntheticRole",
                Model = Model,
                Clips = clips,
                MainTexture = MainTexture,
                ShaderName = "Custom/GpuSkinningAnimation",
                MaximumTextureSize = 64
            };
        }

        internal Color32[] Pixels(GpuAnimationBakeResult result)
        {
            Texture2D texture = Own(new Texture2D(2, 2, TextureFormat.RGBA32, false, true));
            Assert.That(texture.LoadImage(result.AnimationTexturePng, false), Is.True);
            Assert.That(texture.width, Is.EqualTo(result.Data.texWidth));
            Assert.That(texture.height, Is.EqualTo(result.Data.texHeight));
            return texture.GetPixels32();
        }

        private static BoneWeight Weight()
        {
            return new BoneWeight { boneIndex0 = 0, weight0 = 1 };
        }
    }
}

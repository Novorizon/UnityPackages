using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public sealed class NativeGpuPixelsTests
{
    [Test]
    public void LitDepthNormalsReadsAnimatedPositionsAndNormalsFromTheActualGpu()
    {
        var shader = Shader.Find("Novorizon/GpuSkinning/URP Lit");
        Assert.That(shader, Is.Not.Null);
        var material = new Material(shader);
        var mesh = new Mesh();
        var animation = new Texture2D(16, 1, TextureFormat.RGBA32, false, true);
        var target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var pixels = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
        var command = new CommandBuffer();
        RenderTexture previous = RenderTexture.active;
        try
        {
            mesh.vertices = new[] { new Vector3(-.25f, -.25f, 0), new Vector3(.25f, -.25f, 0), new Vector3(.25f, .25f, 0), new Vector3(-.25f, .25f, 0) };
            mesh.normals = Enumerable.Repeat(Vector3.right, 4).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(0, 1, 0, 1), 4).ToArray();
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
            mesh.SetUVs(1, new List<Vector4>(Enumerable.Repeat(Vector4.zero, 4)));
            mesh.SetUVs(2, new List<Vector4>(Enumerable.Repeat(new Vector4(1, 0, 0, 0), 4)));
            Quaternion rotated = Quaternion.Euler(0, 0, 90);
            float[] frames = { 0, 0, 0, 1, 0, 0, 0, 1, rotated.x, rotated.y, rotated.z, rotated.w, .5f, 0, 0, 1 };
            animation.SetPixels32(frames.Select(Encode).ToArray());
            animation.filterMode = FilterMode.Point;
            animation.wrapMode = TextureWrapMode.Clamp;
            animation.Apply(false, false);
            material.SetTexture("_AnimationTex", animation);
            material.SetVector("_AnimationTexSize", new Vector4(16, 1, 0, 0));
            material.SetInt("_BoneNum", 1);
            material.DisableKeyword("_GBUFFER_NORMALS_OCT");
            target.Create();
            int pass = material.FindPass("DepthNormals");
            Assert.That(pass, Is.GreaterThanOrEqualTo(0));
            Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.Translate(new Vector3(0, 0, 2));
            Matrix4x4 projection = GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1, 1, -1, 1, .1f, 10), true);
            for (int frame = 0; frame < 2; frame++)
            {
                var properties = new MaterialPropertyBlock();
                properties.SetInt("_FrameIndex", frame);
                command.Clear();
                command.SetRenderTarget(target);
                command.ClearRenderTarget(true, true, new Color(0, 0, 0, 1));
                command.SetViewport(new Rect(0, 0, 64, 64));
                command.SetViewProjectionMatrices(view, projection);
                command.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass, properties);
                Graphics.ExecuteCommandBuffer(command);
                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                pixels.Apply();
                Color center = pixels.GetPixel(frame == 0 ? 32 : 48, 32);
                Assert.That(center.a, Is.LessThan(.1f), "Animated silhouette must cover the expected pixel.");
                if (frame == 0)
                {
                    Assert.That(center.r, Is.GreaterThan(.9f), "Identity bone keeps the input normal along +X.");
                    Assert.That(Math.Abs(center.g), Is.LessThan(.1f));
                }
                else
                {
                    Assert.That(center.g, Is.GreaterThan(.9f), "The 90-degree bone rotation must rotate the normal to +Y.");
                    Assert.That(Math.Abs(center.r), Is.LessThan(.1f));
                    Assert.That(pixels.GetPixel(32, 32).a, Is.GreaterThan(.9f), "Translation must move the mesh away from the old center.");
                }
            }
        }
        finally
        {
            RenderTexture.active = previous;
            command.Release();
            target.Release();
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(pixels);
            Object.DestroyImmediate(animation);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(material);
        }
    }

    static Color32 Encode(float value)
    {
        // Encode the documented fixed-point texture contract independently of the baker.
        uint magnitude = (uint)Math.Floor(Math.Abs((double)value) * 16777216);
        return new Color32(
            (byte)((magnitude >> 24) | (value < 0 ? 128u : 0u)),
            (byte)(magnitude >> 16),
            (byte)(magnitude >> 8),
            (byte)magnitude);
    }
}

using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace GpuAnimatorTests
{
    public sealed class GpuSkinningNormalGpuTests
    {
        [Test]
        public void SharedBuiltinNormalMathMatchesInverseTransposeOnTheActualGpu()
        {
            var shader = Shader.Find("Hidden/Novorizon/GpuSkinningNormalAudit");
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
            var material = new Material(shader);
            var mesh = new Mesh();
            var target = new RenderTexture(8, 8, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(8, 8, TextureFormat.RGBAFloat, false, true);
            var commands = new CommandBuffer();
            RenderTexture previous = RenderTexture.active;
            try
            {
                mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                Vector3 normal = new Vector3(1, 0, 1).normalized;
                material.SetVector("_SourceNormal", normal);
                target.Create();
                Matrix4x4 mixed = Blend(
                    Matrix4x4.identity,
                    Matrix4x4.Rotate(Quaternion.Euler(0, 0, 120)),
                    .5f);
                // Positive/negative blended transforms, a reflection, small valid scale,
                // and collapse all share the exact helper used by the shipped PBR shader.
                var transforms = new[]
                {
                    mixed,
                    Matrix4x4.Scale(Vector3.one * -1) * mixed,
                    Matrix4x4.Scale(Vector3.one * -2),
                    Matrix4x4.Scale(Vector3.one * 1e-8f) * mixed,
                    Matrix4x4.Scale(Vector3.zero)
                };
                for (int i = 0; i < transforms.Length; i++)
                {
                    Matrix4x4 transform = transforms[i];
                    material.SetVector("_SkinRow0", transform.GetRow(0));
                    material.SetVector("_SkinRow1", transform.GetRow(1));
                    material.SetVector("_SkinRow2", transform.GetRow(2));
                    commands.Clear();
                    commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(false, true, Color.clear);
                    commands.SetViewport(new Rect(0, 0, 8, 8));
                    commands.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                    Graphics.ExecuteCommandBuffer(commands);
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 8, 8), 0, 0);
                    pixels.Apply();
                    Color pixel = pixels.GetPixel(4, 4);
                    Assert.That(pixel.a, Is.GreaterThan(.99f), "The audit mesh must cover the readback pixel.");
                    Vector3 actual = new Vector3(pixel.r * 2 - 1, pixel.g * 2 - 1, pixel.b * 2 - 1);
                    Vector3 expected = i == transforms.Length - 1 ? normal : transform.inverse.transpose.MultiplyVector(normal).normalized;
                    Assert.That(actual.x, Is.EqualTo(expected.x).Within(.003f), "Transform " + i + " normal x");
                    Assert.That(actual.y, Is.EqualTo(expected.y).Within(.003f), "Transform " + i + " normal y");
                    Assert.That(actual.z, Is.EqualTo(expected.z).Within(.003f), "Transform " + i + " normal z");
                    Assert.That(actual.sqrMagnitude, Is.EqualTo(1).Within(.006f));
                }
            }
            finally
            {
                RenderTexture.active = previous;
                commands.Release();
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }

        private static Matrix4x4 Blend(Matrix4x4 left, Matrix4x4 right, float weight)
        {
            var result = new Matrix4x4();
            for (int i = 0; i < 16; i++)
            {
                result[i] = left[i] * (1 - weight) + right[i] * weight;
            }
            return result;
        }
    }
}

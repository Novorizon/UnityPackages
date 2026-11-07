using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Game.Framework.GpuAnimation.URP.Editor;

namespace Game.Framework.GpuAnimation.URP.Tests
{
    public sealed class UrpShaderTests
    {
        [Test]
        public void UrpShaderImportsWithTheExistingAnimationContractAndDepthPass()
        {
            var shader = Shader.Find("Novorizon/GpuSkinning/URP Unlit");
            Assert.That(shader, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            var material = new Material(shader);
            try
            {
                foreach (string property in new[] { "_MainTex", "_Mask", "_AnimationTex", "_AnimationTexSize", "_BoneNum", "_FrameIndex" })
                {
                    Assert.That(material.HasProperty(property), Is.True, property);
                }

                Assert.That(material.FindPass("ForwardUnlit"), Is.GreaterThanOrEqualTo(0));
                Assert.That(material.FindPass("DepthOnly"), Is.GreaterThanOrEqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ConfigurePreservesAnExistingGraphicsOrQualityPipeline()
        {
            var previousGraphics = GraphicsSettings.defaultRenderPipeline;
            var previousQuality = QualitySettings.renderPipeline;
            var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            var pipeline = UniversalRenderPipelineAsset.Create(renderer);
            try
            {
                GraphicsSettings.defaultRenderPipeline = pipeline;
                QualitySettings.renderPipeline = null;
                UrpProjectSetup.Configure();
                Assert.That(GraphicsSettings.defaultRenderPipeline, Is.SameAs(pipeline));
                GraphicsSettings.defaultRenderPipeline = null;
                QualitySettings.renderPipeline = pipeline;
                UrpProjectSetup.Configure();
                Assert.That(GraphicsSettings.defaultRenderPipeline, Is.Null);
                Assert.That(QualitySettings.renderPipeline, Is.SameAs(pipeline));
            }
            finally
            {
                GraphicsSettings.defaultRenderPipeline = previousGraphics;
                QualitySettings.renderPipeline = previousQuality;
                Object.DestroyImmediate(pipeline);
                Object.DestroyImmediate(renderer);
            }
        }
    }
}

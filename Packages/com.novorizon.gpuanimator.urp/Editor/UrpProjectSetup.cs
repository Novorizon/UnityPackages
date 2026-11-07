using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Framework.GpuAnimation.URP.Editor
{
    public static class UrpProjectSetup
    {
        private const string DirectoryPath = "Assets/Settings/NovorizonURP";
        private const string PipelinePath = DirectoryPath + "/UniversalRenderPipeline.asset";
        private const string RendererPath = DirectoryPath + "/UniversalRenderer.asset";

        [MenuItem("Tools/Novorizon/URP/Configure Default Pipeline")]
        public static void Configure()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UniversalRenderPipelineAsset).Assembly);
            if (package == null || !UrpCompatibility.IsSupported(package.version))
            {
                throw new NotSupportedException("GPU animation supports stable URP 14.0.1 or newer within 14.x. Resolved version: " + (package?.version ?? "unknown") + ". The project package selection was not changed.");
            }

            if (GraphicsSettings.defaultRenderPipeline != null || QualitySettings.renderPipeline != null)
            {
                return;
            }

            Directory.CreateDirectory(DirectoryPath);
            AssetDatabase.Refresh();
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                RejectOccupiedPath(PipelinePath);
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                if (renderer == null)
                {
                    RejectOccupiedPath(RendererPath);
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
                    ResourceReloader.ReloadAllNullIn(renderer, UniversalRenderPipelineAsset.packagePath);
                    AssetDatabase.CreateAsset(renderer, RendererPath);
                }

                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            Debug.Log("[Novorizon URP] Default URP 14 pipeline configured at " + PipelinePath);
        }

        private static void RejectOccupiedPath(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path))
            {
                throw new InvalidOperationException("A different asset already occupies " + path + ". Move it before configuring URP.");
            }
        }
    }
}

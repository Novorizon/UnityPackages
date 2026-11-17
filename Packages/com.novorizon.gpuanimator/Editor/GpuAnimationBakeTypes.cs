using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>定义GPU 动画 Bake 进度回调的参数及返回约定。</summary>
internal delegate bool GpuAnimationBakeProgress(float progress, string message);

/// <summary>提供GPU 动画 Bake 请求的实现。</summary>
internal sealed class GpuAnimationBakeRequest
{
    public string Name;
    public GameObject Model;
    public AnimationClip[] Clips;
    public Texture2D MainTexture;
    public Texture2D MaskTexture;
    public string OutputDirectory;
    public string ShaderName;
    public int MaximumTextureSize = 2048;
}

/// <summary>GPU 动画 Bake对应的结果。携带操作执行结果和相关数据。</summary>
internal sealed class GpuAnimationBakeResult : IDisposable
{
    public AnimationData Data;
    public Mesh Mesh;
    public byte[] AnimationTexturePng;
    public readonly List<string> Warnings = new List<string>();

    /// <summary>结束GPU 动画 Bake 结果对相关资源的持有，并执行该类型的清理流程。</summary>
    public void Dispose()
    {
        if (Mesh != null)
            UnityEngine.Object.DestroyImmediate(Mesh);
        Mesh = null;
    }
}

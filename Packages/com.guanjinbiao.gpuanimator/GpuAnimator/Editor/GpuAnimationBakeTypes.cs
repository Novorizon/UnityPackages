using System;
using System.Collections.Generic;
using UnityEngine;

internal delegate bool GpuAnimationBakeProgress(float progress, string message);

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

internal sealed class GpuAnimationBakeResult : IDisposable
{
    public AnimationData Data;
    public Mesh Mesh;
    public byte[] AnimationTexturePng;
    public readonly List<string> Warnings = new List<string>();

    public void Dispose()
    {
        if (Mesh != null)
            UnityEngine.Object.DestroyImmediate(Mesh);
        Mesh = null;
    }
}

using System;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Plays baked GPU animations on a renderer configured by the caller.
/// Resource loading, materials, and animation selection remain the caller's responsibility.
/// </summary>
public sealed class GpuAnimator : MonoBehaviour
{
    private static readonly int FrameIndexId = Shader.PropertyToID("_FrameIndex");
    private static readonly int BlendFrameIndexId = Shader.PropertyToID("_BlendFrameIndex");

    private Renderer targetRenderer;
    private AnimationData animationData;
    private MaterialPropertyBlock propertyBlock;
    private AnimationClipData currentClip;
    private int currentFrame = -1;
    private int lastFrame = -1;
    private bool isPlaying;
    private bool loopPlayback;
    private double frameRate;
    private double frameTimer;
    private Action<GpuAnimator, object> completionCallback;
    private object completionUserdata;

    public bool IsInitialized => targetRenderer != null && animationData != null;
    public bool IsPlaying => isPlaying && IsInitialized;
    public string CurrentClipName => currentClip != null ? currentClip.name : null;
    public int CurrentFrame => currentFrame;
    public bool AutomaticUpdate { get; set; } = true;

    /// <summary>
    /// Attaches caller-owned playback data and a renderer whose mesh, materials, and
    /// animation texture are already configured. No animation starts automatically.
    /// </summary>
    public bool Initialize(Renderer targetRenderer, AnimationData animationData)
    {
        Clear();
        if (targetRenderer == null || animationData == null || animationData.clips == null || animationData.totalFrame <= 0)
            return false;

        this.targetRenderer = targetRenderer;
        this.animationData = animationData;
        propertyBlock = new MaterialPropertyBlock();
        SetPropertyBlock();
        return true;
    }

    /// <summary>
    /// Uses the renderer on this GameObject with caller-owned animation data.
    /// </summary>
    public bool Initialize(AnimationData animationData)
    {
        return Initialize(GetComponent<Renderer>(), animationData);
    }

    /// <summary>
    /// Refreshes the cached property block after the caller changes renderer properties.
    /// Playback updates reuse this block and only set animation frame properties.
    /// </summary>
    public void SetPropertyBlock()
    {
        if (targetRenderer != null && propertyBlock != null)
            targetRenderer.GetPropertyBlock(propertyBlock);
    }

    /// <summary>
    /// Plays the caller-selected clip with the previous randomized starting phase.
    /// </summary>
    public void Play(string clipName, Action<GpuAnimator, object> callback = null, object userdata = null)
    {
        TryPlay(clipName, callback, userdata, randomizeStart: true);
    }

    /// <summary>
    /// Returns false if initialization is incomplete or the named clip is missing or
    /// invalid. A null loop override uses the baked setting without changing shared data.
    /// Set randomizeStart to stagger the starting phase of crowd animations.
    /// </summary>
    public bool TryPlay(string clipName, Action<GpuAnimator, object> callback = null, object userdata = null, bool randomizeStart = false, bool? loop = null)
    {
        if (!IsInitialized || string.IsNullOrEmpty(clipName) || animationData.clips == null)
            return false;

        for (int i = 0; i < animationData.clips.Length; i++)
        {
            AnimationClipData clip = animationData.clips[i];
            if (clip == null || !string.Equals(clip.name, clipName, StringComparison.Ordinal))
                continue;

            if (clip.startFrame < 0 || clip.endFrame < clip.startFrame ||
                clip.endFrame >= animationData.totalFrame || clip.frameRate <= 0 ||
                float.IsNaN(clip.frameRate) || float.IsInfinity(clip.frameRate))
                return false;

            int offset = randomizeStart ? Random.Range(0, (clip.endFrame - clip.startFrame) / 3) : 0;
            currentClip = clip;
            currentFrame = clip.startFrame + offset;
            lastFrame = currentFrame;
            loopPlayback = loop ?? clip.loop;
            frameRate = clip.frameRate;
            frameTimer = 0;
            completionCallback = callback;
            completionUserdata = userdata;
            isPlaying = true;

            SetPropertyBlock();
            ApplyFrame();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Advances playback explicitly, independent of AutomaticUpdate. Invalid or
    /// nonpositive deltas are ignored. Disable AutomaticUpdate when driving ticks externally.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!IsPlaying || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return;

        frameTimer += deltaTime * frameRate;
        double framesToAdvance = Math.Floor(frameTimer);
        if (framesToAdvance < 1)
            return;

        frameTimer -= framesToAdvance;
        lastFrame = currentFrame;
        int frameCount = currentClip.Length();
        double nextFrame = currentFrame - currentClip.startFrame + framesToAdvance;

        if (loopPlayback)
        {
            currentFrame = currentClip.startFrame + (int)(nextFrame % frameCount);
        }
        else if (nextFrame >= frameCount)
        {
            currentFrame = currentClip.endFrame;
            isPlaying = false;
            frameTimer = 0;
        }
        else
        {
            currentFrame = currentClip.startFrame + (int)nextFrame;
        }

        ApplyFrame();
        if (!isPlaying)
        {
            // Finish the old playback before user code can start or clear another clip.
            Action<GpuAnimator, object> callback = completionCallback;
            object userdata = completionUserdata;
            completionCallback = null;
            completionUserdata = null;
            callback?.Invoke(this, userdata);
        }
    }

    /// <summary>
    /// Stops playback without a completion callback and keeps the displayed pose.
    /// </summary>
    public void Stop()
    {
        isPlaying = false;
        frameTimer = 0;
        completionCallback = null;
        completionUserdata = null;
    }

    /// <summary>
    /// Stops playback and releases the injected references for reuse.
    /// The renderer's materials and displayed pose are left as configured by the caller.
    /// </summary>
    public void Clear()
    {
        Stop();
        targetRenderer = null;
        animationData = null;
        propertyBlock = null;
        currentClip = null;
        currentFrame = -1;
        lastFrame = -1;
        frameRate = 0;
        loopPlayback = false;
    }

    private void Update()
    {
        if (AutomaticUpdate)
            Tick(Time.deltaTime);
    }

    private void ApplyFrame()
    {
        propertyBlock.SetInt(BlendFrameIndexId, lastFrame);
        propertyBlock.SetInt(FrameIndexId, currentFrame);
        targetRenderer.SetPropertyBlock(propertyBlock);
    }
}

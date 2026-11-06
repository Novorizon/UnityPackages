using System;
using System.Collections.Generic;
using Game.Framework;
using UnityEngine;

/// <summary>统一推进处于自动模式的播放器，并汇总所有播放器的动画通知。</summary>
public sealed class GpuAnimatorManager : MonoSingleton<GpuAnimatorManager>
{
    private struct Entry
    {
        internal GpuAnimator Player;
        internal long Version;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<GpuAnimator, long> registered = new Dictionary<GpuAnimator, long>();
    private long nextVersion;
    private bool ticking;
    private float playbackSpeed = 1;
    private bool initialized;
    private static GpuAnimatorManager lifetimeOwner;
    public int Count => registered.Count;
    public bool IsPaused { get; set; }
    public bool AutomaticUpdate { get; set; } = true;

    public float PlaybackSpeed
    {
        get => playbackSpeed;
        set
        {
            if (value < 0 || float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            playbackSpeed = value;
        }
    }

    public event Action<GpuAnimator, string, string> AnimationChanged;
    public event Action<GpuAnimator, string> AnimationStarted;
    public event Action<GpuAnimator, string> AnimationCompleted;
    public event Action<GpuAnimator, string> AnimationStopped;
    /// <summary>取得单例即可使用；不清空已经注册的播放器。</summary>
    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        foreach (var player in FindObjectsOfType<GpuAnimator>(true))
        {
            Register(player);
        }
    }

    protected override void OnStart()
    {
        lifetimeOwner = this;
        Initialize();
    }

    static GpuAnimatorManager()
    {
        SingletonLifecycle.Register(ResetForPlay, order: 100);
    }

    private static void ResetForPlay()
    {
        if (lifetimeOwner != null)
        {
            lifetimeOwner.registered.Clear();
            lifetimeOwner.entries.Clear();
            lifetimeOwner.initialized = false;
            lifetimeOwner.IsPaused = false;
            lifetimeOwner.playbackSpeed = 1;
            lifetimeOwner.AutomaticUpdate = true;
            lifetimeOwner.AnimationChanged = null;
            lifetimeOwner.AnimationStarted = null;
            lifetimeOwner.AnimationCompleted = null;
            lifetimeOwner.AnimationStopped = null;
        }

        lifetimeOwner = null;
        ResetSingletonReference();
    }

    public bool Register(GpuAnimator player)
    {
        if (player == null || !player.AutomaticUpdate || !player.IsPlaying || !player.isActiveAndEnabled || IsDestroying)
        {
            return false;
        }

        if (registered.ContainsKey(player))
        {
            return false;
        }

        long version = ++nextVersion;
        registered.Add(player, version);
        entries.Add(new Entry { Player = player, Version = version });
        return true;
    }

    public bool Unregister(GpuAnimator player)
    {
        if (ReferenceEquals(player, null))
        {
            return false;
        }

        bool removed = registered.Remove(player);
        if (removed && !ticking)
        {
            Compact();
        }

        return removed;
    }

    /// <summary>项目显式驱动时应关闭本 Manager 的 AutomaticUpdate。</summary>
    public void Tick(float deltaTime)
    {
        if (ticking || IsDestroying || IsPaused || playbackSpeed == 0 || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
        {
            return;
        }

        float elapsed = deltaTime * playbackSpeed;
        if (float.IsInfinity(elapsed))
        {
            return;
        }

        ticking = true;
        int limit = entries.Count;
        try
        {
            for (int i = 0; i < limit && !IsDestroying; i++)
            {
                Entry entry = entries[i];
                if (!IsCurrent(entry))
                {
                    continue;
                }

                if (!entry.Player.isActiveAndEnabled || !entry.Player.AutomaticUpdate || !entry.Player.IsPlaying)
                {
                    registered.Remove(entry.Player);
                    continue;
                }

                try
                {
                    entry.Player.Tick(elapsed);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
        finally
        {
            ticking = false;
            Compact();
        }
    }

    private bool IsCurrent(Entry entry)
    {
        return entry.Player != null && registered.TryGetValue(entry.Player, out long current) && current == entry.Version;
    }

    private void Compact()
    {
        int count = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            if (entry.Player == null)
            {
                if (!ReferenceEquals(entry.Player, null))
                {
                    registered.Remove(entry.Player);
                }

                continue;
            }

            if (IsCurrent(entry))
            {
                entries[count++] = entry;
            }
        }

        if (count < entries.Count)
        {
            entries.RemoveRange(count, entries.Count - count);
        }
    }

    private void Update()
    {
        if (AutomaticUpdate)
        {
            Tick(Time.deltaTime);
        }
    }

    internal void NotifyChanged(GpuAnimator player, string previous, string current)
    {
        try
        {
            AnimationChanged?.Invoke(player, previous, current);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    internal void NotifyStarted(GpuAnimator player, string clip)
    {
        Notify(AnimationStarted, player, clip);
    }

    internal void NotifyCompleted(GpuAnimator player, string clip)
    {
        Notify(AnimationCompleted, player, clip);
    }

    internal void NotifyStopped(GpuAnimator player, string clip)
    {
        Notify(AnimationStopped, player, clip);
    }

    private static void Notify(Action<GpuAnimator, string> listeners, GpuAnimator player, string clip)
    {
        try
        {
            listeners?.Invoke(player, clip);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    protected override void OnExit()
    {
        if (IsDestroying && ReferenceEquals(lifetimeOwner, this))
        {
            lifetimeOwner = null;
        }

        registered.Clear();
        initialized = false;
        // 事件回调中销毁 Manager 时，Tick 的 finally 会负责清理数组。
        if (!ticking)
        {
            entries.Clear();
        }

        AnimationChanged = null;
        AnimationStarted = null;
        AnimationCompleted = null;
        AnimationStopped = null;
    }
}

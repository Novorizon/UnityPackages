using System;
using System.Collections.Generic;
using Game.Framework;
using UnityEngine;

/// <summary>统一推进处于自动模式的播放器，并汇总所有播放器的动画通知。</summary>
public sealed partial class GpuAnimatorManager : MonoSingleton<GpuAnimatorManager>
{
    /// <summary>登记 GPU 动画播放器管理器。</summary>
    public static bool Register(GpuAnimator player) =>
        Instance?.RegisterInternal(player) ?? false;

    /// <summary>从已存在的组件注销播放器。</summary>
    public static bool Unregister(GpuAnimator player) =>
        TryGetInstance(out var current) ? current.UnregisterInternal(player) : false;

    /// <summary>在当前组件上执行操作。</summary>
    public static void Tick(float deltaTime) =>
        Instance.TickInternal(deltaTime);

    public static int Count => TryGetInstance(out var current) ? current.registeredCount : 0;

    public static bool IsPaused
    {
        get => TryGetInstance(out var current) ? current.isPaused : false;
        set => Instance.isPaused = value;
    }

    public static bool AutomaticUpdate
    {
        get => Instance.automaticUpdate;
        set => Instance.automaticUpdate = value;
    }

    public static float PlaybackSpeed
    {
        get => Instance.Speed;
        set => Instance.Speed = value;
    }

    /// <summary>在当前调度组件上订阅动画切换通知。</summary>
    public static void SubscribeAnimationChanged(Action<GpuAnimator, string, string> listener)
    {
        Instance.animationChanged += listener;
    }

    /// <summary>从已存在的调度组件移除订阅。</summary>
    public static void UnsubscribeAnimationChanged(Action<GpuAnimator, string, string> listener)
    {
        if (TryGetInstance(out var current))
        {
            current.animationChanged -= listener;
        }
    }

    /// <summary>在当前调度组件上订阅动画开始通知。</summary>
    public static void SubscribeAnimationStarted(Action<GpuAnimator, string> listener)
    {
        Instance.animationStarted += listener;
    }

    /// <summary>从已存在的调度组件移除订阅。</summary>
    public static void UnsubscribeAnimationStarted(Action<GpuAnimator, string> listener)
    {
        if (TryGetInstance(out var current))
        {
            current.animationStarted -= listener;
        }
    }

    /// <summary>在当前调度组件上订阅动画完成通知。</summary>
    public static void SubscribeAnimationCompleted(Action<GpuAnimator, string> listener)
    {
        Instance.animationCompleted += listener;
    }

    /// <summary>从已存在的调度组件移除订阅。</summary>
    public static void UnsubscribeAnimationCompleted(Action<GpuAnimator, string> listener)
    {
        if (TryGetInstance(out var current))
        {
            current.animationCompleted -= listener;
        }
    }

    /// <summary>在当前调度组件上订阅动画停止通知。</summary>
    public static void SubscribeAnimationStopped(Action<GpuAnimator, string> listener)
    {
        Instance.animationStopped += listener;
    }

    /// <summary>从已存在的调度组件移除订阅。</summary>
    public static void UnsubscribeAnimationStopped(Action<GpuAnimator, string> listener)
    {
        if (TryGetInstance(out var current))
        {
            current.animationStopped -= listener;
        }
    }

    /// <summary>保存条目所需的数据与配套操作。</summary>
    private struct Entry
    {
        internal GpuAnimator Player;
        internal long Version;
    }

    private struct Registration
    {
        internal long Version;
        internal int Index;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private readonly Dictionary<GpuAnimator, Registration> registered = new Dictionary<GpuAnimator, Registration>();
    private long nextVersion;
    private bool ticking;
    private bool hasStaleEntries;
    private float playbackSpeed = 1;
    private bool initialized;
    private int registeredCount => registered.Count;
    private bool isPaused { get; set; }
    private bool automaticUpdate { get; set; } = true;

    private float Speed
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

    private event Action<GpuAnimator, string, string> animationChanged;
    private event Action<GpuAnimator, string> animationStarted;
    private event Action<GpuAnimator, string> animationCompleted;
    private event Action<GpuAnimator, string> animationStopped;
    /// <summary>取得单例即可使用；不清空已经注册的播放器。</summary>
    public override void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        foreach (var player in FindObjectsOfType<GpuAnimator>(true))
        {
            RegisterInternal(player);
        }
    }

    protected override void OnStart()
    {
        Initialize();
    }

    /// <summary>登记 GPU 动画播放器管理器。</summary>
    private bool RegisterInternal(GpuAnimator player)
    {
        if (player == null || !player.AutomaticUpdate || !player.IsPlaying || !player.isActiveAndEnabled || IsDestroying)
        {
            return false;
        }

        if (registered.ContainsKey(player))
        {
            return false;
        }

        // A paused or manually driven manager may never Tick. Keep churn bounded while
        // amortizing compaction; removing one player must not scan the whole crowd.
        if (!ticking && hasStaleEntries && entries.Count - registered.Count >= Math.Max(64, registered.Count))
        {
            Compact();
        }

        long version = ++nextVersion;
        registered.Add(player, new Registration { Version = version, Index = entries.Count });
        entries.Add(new Entry { Player = player, Version = version });
        return true;
    }

    /// <summary>撤销登记 GPU 动画播放器管理器。</summary>
    private bool UnregisterInternal(GpuAnimator player)
    {
        if (ReferenceEquals(player, null))
        {
            return false;
        }

        if (!registered.TryGetValue(player, out Registration registration))
        {
            return false;
        }

        registered.Remove(player);
        // Clear the reference immediately, including while paused. The generation still
        // protects a re-registration made from a completion callback during this Tick.
        entries[registration.Index] = default;
        hasStaleEntries = true;
        if (!ticking && registered.Count == 0)
        {
            entries.Clear();
            hasStaleEntries = false;
        }

        return true;
    }

    /// <summary>项目显式驱动时应关闭本 Manager 的 automaticUpdate。</summary>
    /// <remarks>推进已登记的自动播放器，回调中发生的注册和移除按本轮调度规则处理。</remarks>
    private void TickInternal(float deltaTime)
    {
        if (ticking || IsDestroying || isPaused || playbackSpeed == 0 || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
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
                if (entry.Player == null)
                {
                    UnregisterInternal(entry.Player);
                    continue;
                }
                if (!IsCurrent(entry))
                {
                    continue;
                }

                if (!entry.Player.isActiveAndEnabled || !entry.Player.AutomaticUpdate || !entry.Player.IsPlaying)
                {
                    UnregisterInternal(entry.Player);
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
            if (hasStaleEntries)
            {
                Compact();
            }
        }
    }

    private bool IsCurrent(Entry entry)
    {
        return entry.Player != null && registered.TryGetValue(entry.Player, out Registration current) && current.Version == entry.Version;
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

            if (registered.TryGetValue(entry.Player, out Registration registration) && registration.Version == entry.Version)
            {
                registration.Index = count;
                registered[entry.Player] = registration;
                entries[count++] = entry;
            }
        }

        if (count < entries.Count)
        {
            entries.RemoveRange(count, entries.Count - count);
        }
        hasStaleEntries = false;
    }

    private void Update()
    {
        if (automaticUpdate)
        {
            TickInternal(Time.deltaTime);
        }
    }

    internal void NotifyChanged(GpuAnimator player, string previous, string current)
    {
        try
        {
            animationChanged?.Invoke(player, previous, current);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    internal void NotifyStarted(GpuAnimator player, string clip)
    {
        Notify(animationStarted, player, clip);
    }

    internal void NotifyCompleted(GpuAnimator player, string clip)
    {
        Notify(animationCompleted, player, clip);
    }

    internal void NotifyStopped(GpuAnimator player, string clip)
    {
        Notify(animationStopped, player, clip);
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
        registered.Clear();
        initialized = false;
        hasStaleEntries = true;
        // 事件回调中销毁 Manager 时，Tick 的 finally 会负责清理数组。
        if (!ticking)
        {
            entries.Clear();
            hasStaleEntries = false;
        }

        animationChanged = null;
        animationStarted = null;
        animationCompleted = null;
        animationStopped = null;
    }
}

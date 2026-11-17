using UnityEngine;

namespace Game.Ability
{
    /// <summary>Arrival policy is independent from the damage or modifier immunity policy.</summary>
    /// <remarks>定义跟踪目标策略可选的枚举值。</remarks>
    public enum TrackingTargetPolicy
    {
        KeepTracking = 0,
        RevalidateTarget = 1
    }

    /// <summary>Optional presentation boundary for a moving projectile, with a core-owned lifetime.</summary>
    /// <remarks>定义投射物演出的调用契约。</remarks>
    public interface IProjectilePresentation
    {
        /// <summary>播放投射物效果。</summary>
        IProjectilePresentationHandle PlayProjectileEffect(string effectName, Projectile projectile);
    }

    /// <summary>定义投射物演出句柄的调用契约。</summary>
    public interface IProjectilePresentationHandle
    {
        bool IsActive { get; }
        /// <summary>设置位置。</summary>
        void SetPosition(Vector3 position);
        /// <summary>停止投射物演出句柄。</summary>
        void Stop();
    }
}

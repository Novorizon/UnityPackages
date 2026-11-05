using UnityEngine;

namespace Game.Ability
{
    /// <summary>Arrival policy is independent from the damage or modifier immunity policy.</summary>
    public enum TrackingTargetPolicy
    {
        KeepTracking = 0,
        RevalidateTarget = 1
    }

    /// <summary>Optional presentation boundary for a moving projectile, with a core-owned lifetime.</summary>
    public interface IProjectilePresentation
    {
        IProjectilePresentationHandle PlayProjectileEffect(string effectName, Projectile projectile);
    }

    public interface IProjectilePresentationHandle
    {
        bool IsActive { get; }
        void SetPosition(Vector3 position);
        void Stop();
    }
}

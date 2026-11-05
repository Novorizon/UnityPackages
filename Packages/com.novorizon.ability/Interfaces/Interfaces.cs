using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    /// <summary>
    /// Minimal unit surface required by the ability engine.
    /// Game-layer adapters own real HP, death, visibility, and transform data.
    /// </summary>
    public interface IUnit
    {
        int EntityId { get; }
        int TeamId { get; }
        UnitType UnitType { get; }
        bool IsAlive { get; }
        bool IsMagicImmune { get; }
        bool IsInvulnerable { get; }
        Vector3 Position { get; }
        bool IsVisibleToTeam(int teamId);
        // The engine calculates the final result; the adapter applies it to real game state.
        void ApplyDamage(DamageResult result);
        void Heal(HealInfo info);
    }

    /// <summary>
    /// Resource adapter for mana-like costs. In the current TD game this can map to items.
    /// </summary>
    public interface IResourceOwner
    {
        float Mana { get; }
        bool HasMana(float amount);
        bool SpendMana(float amount);
    }

    /// <summary>
    /// World query boundary. The engine asks for broad candidates, then applies ability target rules.
    /// </summary>
    public interface IWorld
    {
        float Time { get; }
        void FindUnits(Vector3 center, float radius, TargetQuery query, IList<IUnit> results);
        bool HasLineOfSight(IUnit viewer, Vector3 position);
    }

    /// <summary>
    /// Optional presentation hooks kept outside gameplay rules.
    /// </summary>
    public interface IPresentation
    {
        void PlayEffect(string effectName, Vector3 position);
        void PlayEffect(string effectName, IUnit target);
        IPresentationHandle PlayPersistentEffect(string effectName, IUnit target);
        void PlaySound(string soundName, Vector3 position);
    }

    /// <summary>Core-owned lifetime token for a persistent presentation request.</summary>
    public interface IPresentationHandle
    {
        string EffectName { get; }
        int TargetEntityId { get; }
        bool IsActive { get; }
        void Stop();
    }

    /// <summary>Optional diagnostics surface implemented by presentation adapters.</summary>
    public interface ITrackedPresentation
    {
        void GetActivePresentationHandles(IList<PresentationHandleInfo> results);
    }

    public sealed class PresentationHandleInfo
    {
        public string EffectName;
        public int TargetEntityId;
        public bool IsActive;
    }
}

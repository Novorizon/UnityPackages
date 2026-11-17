using System.Collections.Generic;
using UnityEngine;

namespace Game.Ability
{
    /// <summary>
    /// Minimal unit surface required by the ability engine.
    /// Game-layer adapters own real HP, death, visibility, and transform data.
    /// </summary>
    /// <remarks>定义单位的调用契约。</remarks>
    public interface IUnit
    {
        int EntityId { get; }
        int TeamId { get; }
        UnitType UnitType { get; }
        bool IsAlive { get; }
        bool IsMagicImmune { get; }
        bool IsInvulnerable { get; }
        Vector3 Position { get; }
        /// <summary>判断是否为可见 To 阵营。</summary>
        bool IsVisibleToTeam(int teamId);
        // The engine calculates the final result; the adapter applies it to real game state.
        /// <summary>应用伤害。</summary>
        void ApplyDamage(DamageResult result);
        /// <summary>处理治疗。</summary>
        void Heal(HealInfo info);
    }

    /// <summary>
    /// Resource adapter for mana-like costs. In the current TD game this can map to items.
    /// </summary>
    /// <remarks>定义资源持有方的调用契约。</remarks>
    public interface IResourceOwner
    {
        float Mana { get; }
        /// <summary>检查是否存在法力。</summary>
        bool HasMana(float amount);
        /// <summary>返回Spend 法力的判断结果。</summary>
        bool SpendMana(float amount);
    }

    /// <summary>
    /// World query boundary. The engine asks for broad candidates, then applies ability target rules.
    /// </summary>
    /// <remarks>定义世界的调用契约。</remarks>
    public interface IWorld
    {
        float Time { get; }
        /// <summary>查找单位。</summary>
        void FindUnits(Vector3 center, float radius, TargetQuery query, IList<IUnit> results);
        /// <summary>检查是否存在直线 Of 视线。</summary>
        bool HasLineOfSight(IUnit viewer, Vector3 position);
    }

    /// <summary>
    /// Optional presentation hooks kept outside gameplay rules.
    /// </summary>
    /// <remarks>定义演出的调用契约。</remarks>
    public interface IPresentation
    {
        /// <summary>播放效果。</summary>
        void PlayEffect(string effectName, Vector3 position);
        /// <summary>播放效果。</summary>
        void PlayEffect(string effectName, IUnit target);
        /// <summary>播放持久化效果。</summary>
        IPresentationHandle PlayPersistentEffect(string effectName, IUnit target);
        /// <summary>播放音效。</summary>
        void PlaySound(string soundName, Vector3 position);
    }

    /// <summary>Core-owned lifetime token for a persistent presentation request.</summary>
    /// <remarks>定义演出句柄的调用契约。</remarks>
    public interface IPresentationHandle
    {
        string EffectName { get; }
        int TargetEntityId { get; }
        bool IsActive { get; }
        /// <summary>停止演出句柄。</summary>
        void Stop();
    }

    /// <summary>Optional diagnostics surface implemented by presentation adapters.</summary>
    /// <remarks>定义已跟踪演出的调用契约。</remarks>
    public interface ITrackedPresentation
    {
        /// <summary>获取活跃演出 Handles。</summary>
        void GetActivePresentationHandles(IList<PresentationHandleInfo> results);
    }

    /// <summary>提供演出句柄信息的实现。</summary>
    public sealed class PresentationHandleInfo
    {
        public string EffectName;
        public int TargetEntityId;
        public bool IsActive;
    }
}

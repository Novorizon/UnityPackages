using UnityEngine;

namespace Game.Ability
{
    /// <summary>
    /// C# extension point for ability-specific behavior.
    /// This replaces a Lua layer while keeping the core runtime data-driven and testable.
    /// </summary>
    /// <remarks>提供技能脚本的实现。</remarks>
    public abstract class AbilityScript
    {
        public Ability Ability { get; private set; }
        public AbilitySystem Engine => Ability != null ? Ability.Engine : null;
        public IUnit Caster => Ability != null ? Ability.Owner : null;

        /// <summary>绑定技能脚本。</summary>
        internal void Bind(Ability ability)
        {
            Ability = ability;
        }

        /// <summary>响应已创建。</summary>
        public virtual void OnCreated() { }
        /// <summary>响应已移除。</summary>
        public virtual void OnRemoved() { }
        /// <summary>响应 Upgrade。</summary>
        public virtual void OnUpgrade() { }

        /// <summary>获取固有状态修饰器名称。</summary>
        public virtual string GetIntrinsicModifierName()
        {
            return Ability != null ? Ability.Definition.IntrinsicModifierName : null;
        }

        /// <summary>Read-only validation used by both previews and actual orders. Do not mutate gameplay state here.</summary>
        /// <remarks>根据传入参数取得施法筛选器。</remarks>
        public virtual CastResult CastFilter(CastContext context)
        {
            return CastResult.Ok();
        }

        /// <summary>获取冷却。</summary>
        public virtual float GetCooldown()
        {
            return Ability != null ? Ability.Definition.Cooldown.GetValue(Ability.Level) : 0f;
        }

        /// <summary>获取法力代价。</summary>
        public virtual float GetManaCost()
        {
            return Ability != null ? Ability.Definition.ManaCost.GetValue(Ability.Level) : 0f;
        }

        /// <summary>获取施法范围。</summary>
        public virtual float GetCastRange()
        {
            return Ability != null ? Ability.Definition.CastRange.GetValue(Ability.Level) : 0f;
        }

        /// <summary>获取范围效果半径。</summary>
        public virtual float GetAoeRadius()
        {
            return Ability != null ? Ability.Definition.AoeRadius.GetValue(Ability.Level) : 0f;
        }

        /// <summary>获取施法点。</summary>
        public virtual float GetCastPoint()
        {
            return Ability != null ? Ability.Definition.CastPoint.GetValue(Ability.Level) : 0f;
        }

        /// <summary>获取引导时间。</summary>
        public virtual float GetChannelTime()
        {
            return Ability != null ? Ability.Definition.ChannelTime.GetValue(Ability.Level) : 0f;
        }

        /// <summary>获取施法后摇。</summary>
        public virtual float GetCastBackswing()
        {
            return Ability != null ? Ability.Definition.CastBackswing.GetValue(Ability.Level) : 0f;
        }

        /// <summary>Return false to cancel before mana, charges, or cooldown are consumed.</summary>
        /// <remarks>响应技能阶段 Start。</remarks>
        public virtual bool OnAbilityPhaseStart(CastContext context) { return true; }
        /// <summary>响应技能阶段已中断。</summary>
        public virtual void OnAbilityPhaseInterrupted() { }

        /// <summary>响应法术 Start。</summary>
        public virtual void OnSpellStart(CastContext context) { }
        /// <summary>响应引导思考。</summary>
        public virtual void OnChannelThink(float deltaTime) { }
        /// <summary>响应引导 Finish。</summary>
        public virtual void OnChannelFinish(bool interrupted) { }
        /// <summary>响应开关。</summary>
        public virtual void OnToggle(bool enabled) { }

        // Return true when custom hit logic wants the projectile removed immediately.
        /// <summary>响应投射物命中。</summary>
        public virtual bool OnProjectileHit(Projectile projectile, IUnit target, Vector3 position)
        {
            return false;
        }

        // Convenience helper for Dota-style named tuning values.
        /// <summary>获取特殊值。</summary>
        protected float GetSpecialValue(string name)
        {
            return Ability != null ? Ability.GetSpecialValue(name) : 0f;
        }
    }

    /// <summary>提供默认值技能脚本的实现。</summary>
    public sealed class DefaultAbilityScript : AbilityScript
    {
    }
}


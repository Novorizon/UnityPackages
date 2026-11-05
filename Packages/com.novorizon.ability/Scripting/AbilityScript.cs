using UnityEngine;

namespace Game.Ability
{
    /// <summary>
    /// C# extension point for ability-specific behavior.
    /// This replaces a Lua layer while keeping the core runtime data-driven and testable.
    /// </summary>
    public abstract class AbilityScript
    {
        public Ability Ability { get; private set; }
        public AbilitySystem Engine => Ability != null ? Ability.Engine : null;
        public IUnit Caster => Ability != null ? Ability.Owner : null;

        internal void Bind(Ability ability)
        {
            Ability = ability;
        }

        public virtual void OnCreated() { }
        public virtual void OnRemoved() { }
        public virtual void OnUpgrade() { }

        public virtual string GetIntrinsicModifierName()
        {
            return Ability != null ? Ability.Definition.IntrinsicModifierName : null;
        }

        /// <summary>Read-only validation used by both previews and actual orders. Do not mutate gameplay state here.</summary>
        public virtual CastResult CastFilter(CastContext context)
        {
            return CastResult.Ok();
        }

        public virtual float GetCooldown()
        {
            return Ability != null ? Ability.Definition.Cooldown.GetValue(Ability.Level) : 0f;
        }

        public virtual float GetManaCost()
        {
            return Ability != null ? Ability.Definition.ManaCost.GetValue(Ability.Level) : 0f;
        }

        public virtual float GetCastRange()
        {
            return Ability != null ? Ability.Definition.CastRange.GetValue(Ability.Level) : 0f;
        }

        public virtual float GetAoeRadius()
        {
            return Ability != null ? Ability.Definition.AoeRadius.GetValue(Ability.Level) : 0f;
        }

        public virtual float GetCastPoint()
        {
            return Ability != null ? Ability.Definition.CastPoint.GetValue(Ability.Level) : 0f;
        }

        public virtual float GetChannelTime()
        {
            return Ability != null ? Ability.Definition.ChannelTime.GetValue(Ability.Level) : 0f;
        }

        public virtual float GetCastBackswing()
        {
            return Ability != null ? Ability.Definition.CastBackswing.GetValue(Ability.Level) : 0f;
        }

        /// <summary>Return false to cancel before mana, charges, or cooldown are consumed.</summary>
        public virtual bool OnAbilityPhaseStart(CastContext context) { return true; }
        public virtual void OnAbilityPhaseInterrupted() { }

        public virtual void OnSpellStart(CastContext context) { }
        public virtual void OnChannelThink(float deltaTime) { }
        public virtual void OnChannelFinish(bool interrupted) { }
        public virtual void OnToggle(bool enabled) { }

        // Return true when custom hit logic wants the projectile removed immediately.
        public virtual bool OnProjectileHit(Projectile projectile, IUnit target, Vector3 position)
        {
            return false;
        }

        // Convenience helper for Dota-style named tuning values.
        protected float GetSpecialValue(string name)
        {
            return Ability != null ? Ability.GetSpecialValue(name) : 0f;
        }
    }

    public sealed class DefaultAbilityScript : AbilityScript
    {
    }
}


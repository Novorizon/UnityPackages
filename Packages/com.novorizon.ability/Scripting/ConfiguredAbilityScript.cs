namespace Game.Ability
{
    /// <summary>
    /// Default script used when an ability is fully described by configured actions.
    /// </summary>
    /// <remarks>提供已配置技能脚本的实现。</remarks>
    public sealed class ConfiguredAbilityScript : AbilityScript
    {
        /// <summary>响应法术 Start。</summary>
        public override void OnSpellStart(CastContext context)
        {
            if (Ability == null || Ability.Definition == null)
            {
                return;
            }

            ActionRunner.Execute(Ability.Definition.Actions, context);
        }

        /// <summary>响应投射物命中。</summary>
        public override bool OnProjectileHit(Projectile projectile, IUnit target, UnityEngine.Vector3 position)
        {
            if (Ability == null || projectile == null || !ReferenceEquals(projectile.Ability, Ability) || target == null) return false;
            CastContext context = ActionRunner.CreateSingleTargetContext(Engine, Ability, projectile.Caster, target, position);
            context.TargetPosition = position;
            context.Direction = projectile.Direction;
            context.Projectile = projectile;
            context.Event = new ModifierEvent { EventType = ModifierEventType.ProjectileHit, Engine = Engine, Source = projectile.Caster, Target = target, Ability = Ability, Projectile = projectile, Position = position };
            ActionRunner.Execute(Ability.Definition.OnProjectileHitActions, context);
            return false;
        }
    }
}


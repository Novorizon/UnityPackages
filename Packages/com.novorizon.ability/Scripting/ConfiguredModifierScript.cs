namespace Game.Ability
{
    /// <summary>
    /// Default script used when a modifier is fully described by configured lifecycle actions.
    /// </summary>
    public sealed class ConfiguredModifierScript : ModifierScript
    {
        public override void OnCreated(ModifierApplyOptions options)
        {
            ActionRunner.Execute(Modifier.Definition.OnCreatedActions, CreateContext(Parent));
        }

        public override void OnRefresh(ModifierApplyOptions options)
        {
            ActionRunner.Execute(Modifier.Definition.OnRefreshActions, CreateContext(Parent));
        }

        public override void OnDestroy()
        {
            // Runtime teardown must release the modifier without executing gameplay actions such
            // as expiry damage, healing, or spawning another modifier after battle settlement.
            if (Engine == null || Engine.IsClearingRuntime || !Modifier.IsRuntimeCurrent)
            {
                return;
            }

            ActionRunner.Execute(Modifier.Definition.OnDestroyActions, CreateContext(Parent));
        }

        public override void OnIntervalThink()
        {
            ActionRunner.Execute(Modifier.Definition.IntervalActions, CreateContext(Parent));
        }

        public override void OnEvent(ModifierEvent modifierEvent)
        {
            if (modifierEvent == null || Modifier == null || Modifier.Definition == null)
            {
                return;
            }

            if (Modifier.Definition.TriggerEventType == ModifierEventType.None || modifierEvent.EventType != Modifier.Definition.TriggerEventType)
            {
                return;
            }

            if (!ShouldHandleEvent(modifierEvent))
            {
                return;
            }

            // Trigger actions prefer the event target but fall back to the modifier parent.
            IUnit target = modifierEvent.Target ?? Parent;
            ActionRunner.Execute(Modifier.Definition.TriggerActions, CreateContext(target, modifierEvent));
        }

        internal bool ShouldHandleEvent(ModifierEvent modifierEvent)
        {
            DamageFlags flags = modifierEvent.DamageResult != null ? modifierEvent.DamageResult.Flags : modifierEvent.DamageInfo != null ? modifierEvent.DamageInfo.Flags : DamageFlags.None;
            if ((flags & Modifier.Definition.TriggerExcludedDamageFlags) != 0) return false;

            if (Modifier.Definition.TriggerEventScope == ModifierEventScope.Global)
            {
                return true;
            }

            switch (modifierEvent.EventType)
            {
                case ModifierEventType.DamageTaken:
                case ModifierEventType.Healed:
                case ModifierEventType.Death:
                case ModifierEventType.Respawn:
                    return IsSameUnit(modifierEvent.Target, Parent);

                case ModifierEventType.DamageDealt:
                case ModifierEventType.AttackStart:
                case ModifierEventType.AttackLanded:
                case ModifierEventType.OrderIssued:
                    return IsSameUnit(modifierEvent.Source, Parent);

                case ModifierEventType.AbilityExecuted:
                case ModifierEventType.AbilityFullyCast:
                case ModifierEventType.ChannelFinished:
                    return IsSameUnit(modifierEvent.Source, Parent) || IsSameUnit(modifierEvent.Source, Caster);

                case ModifierEventType.DamageCalculated:
                case ModifierEventType.ProjectileHit:
                    return IsSameUnit(modifierEvent.Source, Parent) || IsSameUnit(modifierEvent.Target, Parent);

                case ModifierEventType.ModifierAdded:
                case ModifierEventType.ModifierRemoved:
                    return modifierEvent.Modifier != null && IsSameUnit(modifierEvent.Modifier.Parent, Parent);

                default:
                    return false;
            }
        }

        private CastContext CreateContext(IUnit target, ModifierEvent modifierEvent = null)
        {
            CastContext context = ActionRunner.CreateSingleTargetContext(Engine, Ability, Caster, target, Parent != null ? Parent.Position : UnityEngine.Vector3.zero);
            context.Modifier = Modifier;
            context.Level = Modifier.Level;
            context.Event = modifierEvent;
            return context;
        }

        private static bool IsSameUnit(IUnit left, IUnit right)
        {
            return left != null && right != null &&
                   (ReferenceEquals(left, right) || left.EntityId == right.EntityId);
        }
    }
}


namespace Game.Ability
{
    /// <summary>
    /// C# extension point for modifier behavior.
    /// Modifiers are the durable home for buffs, debuffs, auras, states, and property changes.
    /// </summary>
    public abstract class ModifierScript
    {
        public Modifier Modifier { get; private set; }
        public AbilitySystem Engine => Modifier != null ? Modifier.Engine : null;
        public IUnit Parent => Modifier != null ? Modifier.Parent : null;
        public IUnit Caster => Modifier != null ? Modifier.Caster : null;
        public Ability Ability => Modifier != null ? Modifier.Ability : null;
        public int Level => Modifier != null ? Modifier.Level : 1;

        internal void Bind(Modifier modifier)
        {
            Modifier = modifier;
        }

        public virtual void OnCreated(ModifierApplyOptions options) { }
        public virtual void OnRefresh(ModifierApplyOptions options) { }
        public virtual void OnDestroy() { }
        public virtual void OnIntervalThink() { }
        public virtual void OnEvent(ModifierEvent modifierEvent) { }

        public virtual float GetProperty(ModifierProperty property, ModifierPropertyContext context)
        {
            if (Modifier == null || Modifier.Definition == null)
            {
                return 0f;
            }

            // Default modifier properties stack additively by stack count.
            return Modifier.Definition.GetPropertyValue(property, Level) * Modifier.StackCount;
        }

        public virtual bool CheckState(UnitState state)
        {
            // States are bit flags on the definition unless a custom script overrides this.
            return Modifier != null && Modifier.Definition != null && (Modifier.Definition.States & state) != 0;
        }

        public virtual bool IsHidden()
        {
            return Modifier != null && Modifier.Definition != null && Modifier.Definition.IsHidden;
        }

        public virtual bool IsDebuff()
        {
            return Modifier != null && Modifier.Definition != null && Modifier.Definition.IsDebuff;
        }

        public virtual bool IsPurgable()
        {
            return Modifier != null && Modifier.Definition != null && Modifier.Definition.IsPurgable;
        }
    }

    public sealed class DefaultModifierScript : ModifierScript
    {
    }
}


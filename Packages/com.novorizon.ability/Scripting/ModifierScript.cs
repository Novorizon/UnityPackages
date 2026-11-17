namespace Game.Ability
{
    /// <summary>
    /// C# extension point for modifier behavior.
    /// Modifiers are the durable home for buffs, debuffs, auras, states, and property changes.
    /// </summary>
    /// <remarks>提供状态修饰器脚本的实现。</remarks>
    public abstract class ModifierScript
    {
        public Modifier Modifier { get; private set; }
        public AbilitySystem Engine => Modifier != null ? Modifier.Engine : null;
        public IUnit Parent => Modifier != null ? Modifier.Parent : null;
        public IUnit Caster => Modifier != null ? Modifier.Caster : null;
        public Ability Ability => Modifier != null ? Modifier.Ability : null;
        public int Level => Modifier != null ? Modifier.Level : 1;

        /// <summary>绑定状态修饰器脚本。</summary>
        internal void Bind(Modifier modifier)
        {
            Modifier = modifier;
        }

        /// <summary>响应已创建。</summary>
        public virtual void OnCreated(ModifierApplyOptions options) { }
        /// <summary>响应 Refresh。</summary>
        public virtual void OnRefresh(ModifierApplyOptions options) { }
        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        public virtual void OnDestroy() { }
        /// <summary>响应间隔思考。</summary>
        public virtual void OnIntervalThink() { }
        /// <summary>响应事件。</summary>
        public virtual void OnEvent(ModifierEvent modifierEvent) { }

        /// <summary>获取属性。</summary>
        public virtual float GetProperty(ModifierProperty property, ModifierPropertyContext context)
        {
            if (Modifier == null || Modifier.Definition == null)
            {
                return 0f;
            }

            // Default modifier properties stack additively by stack count.
            return Modifier.Definition.GetPropertyValue(property, Level) * Modifier.StackCount;
        }

        /// <summary>检查状态。</summary>
        public virtual bool CheckState(UnitState state)
        {
            // States are bit flags on the definition unless a custom script overrides this.
            return Modifier != null && Modifier.Definition != null && (Modifier.Definition.States & state) != 0;
        }

        /// <summary>判断是否已隐藏。</summary>
        public virtual bool IsHidden()
        {
            return Modifier != null && Modifier.Definition != null && Modifier.Definition.IsHidden;
        }

        /// <summary>判断是否为 Debuff。</summary>
        public virtual bool IsDebuff()
        {
            return Modifier != null && Modifier.Definition != null && Modifier.Definition.IsDebuff;
        }

        /// <summary>判断是否为 Purgable。</summary>
        public virtual bool IsPurgable()
        {
            return Modifier != null && Modifier.Definition != null && Modifier.Definition.IsPurgable;
        }
    }

    /// <summary>提供默认值状态修饰器脚本的实现。</summary>
    public sealed class DefaultModifierScript : ModifierScript
    {
    }
}


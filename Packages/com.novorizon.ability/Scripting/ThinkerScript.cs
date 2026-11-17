namespace Game.Ability
{
    /// <summary>
    /// C# extension point for persistent area logic created by abilities.
    /// </summary>
    /// <remarks>提供持续效果对象脚本的实现。</remarks>
    public abstract class ThinkerScript
    {
        public Thinker Thinker { get; private set; }
        public AbilitySystem Engine => Thinker != null ? Thinker.Engine : null;
        public Ability Ability => Thinker != null ? Thinker.Ability : null;
        public IUnit Caster => Thinker != null ? Thinker.Caster : null;

        /// <summary>绑定持续效果对象脚本。</summary>
        internal void Bind(Thinker thinker)
        {
            Thinker = thinker;
        }

        /// <summary>响应已创建。</summary>
        public virtual void OnCreated() { }
        /// <summary>Unity 销毁对象时执行本对象的清理逻辑。</summary>
        public virtual void OnDestroy() { }
        /// <summary>响应间隔思考。</summary>
        public virtual void OnIntervalThink() { }
        /// <summary>响应思考。</summary>
        public virtual void OnThink(float deltaTime) { }
    }

    /// <summary>提供默认值持续效果对象脚本的实现。</summary>
    public sealed class DefaultThinkerScript : ThinkerScript
    {
    }
}

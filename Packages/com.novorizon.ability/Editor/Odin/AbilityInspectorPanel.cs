#if UNITY_EDITOR && ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using Game.Ability.Configuration;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace Game.Ability.Editor.Odin
{
    /// <summary>Owns one Odin tree for the selected JSON definition. It never owns files or a game engine.</summary>
    /// <remarks>提供技能检视面板面板的实现。</remarks>
    public sealed class AbilityInspectorPanel : IDisposable
    {
        private PropertyTree tree;
        private object target;
        private readonly AbilityFormContext context = new AbilityFormContext();

        /// <summary>设置目标。</summary>
        public void SetTarget(object value, Func<string, IEnumerable<string>> references = null)
        {
            context.References = references;
            if (ReferenceEquals(target, value)) return;
            DisposeTree();
            target = value;
            context.Reset();
            object wrapper = value is JsonAbility ability ? (object)new AbilityTarget { Value = ability }
                : value is JsonModifier modifier ? new ModifierTarget { Value = modifier }
                : value is JsonProjectile projectile ? new ProjectileTarget { Value = projectile }
                : value is JsonAbilityDocument document ? new DocumentTarget { Value = document }
                : null;
            if (wrapper != null) tree = PropertyTree.Create(wrapper);
        }

        /// <summary>绘制技能检视面板面板。</summary>
        public void Draw()
        {
            if (tree == null) return;
            using (AbilityFormContext.Push(context)) tree.Draw(false);
        }

        /// <summary>结束技能检视面板面板对相关资源的持有，并执行该类型的清理流程。</summary>
        public void Dispose()
        {
            DisposeTree();
            target = null;
            context.Reset();
            context.References = null;
        }

        /// <summary>释放树。</summary>
        private void DisposeTree()
        {
            tree?.Dispose();
            tree = null;
        }

        /// <summary>表示技能检视面板面板中的技能目标。</summary>
        private sealed class AbilityTarget { [HideLabel, InlineProperty] public JsonAbility Value; }
        /// <summary>表示技能检视面板面板中的状态修饰器目标。</summary>
        private sealed class ModifierTarget { [HideLabel, InlineProperty] public JsonModifier Value; }
        /// <summary>表示技能检视面板面板中的投射物目标。</summary>
        private sealed class ProjectileTarget { [HideLabel, InlineProperty] public JsonProjectile Value; }
        /// <summary>表示技能检视面板面板中的文档目标。</summary>
        private sealed class DocumentTarget { [HideLabel, InlineProperty] public JsonAbilityDocument Value; }
    }
}
#endif

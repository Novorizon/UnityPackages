#if UNITY_EDITOR && ODIN_INSPECTOR
using System;
using System.Collections.Generic;
using Game.Ability.Configuration;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;

namespace Game.Ability.Editor.Odin
{
    /// <summary>Owns one Odin tree for the selected JSON definition. It never owns files or a game engine.</summary>
    public sealed class AbilityInspectorPanel : IDisposable
    {
        private PropertyTree tree;
        private object target;
        private readonly AbilityFormContext context = new AbilityFormContext();

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

        public void Draw()
        {
            if (tree == null) return;
            using (AbilityFormContext.Push(context)) tree.Draw(false);
        }

        public void Dispose()
        {
            DisposeTree();
            target = null;
            context.Reset();
            context.References = null;
        }

        private void DisposeTree()
        {
            tree?.Dispose();
            tree = null;
        }

        private sealed class AbilityTarget { [HideLabel, InlineProperty] public JsonAbility Value; }
        private sealed class ModifierTarget { [HideLabel, InlineProperty] public JsonModifier Value; }
        private sealed class ProjectileTarget { [HideLabel, InlineProperty] public JsonProjectile Value; }
        private sealed class DocumentTarget { [HideLabel, InlineProperty] public JsonAbilityDocument Value; }
    }
}
#endif

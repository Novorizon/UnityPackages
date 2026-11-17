#if UNITY_EDITOR
using UnityEngine;

namespace Game.Ability.Editor.Odin
{
    // Unity Undo records canonical authoring JSON, never the live runtime or third-party serialization.
    /// <summary>提供技能编辑工作台草稿的实现。</summary>
    internal sealed class AbilityStudioDraft : ScriptableObject
    {
        [SerializeField] public string Json;
    }
}
#endif

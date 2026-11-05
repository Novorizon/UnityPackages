#if UNITY_EDITOR
using UnityEngine;

namespace Game.Ability.Editor.Odin
{
    // Unity Undo records canonical authoring JSON, never the live runtime or third-party serialization.
    internal sealed class AbilityStudioDraft : ScriptableObject
    {
        [SerializeField] public string Json;
    }
}
#endif

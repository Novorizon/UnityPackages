using UnityEngine;

namespace Game.Framework.Story.Unity
{
    [CreateAssetMenu(menuName = "Novorizon/Story/Settings", fileName = "StorySettings")]
    public sealed class StorySettings : ScriptableObject
    {
        public StorySequenceAsset sequence;
        public StoryAssetLibrary assets;
        [Min(0)] public float charactersPerSecond = 40;
        [Min(.1f)] public float videoPrepareTimeoutSeconds = 15;
    }
}

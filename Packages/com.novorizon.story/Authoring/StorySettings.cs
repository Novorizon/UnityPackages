using UnityEngine;

namespace Game.Framework.Story.Unity
{
    /// <summary>剧情对应的设置。保存本模块使用的配置。</summary>
    [CreateAssetMenu(menuName = "Novorizon/Story/Settings", fileName = "StorySettings")]
    public sealed class StorySettings : ScriptableObject
    {
        public StorySequenceAsset sequence;
        public StoryAssetLibrary assets;
        [Min(0)] public float charactersPerSecond = 40;
        [Min(.1f)] public float videoPrepareTimeoutSeconds = 15;
    }
}

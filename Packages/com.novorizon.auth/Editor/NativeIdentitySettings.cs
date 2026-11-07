using UnityEngine;

namespace Game.Framework.Auth.Editor
{
    /// <summary>Public OAuth client identifier, never a client secret.</summary>
    [CreateAssetMenu(menuName = "Novorizon/Native Identity Settings")]
    public sealed class NativeIdentitySettings : ScriptableObject
    {
        public string googleIosClientId;
    }
}

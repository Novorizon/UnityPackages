using UnityEngine;

namespace Game.Framework.Auth.Editor
{
    /// <summary>Public OAuth client identifier, never a client secret.</summary>
    /// <remarks>原生身份对应的设置。保存本模块使用的配置。</remarks>
    [CreateAssetMenu(menuName = "Novorizon/Native Identity Settings")]
    public sealed class NativeIdentitySettings : ScriptableObject
    {
        public string googleIosClientId;
    }
}

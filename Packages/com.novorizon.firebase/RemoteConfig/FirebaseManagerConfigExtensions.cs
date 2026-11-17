namespace Game.Framework.Firebase.Unity
{
    /// <summary>Firebase 管理器配置对应的扩展方法。为相关类型提供扩展方法。</summary>
    public static class FirebaseManagerConfigExtensions
    {
        /// <summary>创建远程配置。</summary>
        public static IRemoteConfiguration CreateRemoteConfig(this FirebaseManager manager) => new FirebaseRemoteConfiguration(manager);
    }
}

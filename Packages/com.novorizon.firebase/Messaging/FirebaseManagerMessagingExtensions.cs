namespace Game.Framework.Firebase.Unity
{
    /// <summary>Firebase 管理器推送对应的扩展方法。为相关类型提供扩展方法。</summary>
    public static class FirebaseManagerMessagingExtensions
    {
        /// <summary>创建推送。</summary>
        public static IPushMessaging CreateMessaging(this FirebaseManager manager) => new FirebasePushMessaging(manager);
    }
}

namespace Game.Framework.Firebase.Unity
{
    /// <summary>Firebase 管理器统计对应的扩展方法。为相关类型提供扩展方法。</summary>
    public static class FirebaseManagerAnalyticsExtensions
    {
        /// <summary>创建统计。</summary>
        public static FirebaseAnalyticsBackend CreateAnalytics(this FirebaseManager manager) => new FirebaseAnalyticsBackend(manager);
    }
}

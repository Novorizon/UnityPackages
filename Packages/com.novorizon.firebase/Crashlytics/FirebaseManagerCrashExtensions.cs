namespace Game.Framework.Firebase.Unity
{
    /// <summary>Firebase 管理器崩溃对应的扩展方法。为相关类型提供扩展方法。</summary>
    public static class FirebaseManagerCrashExtensions
    {
        /// <summary>创建崩溃上报器。</summary>
        public static ICrashReporter CreateCrashReporter(this FirebaseManager manager) => new FirebaseCrashReporter(manager);
    }
}

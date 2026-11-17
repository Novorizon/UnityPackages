namespace Game.Framework.Firebase.Unity
{
    /// <summary>Firebase 管理器认证对应的扩展方法。为相关类型提供扩展方法。</summary>
    public static class FirebaseManagerAuthExtensions
    {
        /// <summary>创建认证后端。</summary>
        public static Game.Framework.Auth.IAuthBackend CreateAuthBackend(this FirebaseManager manager) => new FirebaseAuthBackend(manager);
    }
}

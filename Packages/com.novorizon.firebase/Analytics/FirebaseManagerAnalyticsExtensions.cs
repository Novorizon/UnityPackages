namespace Game.Framework.Firebase.Unity
{
    public static class FirebaseManagerAnalyticsExtensions
    {
        public static FirebaseAnalyticsBackend CreateAnalytics(this FirebaseManager manager) => new FirebaseAnalyticsBackend(manager);
    }
}

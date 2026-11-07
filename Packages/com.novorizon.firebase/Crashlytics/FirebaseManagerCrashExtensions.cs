namespace Game.Framework.Firebase.Unity
{
    public static class FirebaseManagerCrashExtensions
    {
        public static ICrashReporter CreateCrashReporter(this FirebaseManager manager) => new FirebaseCrashReporter(manager);
    }
}

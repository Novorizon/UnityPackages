namespace Game.Framework.Firebase.Unity
{
    public static class FirebaseManagerConfigExtensions
    {
        public static IRemoteConfiguration CreateRemoteConfig(this FirebaseManager manager) => new FirebaseRemoteConfiguration(manager);
    }
}

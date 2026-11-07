namespace Game.Framework.Firebase.Unity
{
    public static class FirebaseManagerMessagingExtensions
    {
        public static IPushMessaging CreateMessaging(this FirebaseManager manager) => new FirebasePushMessaging(manager);
    }
}

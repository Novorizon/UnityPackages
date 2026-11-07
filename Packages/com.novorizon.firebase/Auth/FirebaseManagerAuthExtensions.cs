namespace Game.Framework.Firebase.Unity
{
    public static class FirebaseManagerAuthExtensions
    {
        public static Game.Framework.Auth.IAuthBackend CreateAuthBackend(this FirebaseManager manager) => new FirebaseAuthBackend(manager);
    }
}

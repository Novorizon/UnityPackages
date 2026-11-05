using System;
namespace Game.Framework
{
    [Obsolete("Use UnityThread.Post or UnityThread.RunAsync.")]
    public sealed class Dispatcher : MonoSingleton<Dispatcher>
    {
        public static void Post(Action action) { UnityThread.Post(action); }
    }
}

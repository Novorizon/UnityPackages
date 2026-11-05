using System;
namespace Game.Framework
{
    [Obsolete("Use UnityThread.Post or UnityThread.RunAsync.")]
    public sealed class MessageDispatcher : MonoSingleton<MessageDispatcher>
    {
        public static void Post(Action action) { UnityThread.Post(action); }
    }
}

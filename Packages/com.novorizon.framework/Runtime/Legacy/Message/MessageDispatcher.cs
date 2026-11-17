using System;
namespace Game.Framework
{
    /// <summary>提供消息调度器的实现。</summary>
    [Obsolete("Use UnityThread.Post or UnityThread.RunAsync.")]
    public sealed class MessageDispatcher : MonoSingleton<MessageDispatcher>
    {
        /// <summary>投递消息调度器。</summary>
        public static void Post(Action action) { UnityThread.Post(action); }
    }
}

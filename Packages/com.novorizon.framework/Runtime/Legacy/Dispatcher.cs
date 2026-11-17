using System;
namespace Game.Framework
{
    /// <summary>提供调度器的实现。</summary>
    [Obsolete("Use UnityThread.Post or UnityThread.RunAsync.")]
    public sealed class Dispatcher : MonoSingleton<Dispatcher>
    {
        /// <summary>投递调度器。</summary>
        public static void Post(Action action) { UnityThread.Post(action); }
    }
}

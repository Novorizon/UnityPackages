namespace Game.Framework.Samples.Demo
{
    // The lazy singleton owns only this demo counter. It is not a player/account service.
    /// <summary>提供示例会话的实现。</summary>
    public sealed class DemoSession : Singleton<DemoSession>
    {
        public int Visits { get; private set; }

        /// <summary>登记 Visit。</summary>
        public void RegisterVisit()
        {
            Visits++;
        }
    }
}
